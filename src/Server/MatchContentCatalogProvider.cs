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
            var packages = new Dictionary<string, CookedCharacterPackageLoadResult>(StringComparer.Ordinal);
            foreach (var rosterEntry in _manifest.Entries)
            {
                if (rosterEntry.Requirement.Version == "legacy-1") continue;
                string directory = Path.Combine(_cookedRoot, rosterEntry.PackageId);
                packages[rosterEntry.PackageId] = CookedCharacterPackageLoader.LoadDirectory(directory, rosterEntry.Requirement);
            }
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
