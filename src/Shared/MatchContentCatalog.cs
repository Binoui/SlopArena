using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed record MatchContentPackageRequirement(string PackageId, string Version, string CookedContentHash, string PackageHash);
public sealed record MatchContentIdentity(string PackageId, string Version, string SourceHash, string CookedContentHash, string PackageHash);
public readonly record struct ContentHandle(ushort Value)
{
    public bool IsValid => Value != 0;
    public override string ToString() => Value.ToString();
}
public sealed record BuiltInRosterEntrySource(CharacterClass Selector, string PackageId);
public sealed record BuiltInRosterManifestSource(ushort SchemaVersion, IReadOnlyList<BuiltInRosterEntrySource> Entries);
public sealed record BuiltInRosterEntry(CharacterClass Selector, string PackageId, MatchContentPackageRequirement Requirement);

public sealed class BuiltInRosterManifest
{
    public const ushort CurrentSchemaVersion = 1;
    public ushort SchemaVersion { get; }
    public IReadOnlyList<BuiltInRosterEntry> Entries { get; }

    public BuiltInRosterManifest(ushort schemaVersion, IReadOnlyList<BuiltInRosterEntry> entries)
    {
        if (schemaVersion == 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        SchemaVersion = schemaVersion;
        Entries = new ReadOnlyCollection<BuiltInRosterEntry>(new List<BuiltInRosterEntry>(entries ?? throw new ArgumentNullException(nameof(entries))));
    }

    public bool TryGetBySelector(CharacterClass selector, out BuiltInRosterEntry entry)
    {
        entry = Entries.FirstOrDefault(x => x.Selector == selector)!;
        return entry != null;
    }

    public bool TryGetByPackageId(string packageId, out BuiltInRosterEntry entry)
    {
        entry = Entries.FirstOrDefault(x => string.Equals(x.PackageId, packageId, StringComparison.Ordinal))!;
        return entry != null;
    }

    public BuiltInRosterEntry? Resolve(CharacterClass selector) => TryGetBySelector(selector, out var value) ? value : null;
    public BuiltInRosterEntry? ResolvePackage(string packageId) => TryGetByPackageId(packageId, out var value) ? value : null;
}

public sealed class MatchContentEntry
{
    public ContentHandle Handle { get; }
    public CharacterClass? LegacySelector { get; }
    public MatchContentIdentity Identity { get; }
    public string DisplayName { get; }
    public CharacterDefinition Definition { get; }
    public BakedAnimationData? BakedAnimation { get; }
    public BakedAnimationData? Baked => BakedAnimation;
    public CookedCharacterPackage? CookedCharacterPackage { get; }

    public MatchContentEntry(ContentHandle handle, CharacterClass? legacySelector, MatchContentIdentity identity,
        string displayName, CharacterDefinition definition, BakedAnimationData? bakedAnimation = null,
        CookedCharacterPackage? cookedCharacterPackage = null)
    {
        if (!handle.IsValid) throw new ArgumentOutOfRangeException(nameof(handle));
        Handle = handle;
        LegacySelector = legacySelector;
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Definition = MatchContentInternals.CloneDefinition(definition ?? throw new ArgumentNullException(nameof(definition)));
        BakedAnimation = MatchContentInternals.CloneBaked(bakedAnimation);
        CookedCharacterPackage = cookedCharacterPackage;
    }
}

public sealed class MatchContentCatalog
{
    private readonly IReadOnlyDictionary<ContentHandle, MatchContentEntry> _byHandle;
    private readonly IReadOnlyDictionary<CharacterClass, MatchContentEntry> _bySelector;
    private readonly IReadOnlyDictionary<string, MatchContentEntry> _byPackage;
    public IReadOnlyList<MatchContentEntry> Entries { get; }
    public IReadOnlyDictionary<ContentHandle, MatchContentEntry> HandleMap => _byHandle;

    internal MatchContentCatalog(IReadOnlyList<MatchContentEntry> entries)
    {
        Entries = new ReadOnlyCollection<MatchContentEntry>(new List<MatchContentEntry>(entries));
        _byHandle = new ReadOnlyDictionary<ContentHandle, MatchContentEntry>(Entries.ToDictionary(x => x.Handle));
        _bySelector = new ReadOnlyDictionary<CharacterClass, MatchContentEntry>(Entries.Where(x => x.LegacySelector.HasValue).ToDictionary(x => x.LegacySelector!.Value));
        _byPackage = new ReadOnlyDictionary<string, MatchContentEntry>(Entries.ToDictionary(x => x.Identity.PackageId, StringComparer.Ordinal));
    }

    public MatchContentEntry? Resolve(ContentHandle handle) => _byHandle.TryGetValue(handle, out var e) ? e : null;
    public MatchContentEntry? Resolve(CharacterClass selector) => _bySelector.TryGetValue(selector, out var e) ? e : null;
    public MatchContentEntry? ResolvePackage(string packageId) => packageId != null && _byPackage.TryGetValue(packageId, out var e) ? e : null;
}

public sealed class MatchContentCatalogBuildResult
{
    public MatchContentCatalog? Catalog { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool IsValid => Catalog != null && Diagnostics.All(x => x.Severity != CharacterDiagnosticSeverity.Error);
    public bool HasErrors => Diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error);
    public MatchContentCatalogBuildResult(MatchContentCatalog? catalog, IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        Catalog = catalog;
        Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics ?? Array.Empty<CharacterDiagnostic>()));
    }
}

public sealed class MatchContentCatalogBuilder
{
    public MatchContentCatalogBuildResult Build(BuiltInRosterManifest manifest,
        IReadOnlyDictionary<string, CookedCharacterPackageLoadResult> cookedPackages,
        LegacyCharacterCatalogAdapter legacyAdapter)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (manifest == null) { diagnostics.Add(Error("catalog.manifest.missing", "manifest", "Roster manifest is required.")); return new(null, diagnostics); }
        if (manifest.SchemaVersion != BuiltInRosterManifest.CurrentSchemaVersion)
            diagnostics.Add(Error("catalog.manifest.schema", "manifest.schemaVersion", "Unsupported roster manifest schema."));
        ValidateManifest(manifest, diagnostics);
        var byPackage = new Dictionary<string, MatchContentEntry>(StringComparer.Ordinal);
        var bySelector = new HashSet<CharacterClass>();
        foreach (var roster in manifest.Entries)
        {
            if (!bySelector.Add(roster.Selector)) diagnostics.Add(Error("catalog.selector.duplicate", roster.Selector.ToString(), "Duplicate roster selector."));
            if (!IsStablePackageId(roster.PackageId) || !byPackage.TryAdd(roster.PackageId, null!))
            {
                if (byPackage.ContainsKey(roster.PackageId)) diagnostics.Add(Error("catalog.package.duplicate", roster.PackageId, "Duplicate package ID."));
                continue;
            }
            if (roster.Requirement == null) continue;
            if (roster.Requirement.Version == "legacy-1")
            {
                if (legacyAdapter == null) { diagnostics.Add(Error("catalog.legacy.adapter-missing", roster.PackageId, "Legacy adapter is required.")); continue; }
                if (!legacyAdapter.TrySnapshot(roster.Selector, out var snapshot, out var snapshotDiagnostics))
                {
                    diagnostics.AddRange(snapshotDiagnostics);
                    continue;
                }
                if (!IdentityMatches(roster.Requirement, snapshot.Identity, roster.PackageId, diagnostics)) continue;
                byPackage[roster.PackageId] = new MatchContentEntry(new ContentHandle(1), snapshot.LegacySelector, snapshot.Identity, snapshot.DisplayName, snapshot.Definition, snapshot.BakedAnimation);
            }
            else
            {
                if (cookedPackages == null || !cookedPackages.TryGetValue(roster.PackageId, out var loaded) || loaded == null || !loaded.IsValid || loaded.Package == null)
                {
                    diagnostics.Add(Error("catalog.package.missing", roster.PackageId, "Cooked package is missing or invalid."));
                    continue;
                }
                if (!IdentityMatches(roster.Requirement, loaded.Identity, roster.PackageId, diagnostics)) continue;
                byPackage[roster.PackageId] = new MatchContentEntry(new ContentHandle(1), roster.Selector, loaded.Identity, loaded.Package.Definition.DisplayName, loaded.ToCharacterDefinition(roster.Selector), loaded.BakedAnimation, loaded.Package);
            }
        }
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error)) return new(null, diagnostics);
        var ordered = byPackage.Values.OrderBy(x => x.Identity.PackageId, StringComparer.Ordinal).ToList();
        var entries = new List<MatchContentEntry>(ordered.Count);
        for (ushort i = 0; i < ordered.Count; i++)
        {
            var x = ordered[i];
            entries.Add(new MatchContentEntry(new ContentHandle((ushort)(i + 1)), x.LegacySelector, x.Identity, x.DisplayName, x.Definition, x.BakedAnimation, x.CookedCharacterPackage));
        }
        return new MatchContentCatalogBuildResult(new MatchContentCatalog(entries), diagnostics);
    }

    public MatchContentCatalogBuildResult Build(BuiltInRosterManifest manifest, IReadOnlyDictionary<string, CookedCharacterPackageLoadResult> cookedPackages)
        => Build(manifest, cookedPackages, new LegacyCharacterCatalogAdapter());

    private static void ValidateManifest(BuiltInRosterManifest manifest, List<CharacterDiagnostic> d)
    {
        var required = new[] { CharacterClass.Manki, CharacterClass.FightGuy, CharacterClass.Kistu, CharacterClass.Bonk };
        foreach (var selector in required)
            if (manifest.Resolve(selector) == null) d.Add(Error("catalog.selector.missing", selector.ToString(), "Built-in roster selector is missing."));
        foreach (var entry in manifest.Entries)
        {
            if (entry.Selector == CharacterClass.None) d.Add(Error("catalog.selector.invalid", "entries", "None is not a valid built-in selector."));
            if (!IsStablePackageId(entry.PackageId)) d.Add(Error("catalog.package.invalid", "entries.packageId", "Package ID is not a stable lowercase ID."));
            if (entry.Requirement == null || entry.Requirement.PackageId != entry.PackageId || !IsSha(entry.Requirement.CookedContentHash) || !IsSha(entry.Requirement.PackageHash) || string.IsNullOrWhiteSpace(entry.Requirement.Version))
                d.Add(Error("catalog.requirement.invalid", entry.PackageId, "Package requirement is incomplete or invalid."));
        }
    }

    private static bool IdentityMatches(MatchContentPackageRequirement requirement, MatchContentIdentity identity, string packageId, List<CharacterDiagnostic> d)
    {
        if (identity.PackageId != requirement.PackageId || identity.Version != requirement.Version || identity.CookedContentHash != requirement.CookedContentHash || identity.PackageHash != requirement.PackageHash)
        {
            d.Add(Error("catalog.identity.mismatch", packageId, "Loaded package identity does not match roster requirement."));
            return false;
        }
        return true;
    }

    internal static CharacterDiagnostic Error(string code, string path, string message) => new(CharacterDiagnosticSeverity.Error, code, path, message);
    internal static bool IsSha(string value) => value != null && value.Length == 64 && value.All(x => (x >= '0' && x <= '9') || (x >= 'a' && x <= 'f'));
    public static bool IsStablePackageId(string value) => !string.IsNullOrEmpty(value) && value.All(x => (x >= 'a' && x <= 'z') || (x >= '0' && x <= '9') || x == '.' || x == '-') && char.IsLetter(value[0]);
}

public sealed class LegacyCharacterCatalogAdapter
{
    private static readonly CharacterClass[] LegacySelectors = { CharacterClass.Manki, CharacterClass.Kistu, CharacterClass.Nilus };

    public MatchContentEntry Snapshot(CharacterClass selector)
    {
        if (!TrySnapshot(selector, out var entry, out var diagnostics))
            throw new InvalidDataException(string.Join("; ", diagnostics.Select(x => x.Message)));
        return entry;
    }

    public bool TrySnapshot(CharacterClass selector, out MatchContentEntry entry, out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        entry = null!;
        var d = new List<CharacterDiagnostic>();
        if (!LegacySelectors.Contains(selector)) { d.Add(MatchContentCatalogBuilder.Error("catalog.legacy.selector", selector.ToString(), "Selector is not a legacy built-in.")); diagnostics = d; return false; }
        CharacterDefinition source;
        try { source = CharacterRegistry.Get(selector); }
        catch (Exception ex) { d.Add(MatchContentCatalogBuilder.Error("catalog.legacy.lookup", selector.ToString(), ex.Message)); diagnostics = d; return false; }
        if (source == null) { d.Add(MatchContentCatalogBuilder.Error("catalog.legacy.null", selector.ToString(), "Registry returned no definition.")); diagnostics = d; return false; }
        try
        {
            string json = CharacterContentSerializer.Serialize(selector.ToString().ToLowerInvariant(), source);
            var clone = CharacterContentSerializer.Load(json);
            string hash = MatchContentInternals.Sha256(Encoding.UTF8.GetBytes(json));
            var identity = new MatchContentIdentity(selector.ToString().ToLowerInvariant(), "legacy-1", hash, hash, hash);
            entry = new MatchContentEntry(new ContentHandle(1), selector, identity, clone.DisplayName, clone, LoadBaked(clone));
            diagnostics = d;
            return true;
        }
        catch (Exception ex)
        {
            d.Add(MatchContentCatalogBuilder.Error("catalog.legacy.snapshot", selector.ToString(), ex.Message));
            diagnostics = d;
            return false;
        }
    }

    private static BakedAnimationData? LoadBaked(CharacterDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.BakedDataPath)) return null;
        try
        {
            string path = definition.BakedDataPath.Replace("res://", "", StringComparison.Ordinal);
            return BakedAnimationData.LoadFromBin(File.ReadAllBytes(path));
        }
        catch { return null; }
    }
}

public static class BuiltInRosterManifestCodec
{
    public static BuiltInRosterManifestSource ParseSource(string json)
    {
        var root = ParseObject(json, "roster");
        ushort schema = RequiredUInt16(root, "schemaVersion");
        var entries = ParseEntries(root, false);
        return new BuiltInRosterManifestSource(schema, entries);
    }

    public static string SerializeSource(BuiltInRosterManifestSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        using var stream = new MemoryStream();
        using var w = new Utf8JsonWriter(stream);
        w.WriteStartObject(); w.WriteNumber("schemaVersion", source.SchemaVersion); w.WritePropertyName("entries"); w.WriteStartArray();
        foreach (var x in source.Entries) { w.WriteStartObject(); w.WriteString("selector", x.Selector.ToString()); w.WriteString("packageId", x.PackageId); w.WriteEndObject(); }
        w.WriteEndArray(); w.WriteEndObject(); w.Flush(); return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static BuiltInRosterManifest ParseCooked(string json)
    {
        var root = ParseObject(json, "roster");
        ushort schema = RequiredUInt16(root, "schemaVersion");
        var entries = new List<BuiltInRosterEntry>();
        var array = RequiredArray(root, "entries");
        foreach (var item in array.EnumerateArray())
        {
            var e = StrictObject(item, "entries[]", "selector", "packageId", "requirement");
            var selector = ParseSelector(RequiredString(e, "selector"));
            string packageId = RequiredString(e, "packageId");
            var r = StrictObject(RequiredProperty(e, "requirement"), "requirement", "packageId", "version", "cookedContentHash", "packageHash");
            entries.Add(new BuiltInRosterEntry(selector, packageId, new MatchContentPackageRequirement(packageId, RequiredString(r, "version"), RequiredString(r, "cookedContentHash"), RequiredString(r, "packageHash"))));
        }
        return new BuiltInRosterManifest(schema, entries);
    }

    public static string Serialize(BuiltInRosterManifest manifest)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        using var stream = new MemoryStream(); using var w = new Utf8JsonWriter(stream);
        w.WriteStartObject(); w.WriteNumber("schemaVersion", manifest.SchemaVersion); w.WritePropertyName("entries"); w.WriteStartArray();
        foreach (var x in manifest.Entries) { w.WriteStartObject(); w.WriteString("selector", x.Selector.ToString()); w.WriteString("packageId", x.PackageId); w.WritePropertyName("requirement"); w.WriteStartObject(); w.WriteString("packageId", x.Requirement.PackageId); w.WriteString("version", x.Requirement.Version); w.WriteString("cookedContentHash", x.Requirement.CookedContentHash); w.WriteString("packageHash", x.Requirement.PackageHash); w.WriteEndObject(); w.WriteEndObject(); }
        w.WriteEndArray(); w.WriteEndObject(); w.Flush(); return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static BuiltInRosterManifest Load(string path) => ParseCooked(File.ReadAllText(path));

    private static List<BuiltInRosterEntrySource> ParseEntries(Dictionary<string, JsonElement> root, bool cooked)
    {
        var result = new List<BuiltInRosterEntrySource>();
        foreach (var item in RequiredArray(root, "entries").EnumerateArray())
        {
            var e = StrictObject(item, "entries[]", "selector", "packageId");
            result.Add(new BuiltInRosterEntrySource(ParseSelector(RequiredString(e, "selector")), RequiredString(e, "packageId")));
        }
        return result;
    }

    private static CharacterClass ParseSelector(string value) => value switch
    {
        "Manki" => CharacterClass.Manki, "FightGuy" => CharacterClass.FightGuy, "Kistu" => CharacterClass.Kistu, "Bonk" => CharacterClass.Bonk, "Nilus" => CharacterClass.Nilus,
        _ => throw new InvalidDataException("Unknown roster selector.")
    };
    private static Dictionary<string, JsonElement> ParseObject(string json, string path)
    {
        try { using var doc = JsonDocument.Parse(json); return StrictObject(doc.RootElement, path, "schemaVersion", "entries"); }
        catch (JsonException ex) { throw new InvalidDataException("Malformed roster JSON.", ex); }
    }
    private static Dictionary<string, JsonElement> StrictObject(JsonElement element, string path, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{path} must be an object.");
        var set = new HashSet<string>(allowed, StringComparer.Ordinal); var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject()) if (!set.Contains(p.Name) || !result.TryAdd(p.Name, p.Value.Clone())) throw new InvalidDataException($"Invalid or duplicate field {path}.{p.Name}.");
        foreach (var name in allowed) if (!result.ContainsKey(name)) throw new InvalidDataException($"Missing field {path}.{name}.");
        return result;
    }
    private static JsonElement RequiredProperty(Dictionary<string, JsonElement> p, string name) => p[name];
    private static JsonElement RequiredArray(Dictionary<string, JsonElement> p, string name) => p[name].ValueKind == JsonValueKind.Array ? p[name] : throw new InvalidDataException($"{name} must be an array.");
    private static string RequiredString(Dictionary<string, JsonElement> p, string name) => p[name].ValueKind == JsonValueKind.String ? p[name].GetString()! : throw new InvalidDataException($"{name} must be a string.");
    private static ushort RequiredUInt16(Dictionary<string, JsonElement> p, string name) => p[name].TryGetUInt16(out var v) ? v : throw new InvalidDataException($"{name} must be an unsigned 16-bit integer.");
}

internal static class MatchContentInternals
{
    public static string Sha256(byte[] bytes) { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(bytes).Select(x => x.ToString("x2"))); }
    public static CharacterDefinition CloneDefinition(CharacterDefinition source)
    {
        string json = CharacterContentSerializer.Serialize(source.Class.ToString().ToLowerInvariant(), source);
        var clone = CharacterContentSerializer.Load(json);
        if (source.CookedSlots != null)
            clone.CookedSlots = new ReadOnlyCollection<CookedSlotDefinition>(new List<CookedSlotDefinition>(source.CookedSlots));
        for (int i = 0; i < AbilitySlots.Count; i++)
        {
            CopyAbility(source.GetSlotAbility(i), clone.GetSlotAbility(i));
            CopyAbility(source.GetSlotAbility(i, true), clone.GetSlotAbility(i, true));
        }
        return clone;
    }

    private static void CopyAbility(AbilitySpec? source, AbilitySpec? target)
    {
        if (source == null || target == null) return;
        target.AimMovement = source.AimMovement;
        target.ChargeHoldTicks = source.ChargeHoldTicks;
        target.AnimSpeed = source.AnimSpeed;
        target.SpecialEffectKeys = source.SpecialEffectKeys == null ? null : (string[])source.SpecialEffectKeys.Clone();
        target.AnimationNames = source.AnimationNames == null ? null : (string[])source.AnimationNames.Clone();
        target.BoneTrails = source.BoneTrails == null ? null : (BoneTrailDef[])source.BoneTrails.Clone();
        target.Stages = CloneStages(source.Stages);
        target.ChargedStages = source.ChargedStages == null ? null : CloneStages(source.ChargedStages);
    }

    private static AttackStage[] CloneStages(AttackStage[] source)
    {
        var copy = (AttackStage[])source.Clone();
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i].HitboxEvents = source[i].HitboxEvents == null ? Array.Empty<HitboxEvent>() : (HitboxEvent[])source[i].HitboxEvents.Clone();
            copy[i].BoneTrails = source[i].BoneTrails == null ? null : (BoneTrailDef[])source[i].BoneTrails.Clone();
        }
        return copy;
    }
    public static BakedAnimationData? CloneBaked(BakedAnimationData? source)
    {
        if (source == null) return null;
        return new BakedAnimationData { BoneNames = (string[])source.BoneNames.Clone(), Animations = source.Animations.Select(a => new BakedAnimationData.BakedAnim { Name = a.Name, FrameCount = a.FrameCount, Frames = a.Frames.Select(f => (float[])f.Clone()).ToArray() }).ToArray() };
    }
}
