using System;
using System.IO;
using System.Linq;
using UnityEngine;
using SlopArena.Shared;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlopArena.Client;

public static class LocalContentResolverSelfTest
{
#if UNITY_EDITOR
    [MenuItem("Tools/SlopArena/Tests/Local Content Resolver")]
#endif
    public static void Run()
    {
        string priorDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            var resolver = LocalContentResolver.CreateDefault();
            var playerResolver = LocalContentResolver.CreateForMode(LocalContentMode.Player);
            if (!Path.IsPathRooted(resolver.ProjectRoot) || resolver.ContentRoots.Any(root => !Path.IsPathRooted(root)) ||
                playerResolver.ContentRoots.Count != 1 || !Path.IsPathRooted(playerResolver.ContentRoots[0]))
                throw new InvalidOperationException("Local resolver returned an invalid development/player root policy.");
            var roster = resolver.ResolveRoster();
            if (!roster.Success || roster.Roster == null)
                throw new InvalidOperationException("Valid rooted cooked roster could not be resolved: " + Format(roster));

            var nilusLegacy = resolver.ResolveLegacy(CharacterClass.Nilus);
            if (!nilusLegacy.Success || nilusLegacy.LegacyEntry == null ||
                nilusLegacy.LegacyEntry.LegacySelector != CharacterClass.Nilus)
                throw new InvalidOperationException("Valid rooted Nilus legacy snapshot could not be resolved: " + Format(nilusLegacy));

            var kistuLegacy = resolver.ResolveLegacy(CharacterClass.Kistu);
            if (kistuLegacy.Success || kistuLegacy.LegacyEntry != null ||
                !kistuLegacy.Diagnostics.Any(d => d.Code == "content.legacy.selector"))
                throw new InvalidOperationException("Kistu legacy resolution did not fail closed.");

            var mankiLegacy = resolver.ResolveLegacy(CharacterClass.Manki);
            if (mankiLegacy.Success || mankiLegacy.LegacyEntry != null ||
                !mankiLegacy.Diagnostics.Any(d => d.Code == "content.legacy.selector"))
                throw new InvalidOperationException("Manki legacy resolution did not fail closed.");

            var fightGuyLegacy = resolver.ResolveLegacy(CharacterClass.FightGuy);
            if (fightGuyLegacy.Success || fightGuyLegacy.LegacyEntry != null ||
                !fightGuyLegacy.Diagnostics.Any(d => d.Code == "content.legacy.selector"))
                throw new InvalidOperationException("FightGuy legacy resolution did not fail closed: " + Format(fightGuyLegacy));

            var unavailableLegacy = resolver.ResolveLegacy((CharacterClass)255);
            if (unavailableLegacy.Success || unavailableLegacy.LegacyEntry != null ||
                !unavailableLegacy.Diagnostics.Any(d => d.Code == "content.legacy.selector"))
                throw new InvalidOperationException("Unavailable legacy selector did not fail closed: " + Format(unavailableLegacy));

            var unavailable = resolver.ResolveCookedPackage("unavailable-package");
            if (unavailable.Success || unavailable.Roster != null || !unavailable.Diagnostics.Any(d => d.Code == "content.package.missing"))
                throw new InvalidOperationException("Unavailable package resolved unexpectedly or did not fail closed: " + Format(unavailable));

            Debug.Log("[LocalContentResolverSelfTest] Passed rooted roster, cooked packages, remaining Nilus legacy snapshot, unavailable package, and cwd-independence checks.");

        }
        finally
        {
            Directory.SetCurrentDirectory(priorDirectory);
        }
    }

    private static string Format(LocalContentResolution resolution)
        => string.Join("; ", resolution.Diagnostics.Select(d => $"{d.Code} ({d.Path}): {d.Message}"));
}
