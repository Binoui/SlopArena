using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Issue #149 — TuningDiff engine invariants: identity (same tree → empty diff), known
/// numeric deltas, keyed-array membership (added/removed elements without index cascade),
/// nested identity paths, and byte-identical output for identical inputs.
/// </summary>
public class TuningDiffTests
{
    private static TuningDiffNode Diff(string a, string b)
        => TuningDiff.Compute(JsonNode.Parse(a), JsonNode.Parse(b));

    private static void AssertNoChanges(TuningDiffNode n)
    {
        if (n.Kind is TuningDiffKind.Changed or TuningDiffKind.Added
            or TuningDiffKind.Removed or TuningDiffKind.TypeChanged)
            Assert.Fail($"expected no changes, got {n.Kind}");
        if (n.Children != null)
            foreach (var (_, child) in n.Children)
                AssertNoChanges(child);
    }

    [Fact]
    public void Identity_SameTree_EmptyDiff()
    {
        const string tree = """
            {"character":"FightGuy","percents":[0,30,60],"moves":[
                {"label":"g2 Straight Punch","hitIndex":0,"frame":{"damage":8},"trajectories":[
                    {"pct":0,"kv":12.34,"stun":20,"points":[{"tick":1,"h":0.5}]}]}]}
            """;
        var diff = Diff(tree, tree);
        AssertNoChanges(diff);
    }

    [Fact]
    public void NumericChange_CarriesDeltaAndBothValues()
    {
        var diff = Diff("""{"a":1,"b":{"c":2.5}}""", """{"a":3,"b":{"c":1.5}}""");
        var a = diff["a"]!;
        Assert.Equal(TuningDiffKind.Changed, a.Kind);
        Assert.Equal(2.0, a.Delta);
        Assert.Equal("1", a.OldValue!.ToJsonString());
        Assert.Equal("3", a.NewValue!.ToJsonString());
        var c = diff["b"]!["c"]!;
        Assert.Equal(-1.0, c.Delta);
    }

    [Fact]
    public void StringChange_NoDelta()
    {
        var diff = Diff("""{"label":"g2 Straight Punch"}""", """{"label":"g3 Sweeping Kick"}""");
        var label = diff["label"]!;
        Assert.Equal(TuningDiffKind.Changed, label.Kind);
        Assert.Null(label.Delta);
        Assert.Equal("\"g3 Sweeping Kick\"", label.NewValue!.ToJsonString());
    }

    [Fact]
    public void KeyedArray_GainedLostAndChanged_WithoutIndexCascade()
    {
        const string a = """
            {"moves":[
                {"label":"g1 Low Kick","hitIndex":0,"kv":8.0},
                {"label":"g2 Straight Punch","hitIndex":0,"kv":12.0},
                {"label":"g3 Sweeping Kick","hitIndex":0,"kv":15.0}]}
            """;
        // Candidate: g2's kv changed, g3 removed, g4 added. With index matching every row
        // would shift; keyed matching must leave g1 Unchanged and report the rest exactly.
        const string b = """
            {"moves":[
                {"label":"g1 Low Kick","hitIndex":0,"kv":8.0},
                {"label":"g2 Straight Punch","hitIndex":0,"kv":13.5},
                {"label":"g4 Double Kick","hitIndex":0,"kv":9.0}]}
            """;
        var moves = Diff(a, b)["moves"]!;
        Assert.Equal(TuningDiffKind.Array, moves.Kind);

        var g1 = FindByLabel(moves, "g1 Low Kick");
        Assert.Equal(TuningDiffKind.Unchanged, g1.Kind); // untouched element stays put
        var g2 = FindByLabel(moves, "g2 Straight Punch");
        Assert.Equal(TuningDiffKind.Changed, g2["kv"]!.Kind);
        Assert.Equal(1.5, g2["kv"]!.Delta);
        var g3 = FindByLabel(moves, "g3 Sweeping Kick");
        Assert.Equal(TuningDiffKind.Removed, g3.Kind);
        Assert.Equal("\"g3 Sweeping Kick\"", g3.OldValue!["label"]!.ToJsonString());
        var g4 = FindByLabel(moves, "g4 Double Kick");
        Assert.Equal(TuningDiffKind.Added, g4.Kind);
    }

    [Fact]
    public void KeyedArray_CompositeIdentity_MultiHitMoves()
    {
        const string a = """
            {"moves":[
                {"label":"a1 Double Punch","hitIndex":0,"kv":6.0},
                {"label":"a1 Double Punch","hitIndex":1,"kv":9.0}]}
            """;
        const string b = """
            {"moves":[
                {"label":"a1 Double Punch","hitIndex":0,"kv":6.0},
                {"label":"a1 Double Punch","hitIndex":1,"kv":10.0}]}
            """;
        var moves = Diff(a, b)["moves"]!;
        Assert.Equal(2, moves.Children!.Count);
        // Same label, different hitIndex → distinct keys; hit 0 untouched, hit 1 changed.
        var hit1 = moves.Children!.Single(c => HitIndexOf(c.Value) == 1).Value;
        Assert.Equal(TuningDiffKind.Changed, hit1["kv"]!.Kind);
        Assert.Equal(1.0, hit1["kv"]!.Delta);
    }

    [Fact]
    public void NestedIdentityPath_TrajectoriesKeyedByPct()
    {
        const string a = """
            {"moves":[{"label":"g2 Straight Punch","hitIndex":0,"trajectories":[
                {"pct":0,"kv":12.0},{"pct":30,"kv":18.0}]}]}
            """;
        const string b = """
            {"moves":[{"label":"g2 Straight Punch","hitIndex":0,"trajectories":[
                {"pct":0,"kv":12.0},{"pct":30,"kv":19.5},{"pct":60,"kv":25.0}]}]}
            """;
        var moveEl = Diff(a, b)["moves"]!.Children!.Single().Value; // one move in this fixture
        var trajs = moveEl["trajectories"]!;
        Assert.Equal(3, trajs.Children!.Count);
        var pct0 = trajs.Children!.Single(c => PctOf(c.Value) == 0).Value;
        Assert.Equal(TuningDiffKind.Unchanged, pct0.Kind); // collapsed: kv identical on both sides
        var pct60 = trajs.Children!.Single(c => PctOf(c.Value) == 60).Value;
        Assert.Equal(TuningDiffKind.Added, pct60.Kind);
    }

    [Fact]
    public void IndexFallback_AppendsExtras()
    {
        var diff = Diff("""{"points":[1,2,3]}""", """{"points":[1,2]}""");
        var points = diff["points"]!;
        Assert.Equal(TuningDiffKind.Array, points.Kind);
        Assert.Equal(3, points.Children!.Count);
        Assert.Equal(TuningDiffKind.Unchanged, points.Children![0].Value.Kind);
        Assert.Equal(TuningDiffKind.Removed, points.Children![2].Value.Kind);
    }

    [Fact]
    public void SkipKeys_VolatileMetadataIgnored()
    {
        var diff = Diff(
            """{"generatedAt":"2026-08-17 09:00 UTC","hits":5}""",
            """{"generatedAt":"2026-08-18 09:00 UTC","hits":6}""");
        Assert.Null(diff["generatedAt"]); // skipped entirely
        Assert.Equal(1.0, diff["hits"]!.Delta);
    }

    [Fact]
    public void TypeMismatch_TypeChanged()
    {
        var diff = Diff("""{"x":{"y":1}}""", """{"x":[1,2]}""");
        Assert.Equal(TuningDiffKind.TypeChanged, diff["x"]!.Kind);
    }

    [Fact]
    public void SameInputs_ByteIdenticalSerialization()
    {
        const string a = """{"moves":[{"label":"g1 Low Kick","kv":8.0},{"label":"g2 Straight Punch","kv":12.0}]}""";
        const string b = """{"moves":[{"label":"g1 Low Kick","kv":8.5},{"label":"g2 Straight Punch","kv":12.0}]}""";
        var opts = new JsonSerializerOptions { WriteIndented = true };
        var first = JsonSerializer.Serialize(Diff(a, b), opts);
        var second = JsonSerializer.Serialize(Diff(a, b), opts);
        Assert.Equal(first, second);
        Assert.Contains("8.5", first); // the changed value survives serialization
    }

    /// <summary>An array element's field value — via the engine's helper (Object/Array nodes
    /// expose fields through Children; Added/Removed/Unchanged carry the whole element JSON).</summary>
    private static JsonNode? ElementField(TuningDiffNode element, string field)
        => element.Field(field);

    private static string? LabelOf(TuningDiffNode element)
        => ElementField(element, "label")?.GetValue<string>();

    private static int? HitIndexOf(TuningDiffNode element)
        => ElementField(element, "hitIndex")?.GetValue<int>();

    private static int? PctOf(TuningDiffNode element)
        => ElementField(element, "pct")?.GetValue<int>();

    private static TuningDiffNode FindByLabel(TuningDiffNode moves, string label)
    {
        foreach (var kv in moves.Children!)
            if (LabelOf(kv.Value) == label) return kv.Value;
        throw new Xunit.Sdk.XunitException($"move '{label}' not found in diff");
    }
}
