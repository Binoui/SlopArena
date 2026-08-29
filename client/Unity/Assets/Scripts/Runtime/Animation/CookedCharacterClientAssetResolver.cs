using System;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Animation
{
    public static class CookedCharacterClientAssetResolver
    {
#if UNITY_EDITOR
        private static readonly System.Collections.Generic.Dictionary<string, CharacterAnimationCatalog> editorDevelopmentCatalogs =
            new System.Collections.Generic.Dictionary<string, CharacterAnimationCatalog>(StringComparer.Ordinal);

        public static void RegisterEditorDevelopmentCatalog(MatchContentIdentity identity, CharacterAnimationCatalog catalog)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            catalog.hideFlags |= HideFlags.DontSave;
            string key = EditorCatalogKey(identity.PackageId, identity.SourceHash);
            if (editorDevelopmentCatalogs.TryGetValue(key, out var previous) && previous != null)
                UnityEngine.Object.DestroyImmediate(previous);
            editorDevelopmentCatalogs[key] = catalog;
        }

        public static void ClearEditorDevelopmentCatalogs()
        {
            foreach (var catalog in editorDevelopmentCatalogs.Values)
                if (catalog != null && (catalog.hideFlags & HideFlags.DontSave) != 0)
                    UnityEngine.Object.DestroyImmediate(catalog);
            editorDevelopmentCatalogs.Clear();
        }

        private static string EditorCatalogKey(string packageId, string sourceHash)
            => packageId + "\n" + sourceHash;
#endif

        public static bool TryResolve(
            MatchContentEntry entry,
            out CharacterAnimationCatalog animationCatalog,
            out GameObject rig,
            out string error)
        {
            return TryResolve(entry, out animationCatalog, out rig, out _, out error);
        }

        public static bool TryResolve(
            MatchContentEntry entry,
            out CharacterAnimationCatalog animationCatalog,
            out GameObject rig,
            out SlopArena.Client.Entities.WeaponAttachConfig weaponConfig,
            out string error)
        {
            animationCatalog = null;
            rig = null;
            weaponConfig = null;
            error = "";

            if (entry == null)
            {
                error = "Match content entry is required.";
                return false;
            }

            return TryResolve(entry.Identity, entry.CookedCharacterPackage, out animationCatalog, out rig, out weaponConfig, out error);
        }

        public static bool TryResolve(
            MatchContentIdentity identity,
            CookedCharacterPackage package,
            out CharacterAnimationCatalog animationCatalog,
            out GameObject rig,
            out string error)
        {
            return TryResolve(identity, package, out animationCatalog, out rig, out _, out error);
        }

        public static bool TryResolve(
            MatchContentIdentity identity,
            CookedCharacterPackage package,
            out CharacterAnimationCatalog animationCatalog,
            out GameObject rig,
            out SlopArena.Client.Entities.WeaponAttachConfig weaponConfig,
            out string error)
        {
            animationCatalog = null;
            rig = null;
            weaponConfig = null;
            error = "";

            if (identity == null)
            {
                error = "Match content identity is required.";
                return false;
            }

            if (package == null)
            {
                error = $"No cooked client package is attached to '{identity.PackageId}'.";
                return false;
            }

            if (!MatchContentCatalogBuilder.IsStablePackageId(identity.PackageId))
            {
                error = $"Invalid package ID '{identity.PackageId}'.";
                return false;
            }

            if (package.Metadata.PackageId != identity.PackageId)
            {
                error = $"Cooked package ID mismatch for '{identity.PackageId}'.";
                return false;
            }
            if (!IsSha256(identity.SourceHash))
            {
                error = $"Cooked package source hash is invalid for '{identity.PackageId}'.";
                return false;
            }
#if UNITY_EDITOR
            if (editorDevelopmentCatalogs.TryGetValue(EditorCatalogKey(identity.PackageId, identity.SourceHash), out var editorCatalog) &&
                editorCatalog != null)
            {
                if (editorCatalog.Rig == null)
                {
                    error = $"Editor animation catalog rig is missing for '{identity.PackageId}'.";
                    return false;
                }
                animationCatalog = editorCatalog;
                rig = editorCatalog.Rig;
                weaponConfig = editorCatalog.WeaponConfig;
                return true;
            }
#endif


            string resourcePath = $"Generated/CharacterPackages/{identity.PackageId}";
            CharacterAnimationCatalog[] candidates = Resources.LoadAll<CharacterAnimationCatalog>(resourcePath);
            CharacterAnimationCatalog match = null;
            foreach (CharacterAnimationCatalog candidate in candidates)
            {
                if (candidate == null || candidate.PackageId != identity.PackageId)
                    continue;
                if (match != null)
                {
                    error = $"Multiple generated animation catalogs found for '{identity.PackageId}'.";
                    return false;
                }
                match = candidate;
            }

            if (match == null)
            {
                error = $"Generated animation catalog is missing for '{identity.PackageId}'.";
                return false;
            }
            if (match.SourceHash != identity.SourceHash)
            {
                error = $"Generated animation catalog source hash mismatch for '{identity.PackageId}'.";
                return false;
            }
            if (match.Rig == null)
            {
                error = $"Generated animation catalog rig is missing for '{identity.PackageId}'.";
                return false;
            }

            animationCatalog = match;
            rig = match.Rig;
            weaponConfig = match.WeaponConfig;
            return true;
        }
        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            }
            return true;
        }
    }
}
