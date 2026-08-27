using System;
using System.Collections.Generic;
using System.IO;
using SlopArena.Shared;

namespace SlopArena.Server;

/// <summary>Builds an immutable content catalog for each match assignment.</summary>
public sealed class MatchContentCatalogProvider
{
    private readonly string _cookedRoot;
    private readonly string _manifestPath;
    private readonly BuiltInRosterManifest _manifest;

    public BuiltInRosterManifest Manifest => _manifest;

    public MatchContentCatalogProvider(string cookedRoot = "content-cooked", string manifestPath = "content-cooked/roster/manifest.json")
    {
        _cookedRoot = cookedRoot ?? throw new ArgumentNullException(nameof(cookedRoot));
        _manifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
        _manifest = BuiltInRosterManifestCodec.Load(_manifestPath);
    }

    public bool TryBuild(out MatchContentCatalog? catalog, out MatchContentHandleMap? handleMap, out string? error)
    {
        catalog = null; handleMap = null; error = null;
        try
        {
            var fightGuy = _manifest.Resolve(CharacterClass.FightGuy);
            if (fightGuy == null) { error = "Built-in roster has no FightGuy entry."; return false; }
            string directory = Path.Combine(_cookedRoot, fightGuy.PackageId);
            var loaded = CookedCharacterPackageLoader.LoadDirectory(directory, fightGuy.Requirement);
            var packages = new Dictionary<string, CookedCharacterPackageLoadResult>(StringComparer.Ordinal) { [fightGuy.PackageId] = loaded };
            var result = new MatchContentCatalogBuilder().Build(_manifest, packages, new LegacyCharacterCatalogAdapter());
            if (!result.IsValid || result.Catalog == null)
            {
                error = string.Join("; ", result.Diagnostics);
                return false;
            }
            catalog = result.Catalog;
            var records = new List<MatchContentHandleRecord>();
            foreach (var entry in catalog.Entries)
                if (entry.LegacySelector.HasValue)
                    records.Add(new MatchContentHandleRecord(entry.Handle, entry.LegacySelector.Value, entry.Identity, entry.DisplayName));
            handleMap = new MatchContentHandleMap(MatchContentHandleMap.CurrentSchemaVersion, records);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
