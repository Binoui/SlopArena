using System;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Animation
{
    public static class CookedCharacterClientAssetResolver
    {
        public static bool TryResolve(
            MatchContentEntry entry,
            out CharacterAnimationCatalog animationCatalog,
            out GameObject rig,
            out string error)
        {
            animationCatalog = null;
            rig = null;
            error = "";

            if (entry == null)
            {
                error = "Match content entry is required.";
                return false;
            }

            return TryResolve(entry.Identity, entry.CookedCharacterPackage, out animationCatalog, out rig, out error);
        }

        public static bool TryResolve(
            MatchContentIdentity identity,
            CookedCharacterPackage package,
            out CharacterAnimationCatalog animationCatalog,
            out GameObject rig,
            out string error)
        {
            animationCatalog = null;
            rig = null;
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
