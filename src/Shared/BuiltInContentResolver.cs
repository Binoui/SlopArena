using System;
using System.Collections.Generic;
using System.IO;

namespace SlopArena.Shared;

/// <summary>Resolves a built-in selector through the same cooked/legacy catalog boundary used by matches.</summary>
public static class BuiltInContentResolver
{
    public static MatchContentEntry Resolve(CharacterClass selector, string cookedRoot = "content-cooked")
    {
        cookedRoot = ResolveRoot(cookedRoot);
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cookedRoot, "roster", "manifest.json"));
        var roster = manifest.Resolve(selector) ?? throw new InvalidDataException($"Roster selector '{selector}' is not available.");
        var packages = new Dictionary<string, CookedCharacterPackageLoadResult>(StringComparer.Ordinal);
        var fightGuy = manifest.Resolve(CharacterClass.FightGuy) ?? throw new InvalidDataException("Roster has no FightGuy package.");
        var loaded = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cookedRoot, fightGuy.PackageId), fightGuy.Requirement);
        if (!loaded.IsValid) throw new InvalidDataException(string.Join("; ", loaded.Diagnostics.Select(x => $"{x.Code}:{x.Message}")));
        packages[fightGuy.PackageId] = loaded;
        var result = new MatchContentCatalogBuilder().Build(manifest, packages, new LegacyCharacterCatalogAdapter());
        if (!result.IsValid || result.Catalog == null) throw new InvalidDataException(string.Join("; ", result.Diagnostics.Select(x => $"{x.Code}:{x.Message}")));
        return result.Catalog.Resolve(selector) ?? throw new InvalidDataException($"Catalog selector '{selector}' is unavailable.");
    }

    private static string ResolveRoot(string root)
    {
        string RelativeManifest(string basePath) => Path.Combine(basePath, root, "roster", "manifest.json");
        string RelativePackage(string basePath) => Path.Combine(basePath, root, "fightguy", "manifest.json");
        if (Path.IsPathRooted(root) && File.Exists(Path.Combine(root, "roster", "manifest.json"))) return root;
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 10 && current != null; i++, current = current.Parent)
            if (File.Exists(RelativeManifest(current.FullName)) && File.Exists(RelativePackage(current.FullName)))
                return Path.Combine(current.FullName, root);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && directory != null; i++, directory = directory.Parent)
            if (File.Exists(RelativeManifest(directory.FullName)) && File.Exists(RelativePackage(directory.FullName)))
                return Path.Combine(directory.FullName, root);
        return root;
    }
}
