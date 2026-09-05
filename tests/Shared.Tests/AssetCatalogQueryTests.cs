using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.AssetCatalog;
using Xunit;
using CatalogEntry = SlopArena.AssetCatalog.Program.CatalogEntry;
using CatalogManifest = SlopArena.AssetCatalog.Program.CatalogManifest;
using CatalogPack = SlopArena.AssetCatalog.Program.CatalogPack;
using MaterializeResult = SlopArena.AssetCatalog.Program.MaterializeResult;
using QueryProfile = SlopArena.AssetCatalog.Program.QueryProfile;
using RequiredPack = SlopArena.AssetCatalog.Program.RequiredPack;
using RoleProfile = SlopArena.AssetCatalog.Program.RoleProfile;
using ShortlistItem = SlopArena.AssetCatalog.Program.ShortlistItem;
using Workset = SlopArena.AssetCatalog.Program.Workset;
using WorksetCandidate = SlopArena.AssetCatalog.Program.WorksetCandidate;

namespace SlopArena.Shared.Tests;

public sealed class AssetCatalogQueryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    [Fact]
    public void BuildWorkset_IsDeterministicAndUsesExactScoreReasons()
    {
        var profile = Profile(1);
        var manifest = Manifest();
        var entry = Entry("Pack", "a", "Roof Vent", "Assets/Pack/RoofVent.prefab", "utility",
            new[] { "industrial", "fantasy" }, new[] { "vertical-silhouette" });
        Workset first = Program.BuildWorkset(profile, manifest, new[] { entry });
        Workset second = Program.BuildWorkset(profile, manifest, new[] { entry });
        Assert.Equal(JsonSerializer.Serialize(first, JsonOptions), JsonSerializer.Serialize(second, JsonOptions));
        WorksetCandidate candidate = Assert.Single(first.Candidates);
        Assert.Equal(31, candidate.Score);
        Assert.Equal(new[] { "tag:industrial (+12)", "name:roof (+10)", "name:vent (+10)", "category:utility (+6)", "stage-tag:vertical-silhouette (+5)", "excluded-tag:fantasy (-12)" }, candidate.Reasons);
    }

    [Fact]
    public void ExcludedTagsReduceScoreWithoutRemovingRecord()
    {
        var profile = Profile(1);
        Workset workset = Program.BuildWorkset(profile, Manifest(), new[] { Entry("Pack", "a", "Crate", "Assets/Pack/Crate.prefab", "prop", Array.Empty<string>(), Array.Empty<string>(), new[] { "fantasy" }) });
        Assert.Single(workset.Candidates);
        Assert.Contains("excluded-tag:fantasy (-12)", workset.Candidates[0].Reasons);
    }

    [Fact]
    public void DuplicateIdentityAndMalformedJsonlFailWithLocation()
    {
        CatalogEntry entry = Entry("Pack", "a", "Crate", "Assets/Pack/Crate.prefab", "prop", Array.Empty<string>(), Array.Empty<string>());
        Assert.Throws<InvalidDataException>(() => Program.BuildWorkset(Profile(1), Manifest(), new[] { entry, entry }));
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(new CatalogEntry { Id = "a", Name = "a", Path = "Assets/a.prefab", SourcePack = "Pack", Category = "prop", Tags = Array.Empty<string>(), StageTags = Array.Empty<string>() }) + Environment.NewLine + "not-json" + Environment.NewLine);
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => { Program.ReadEntries(file).ToList(); });
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void FamilyLimitAndRoleQuotaProduceUniqueShortlistAndUnderfillDiagnostic()
    {
        var profile = Profile(2);
        profile.PerFamilyLimit = 1;
        profile.Roles = new[] { new RoleProfile { Name = "one", Quota = 2, Categories = new[] { "prop" }, Terms = new[] { "crate" } } };
        var entries = Enumerable.Range(0, 3).Select(index => Entry("Pack", index.ToString(), "Crate_" + (index + 1), "Assets/Pack/Crate_" + (index + 1) + ".prefab", "prop", Array.Empty<string>(), Array.Empty<string>())).ToArray();
        Workset workset = Program.BuildWorkset(profile, Manifest(), entries);
        Assert.Single(workset.Shortlist);
        Assert.Contains(workset.Diagnostics, x => x.Code == "ROLE_UNDERFILLED");
        Assert.Equal(1, workset.Shortlist.Select(x => (x.SourcePack, x.Id)).Distinct().Count());
    }

    [Fact]
    public void RequiredPacksContainOnlyShortlistPacks()
    {
        var profile = Profile(1);
        profile.Roles = new[] { new RoleProfile { Name = "one", Quota = 1, Categories = new[] { "prop" } } };
        CatalogEntry selected = Entry("Selected", "a", "Crate", "Assets/Selected/Crate.prefab", "prop", Array.Empty<string>(), Array.Empty<string>());
        CatalogEntry unselected = Entry("Other", "b", "Crate", "Assets/Other/Crate.prefab", "terrain", Array.Empty<string>(), Array.Empty<string>());
        Workset workset = Program.BuildWorkset(profile, Manifest(), new[] { selected, unselected });
        Assert.Equal(new[] { "Selected" }, workset.RequiredPacks.Select(x => x.SourcePack));
        Assert.StartsWith("Assets/LowPolyMegaBundle/", workset.RequiredPacks[0].SourceArchive);
    }

    [Fact]
    public void ProbeIsDeterministicBoundedAndRetainsUnderfill()
    {
        var profile = Profile(3);
        profile.PerFamilyLimit = 1;
        profile.Roles = new[]
        {
            new RoleProfile { Name = "utility", Quota = 3, Categories = new[] { "utility" } },
            new RoleProfile { Name = "structure", Quota = 3, Categories = new[] { "structure" } }
        };
        var entries = new[]
        {
            Entry("Pack", "a", "Vent_1", "Assets/Pack/Vent_1.prefab", "utility", Array.Empty<string>(), Array.Empty<string>()),
            Entry("Pack", "b", "Vent_2", "Assets/Pack/Vent_2.prefab", "utility", Array.Empty<string>(), Array.Empty<string>()),
            Entry("Pack", "c", "Roof", "Assets/Pack/Roof.prefab", "structure", Array.Empty<string>(), Array.Empty<string>())
        };
        Workset first = Program.BuildProbeWorkset(profile, Manifest(), entries, 2);
        Workset second = Program.BuildProbeWorkset(profile, Manifest(), entries, 2);
        Assert.Equal(JsonSerializer.Serialize(first, JsonOptions), JsonSerializer.Serialize(second, JsonOptions));
        Assert.True(first.Shortlist.Count <= profile.Roles.Length * 2);
        Assert.Equal(first.Shortlist.Count, first.Shortlist.Select(x => x.Id).Distinct().Count());
        Assert.All(first.Shortlist, x => Assert.Equal("selected", x.SelectionStatus));
        Assert.Contains(first.Diagnostics, x => x.Code == "ROLE_UNDERFILLED");

    }
    [Fact]
    public void MaterializerExtractsMetaIsIdempotentAndAbortsAllCopiesOnConflict()
    {
        string root = Path.Combine(Path.GetTempPath(), "asset-catalog-" + Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "Unity");
        string archive = Path.Combine(project, "Assets/LowPolyMegaBundle/Pack/Pack.unitypackage");
        string staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(Path.GetDirectoryName(archive));
        try
        {
            WritePackage(archive, ("a", "Assets/Pack/A.prefab", "a-asset", "a-meta"), ("b", "Assets/Pack/B.prefab", "b-asset", "b-meta"));
            Workset workset = MaterializeWorkset("Pack", "Assets/LowPolyMegaBundle/Pack/Pack.unitypackage", "Assets/Pack/A.prefab");
            MaterializeResult first = Program.Materialize(workset, project, staging);
            Assert.True(first.Success, string.Join(";", first.Diagnostics.Select(x => x.Code + ":" + x.Message)));
            Assert.Equal(4, first.Packs.Single().Copied);
            MaterializeResult second = Program.Materialize(workset, project, staging);
            Assert.True(second.Success);
            Assert.Equal(0, second.Packs.Single().Copied);
            Assert.Equal(4, second.Packs.Single().Unchanged);

            File.Delete(Path.Combine(project, "Assets/Pack/B.prefab.meta"));
            File.WriteAllText(Path.Combine(project, "Assets/Pack/B.prefab"), "conflict");
            Workset conflict = MaterializeWorkset("Pack", "Assets/LowPolyMegaBundle/Pack/Pack.unitypackage", "Assets/Pack/A.prefab", "Assets/Pack/B.prefab");
            MaterializeResult failed = Program.Materialize(conflict, project, staging);
            Assert.False(failed.Success);
            Assert.Contains(failed.Diagnostics, x => x.Code == "IMPORT_CONFLICT");
            Assert.False(File.Exists(Path.Combine(project, "Assets/Pack/B.prefab.meta")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MaterializerRejectsPathTraversal()
    {
        string root = Path.Combine(Path.GetTempPath(), "asset-catalog-" + Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "Unity");
        string archive = Path.Combine(project, "Assets/LowPolyMegaBundle/Pack/Pack.unitypackage");
        Directory.CreateDirectory(Path.GetDirectoryName(archive));
        try
        {
            WritePackage(archive, ("a", "Assets/../Escape.prefab", "asset", "meta"));
            MaterializeResult result = Program.Materialize(MaterializeWorkset("Pack", "Assets/LowPolyMegaBundle/Pack/Pack.unitypackage", "Assets/Escape.prefab"), project, Path.Combine(root, "staging"));
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "INVALID_ARCHIVE");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static QueryProfile Profile(int quota) => new()
    {
        SchemaVersion = 1, Concept = "test", CandidateLimit = 100, PerFamilyLimit = 2,
        PreferredTags = new[] { "industrial" }, Terms = new[] { "roof", "vent" }, ExcludedTags = new[] { "fantasy" },
        Roles = new[] { new RoleProfile { Name = "role", Quota = quota, Categories = new[] { "utility" }, StageTags = new[] { "vertical-silhouette" }, Terms = new[] { "roof", "vent" } } }
    };

    private static CatalogManifest Manifest() => new() { SchemaVersion = 2, EntriesFile = "prefabs.jsonl", Packs = new[]
    {
        new CatalogPack { SourcePack = "Pack", SourceArchive = "client/Unity/Assets/LowPolyMegaBundle/Pack/Pack.unitypackage" },
        new CatalogPack { SourcePack = "Selected", SourceArchive = "client/Unity/Assets/LowPolyMegaBundle/Selected/Selected.unitypackage" },
        new CatalogPack { SourcePack = "Other", SourceArchive = "client/Unity/Assets/LowPolyMegaBundle/Other/Other.unitypackage" }
    }};

    private static CatalogEntry Entry(string pack, string id, string name, string path, string category, string[] tags, string[] stageTags, string[] excluded = null) => new()
    {
        SourcePack = pack, Id = id, Name = name, Path = path, Category = category, Tags = tags.Concat(excluded ?? Array.Empty<string>()).ToArray(), StageTags = stageTags
    };

    private static Workset MaterializeWorkset(string pack, string archive, params string[] paths)
    {
        string[] shortlistPaths = paths;
        return new Workset
        {
            SchemaVersion = 1, Concept = "test", RequiredPacks = new List<RequiredPack> { new RequiredPack { SourcePack = pack, SourceArchive = archive } },
            Shortlist = shortlistPaths.Select((path, index) => new ShortlistItem { SourcePack = pack, Id = ((char)('a' + index)).ToString(), Name = "Prefab", Path = path, SelectionStatus = "selected" }).ToList()
        };
    }

    private static void WritePackage(string path, params (string Id, string Path, string Asset, string Meta)[] files)
    {
        using FileStream file = File.Create(path);
        using GZipStream gzip = new(file, CompressionMode.Compress);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var item in files)
        {
            AddTar(tar, item.Id + "/pathname", item.Path);
            AddTar(tar, item.Id + "/asset", item.Asset);
            AddTar(tar, item.Id + "/asset.meta", item.Meta);
        }
    }

    private static void AddTar(TarWriter tar, string name, string content)
    {
        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
    }
}
