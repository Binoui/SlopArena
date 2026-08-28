using System.IO;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

public sealed class MatchContentCatalogTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void CommittedFightGuyPackage_LoadsWithExactIdentity()
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        var roster = manifest.Resolve(CharacterClass.FightGuy)!;
        Assert.NotNull(roster);
        var result = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked/fightguy"), roster.Requirement);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics));
        Assert.Equal("fightguy", result.Identity.PackageId);
        Assert.Equal("08dd4df2185af704b3d563656fb930c2d2d18e8fe394b5ff520df1d926592ef4", result.Identity.CookedContentHash);
        Assert.Equal(16, result.Package!.Definition.Slots.Count);
        Assert.NotNull(result.BakedAnimation);
    }

    [Fact]
    public void LegacyAdapter_SnapshotsAreIndependent()
    {
        var adapter = new LegacyCharacterCatalogAdapter();
        var first = adapter.Snapshot(CharacterClass.Manki);
        var second = adapter.Snapshot(CharacterClass.Manki);
        Assert.NotSame(first.Definition, second.Definition);
        Assert.Equal(first.Identity, second.Identity);
        first.Definition.DisplayName = "mutated";
        Assert.NotEqual("mutated", second.Definition.DisplayName);
    }

    [Fact]
    public void Catalog_AssignsHandlesByStablePackageId()
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        var fightGuy = manifest.Resolve(CharacterClass.FightGuy)!;
        var loaded = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked/fightguy"), fightGuy.Requirement);
        var kistu = manifest.Resolve(CharacterClass.Kistu)!;
        var loadedKistu = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked/kistu"), kistu.Requirement);
        var result = new MatchContentCatalogBuilder().Build(manifest, new Dictionary<string, CookedCharacterPackageLoadResult> { ["fightguy"] = loaded, ["kistu"] = loadedKistu }, new LegacyCharacterCatalogAdapter());
        var catalog = result.Catalog!;
        Assert.NotNull(catalog);
        Assert.Equal(4, catalog.Entries.Count);
        Assert.Equal(1, catalog.ResolvePackage("fightguy")!.Handle.Value);
    }

    [Fact]
    public void MissingCookedKistuPackage_FailsClosed()
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        var fightGuy = manifest.Resolve(CharacterClass.FightGuy)!;
        var loaded = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked/fightguy"), fightGuy.Requirement);
        var result = new MatchContentCatalogBuilder().Build(manifest, new Dictionary<string, CookedCharacterPackageLoadResult> { ["fightguy"] = loaded }, new LegacyCharacterCatalogAdapter());
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x => x.Code == "catalog.package.missing" && x.Path == "kistu");
    }

    [Fact]
    public void HandleMap_RoundTripsDeterministically()
    {
        var identity = new MatchContentIdentity("fightguy", "0.0.0-dev", new string('a',64), new string('b',64), new string('c',64));
        var map = new MatchContentHandleMap(1, new[] { new MatchContentHandleRecord(new ContentHandle(2), CharacterClass.FightGuy, identity, "FightGuy") });
        var json = MatchContentHandleMapCodec.Serialize(map);
        Assert.True(MatchContentHandleMapCodec.TryParse(json, out var parsed));
        Assert.Equal(json, MatchContentHandleMapCodec.Serialize(parsed!));
    }
    [Fact]
    public void CookedLoader_RejectsMissingRequiredPayload()
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        var requirement = manifest.Resolve(CharacterClass.FightGuy)!.Requirement;
        var files = new Dictionary<string, byte[]>
        {
            ["manifest.json"] = File.ReadAllBytes(Path.Combine(Root, "content-cooked/fightguy/manifest.json")),
            ["character.runtime.json"] = File.ReadAllBytes(Path.Combine(Root, "content-cooked/fightguy/character.runtime.json")),
            ["client.bindings"] = File.ReadAllBytes(Path.Combine(Root, "content-cooked/fightguy/client.bindings"))
        };
        Assert.False(CookedCharacterPackageLoader.LoadFiles(files, requirement).IsValid);
    }

    [Fact]
    public void HandleMap_RejectsUnknownFieldsAndDuplicateHandles()
    {
        var unknown = "{\"schemaVersion\":1,\"entries\":[],\"extra\":true}";
        Assert.False(MatchContentHandleMapCodec.TryParse(unknown, out _));
        var duplicate = "{\"schemaVersion\":1,\"entries\":[{\"handle\":1,\"selector\":\"FightGuy\",\"identity\":{\"packageId\":\"fightguy\",\"version\":\"1\",\"sourceHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"cookedContentHash\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"packageHash\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"},\"displayName\":\"FightGuy\"},{\"handle\":1,\"selector\":\"Manki\",\"identity\":{\"packageId\":\"manki\",\"version\":\"1\",\"sourceHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"cookedContentHash\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"packageHash\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"},\"displayName\":\"Manki\"}]}";
        Assert.False(MatchContentHandleMapCodec.TryParse(duplicate, out _));
    }
}
