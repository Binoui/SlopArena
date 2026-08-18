using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SlopArena.Shared;

/// <summary>Classification of one node pair in a tuning diff.</summary>
public enum TuningDiffKind
{
    /// <summary>Scalar present on both sides with equal value.</summary>
    Unchanged,
    /// <summary>Scalar present on both sides with different value (numeric changes carry <see cref="TuningDiffNode.Delta"/>).</summary>
    Changed,
    /// <summary>Only in the candidate tree.</summary>
    Added,
    /// <summary>Only in the baseline tree.</summary>
    Removed,
    /// <summary>Same key but incompatible shapes (object vs array vs scalar).</summary>
    TypeChanged,
    /// <summary>Object — children keyed by property name.</summary>
    Object,
    /// <summary>Array — children keyed by stable identity or index.</summary>
    Array,
}

/// <summary>One node of the diff tree produced by <see cref="TuningDiff.Compute"/>.</summary>
public sealed record TuningDiffNode(
    TuningDiffKind Kind,
    JsonNode? OldValue,
    JsonNode? NewValue,
    double? Delta)
{
    /// <summary>Object children keyed by property name; array children keyed by identity key or index.</summary>
    public List<KeyValuePair<string, TuningDiffNode>>? Children { get; init; }

    public TuningDiffNode? this[string key]
    {
        get
        {
            if (Children == null) return null;
            foreach (var c in Children)
                if (c.Key == key) return c.Value;
            return null;
        }
    }

    /// <summary>Read a field of an array element diff: Object/Array nodes expose fields as
    /// children; Added/Removed/Unchanged elements carry the whole element JSON in NewValue/OldValue.</summary>
    public JsonNode? Field(string field)
        => Kind is TuningDiffKind.Added or TuningDiffKind.Removed or TuningDiffKind.Unchanged
            ? (NewValue ?? OldValue) is JsonObject jo ? jo[field] : null
            : this[field]?.OldValue ?? this[field]?.NewValue;
}

/// <summary>
/// Deterministic structural diff of two JSON trees (issue #149). Walks both trees in
/// parallel; scalar changes carry old/new values plus a numeric delta; arrays are matched
/// by stable identity fields (move label, hit index, percent, follow-up, seed-keyed rows)
/// so an added/removed element reports as Added/Removed instead of shifting every index.
/// Identity fields are looked up by the array's path (e.g. "moves.trajectories" → pct);
/// arrays without an identity entry fall back to index matching. Same inputs → same tree;
/// the caller serializes it for byte-identical diff JSON.
/// </summary>
public static class TuningDiff
{
    /// <summary>Default identity fields per array path, matching the report schemas of
    /// MoveDataReport and SelfPlayReport (camelCase keys as serialized).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultIdentityFields = new Dictionary<string, string[]>
    {
        ["moves"] = new[] { "label", "hitIndex" },
        ["moves.trajectories"] = new[] { "pct" },
        ["moves.diEscape"] = new[] { "pct" },
        ["moves.diEscape.variants"] = new[] { "direction" },
        ["trueCombos"] = new[] { "move", "state" },
        ["trueCombos.edges"] = new[] { "followUp", "pct" },
        ["comboDensity"] = new[] { "pct" },
        ["perMove"] = new[] { "label" },
        ["envelope"] = new[] { "label" },
        ["whiffGrid"] = new[] { "gx", "gy" },
    };

    /// <summary>Property names excluded from the diff at any depth (volatile metadata, e.g. "generatedAt").</summary>
    public static readonly string[] DefaultSkipKeys = { "generatedAt" };

    /// <param name="baseline">First tree (the "before" tuning).</param>
    /// <param name="candidate">Second tree (the "after" tuning).</param>
    /// <param name="identityFields">Array-path → identity field names; defaults to <see cref="DefaultIdentityFields"/>.</param>
    /// <param name="skipKeys">Property names ignored at any depth; defaults to <see cref="DefaultSkipKeys"/>.</param>
    public static TuningDiffNode Compute(JsonNode? baseline, JsonNode? candidate,
        IReadOnlyDictionary<string, string[]>? identityFields = null,
        IReadOnlyCollection<string>? skipKeys = null)
    {
        var identity = identityFields ?? DefaultIdentityFields;
        var skip = skipKeys ?? DefaultSkipKeys;
        return Diff("", baseline, candidate, identity, skip);
    }

    private static TuningDiffNode Diff(string path, JsonNode? a, JsonNode? b,
        IReadOnlyDictionary<string, string[]> identity, IReadOnlyCollection<string> skip)
    {
        if (a == null && b == null) return new TuningDiffNode(TuningDiffKind.Unchanged, null, null, null);
        if (a == null) return new TuningDiffNode(TuningDiffKind.Added, null, b, null);
        if (b == null) return new TuningDiffNode(TuningDiffKind.Removed, a, null, null);

        if (a is JsonObject oa && b is JsonObject ob)
        {
            // Union of keys, baseline order first, then candidate-only — one loop so the
            // deconstruction iteration variables stay out of each other's scope.
            var keys = new List<string>(oa.Count + ob.Count);
            foreach (var (k, _) in oa) keys.Add(k);
            foreach (var (k, _) in ob) if (!oa.ContainsKey(k)) keys.Add(k);

            var children = new List<KeyValuePair<string, TuningDiffNode>>(keys.Count);
            foreach (var key in keys)
            {
                if (skip.Contains(key)) continue;
                string childPath = path.Length == 0 ? key : path + "." + key;
                oa.TryGetPropertyValue(key, out var aNode);
                ob.TryGetPropertyValue(key, out var bNode);
                children.Add(new(key, Diff(childPath, aNode, bNode, identity, skip)));
            }
            // No descendant changed → collapse to Unchanged (carries both full subtrees).
            if (children.All(c => c.Value.Kind == TuningDiffKind.Unchanged))
                return new TuningDiffNode(TuningDiffKind.Unchanged, a, b, null);
            return new TuningDiffNode(TuningDiffKind.Object, null, null, null) { Children = children };
        }

        if (a is JsonArray aa && b is JsonArray ab)
            return DiffArray(path, aa, ab, identity, skip);

        if (a is JsonValue av && b is JsonValue bv)
        {
            if (ValuesEqual(av, bv))
                return new TuningDiffNode(TuningDiffKind.Unchanged, a, b, null);
            return new TuningDiffNode(TuningDiffKind.Changed, a, b, NumericDelta(av, bv));
        }

        return new TuningDiffNode(TuningDiffKind.TypeChanged, a, b, null);
    }

    private static TuningDiffNode DiffArray(string path, JsonArray a, JsonArray b,
        IReadOnlyDictionary<string, string[]> identity, IReadOnlyCollection<string> skip)
    {
        if (identity.TryGetValue(path, out var fields)
            && CanKey(a, fields) && CanKey(b, fields)
            && UniqueKeys(a, fields) && UniqueKeys(b, fields))
        {
            var keysA = Keyed(a, fields);
            var keysB = Keyed(b, fields);
            var children = new List<KeyValuePair<string, TuningDiffNode>>();
            foreach (var (key, node) in keysA)
            {
                var bNode = keysB.TryGetValue(key, out var bn) ? bn : null;
                // Element diff keeps the array's path so nested arrays (e.g. "moves.trajectories")
                // resolve their own identity entries.
                children.Add(new(key, Diff(path, node, bNode, identity, skip)));
            }
            foreach (var (key, node) in keysB)
            {
                if (keysA.ContainsKey(key)) continue;
                children.Add(new(key, Diff(path, null, node, identity, skip)));
            }
            if (children.All(c => c.Value.Kind == TuningDiffKind.Unchanged))
                return new TuningDiffNode(TuningDiffKind.Unchanged, a, b, null);
            return new TuningDiffNode(TuningDiffKind.Array, null, null, null) { Children = children };
        }

        // Index fallback: identical-length arrays diff element-wise; extras append.
        var byIndex = new List<KeyValuePair<string, TuningDiffNode>>();
        int common = Math.Min(a.Count, b.Count);
        for (int i = 0; i < common; i++)
            byIndex.Add(new(i.ToString(), Diff(path, a[i], b[i], identity, skip)));
        for (int i = common; i < a.Count; i++)
            byIndex.Add(new(i.ToString(), new TuningDiffNode(TuningDiffKind.Removed, a[i], null, null)));
        for (int i = common; i < b.Count; i++)
            byIndex.Add(new(i.ToString(), new TuningDiffNode(TuningDiffKind.Added, null, b[i], null)));
        if (byIndex.All(c => c.Value.Kind == TuningDiffKind.Unchanged))
            return new TuningDiffNode(TuningDiffKind.Unchanged, a, b, null);
        return new TuningDiffNode(TuningDiffKind.Array, null, null, null) { Children = byIndex };
    }

    /// <summary>True when every element is an object carrying every identity field (as a value).</summary>
    private static bool CanKey(JsonArray arr, string[] fields)
    {
        if (arr.Count == 0) return true;
        foreach (var el in arr)
        {
            if (el is not JsonObject o) return false;
            foreach (var f in fields)
                if (!o.TryGetPropertyValue(f, out var v) || v is not JsonValue) return false;
        }
        return true;
    }

    /// <summary>Element → identity key (fields joined with a separator). Duplicate keys on one side
    /// would be ambiguous — callers treat that as a degenerate array (fall back to index).</summary>
    private static Dictionary<string, JsonNode> Keyed(JsonArray arr, string[] fields)
    {
        var map = new Dictionary<string, JsonNode>();
        foreach (var el in arr)
        {
            var o = (JsonObject)el!;
            var parts = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                parts[i] = o[fields[i]]!.ToJsonString();
            map[string.Join("\u001f", parts)] = el!;
        }
        return map;
    }

    /// <summary>Identity keys must be unique per side for keyed matching to be sound; when not,
    /// the array uses index matching. (None of the report arrays violate this today.)</summary>
    private static bool UniqueKeys(JsonArray arr, string[] fields)
    {
        if (arr.Count < 2) return true;
        var seen = new HashSet<string>();
        foreach (var el in arr)
        {
            var o = (JsonObject)el!;
            var sb = new System.Text.StringBuilder();
            foreach (var f in fields)
            {
                sb.Append(o[f]!.ToJsonString());
                sb.Append('\u001f');
            }
            if (!seen.Add(sb.ToString())) return false;
        }
        return true;
    }

    private static bool ValuesEqual(JsonValue a, JsonValue b)
    {
        if (a.TryGetValue<double>(out var x) && b.TryGetValue<double>(out var y)) return x == y;
        if (a.TryGetValue<bool>(out var ba) && b.TryGetValue<bool>(out var bb)) return ba == bb;
        return a.ToJsonString() == b.ToJsonString();
    }

    private static double? NumericDelta(JsonValue a, JsonValue b)
    {
        if (a.TryGetValue<double>(out var x) && b.TryGetValue<double>(out var y)) return y - x;
        return null;
    }
}
