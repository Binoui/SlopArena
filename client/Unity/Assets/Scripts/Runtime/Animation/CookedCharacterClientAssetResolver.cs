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

            if (entry.CookedCharacterPackage == null)
            {
                error = $"No cooked client package is attached to '{entry.Identity.PackageId}'.";
                return false;
            }

            string packageId = entry.Identity.PackageId;
            if (entry.CookedCharacterPackage.Metadata.PackageId != packageId)
            {
                error = $"Cooked package ID mismatch for '{packageId}'.";
                return false;
            }

            string resourcePath = $"Generated/CharacterPackages/{packageId}";
            CharacterAnimationCatalog[] candidates = Resources.LoadAll<CharacterAnimationCatalog>(resourcePath);
            CharacterAnimationCatalog match = null;
            foreach (CharacterAnimationCatalog candidate in candidates)
            {
                if (candidate == null || candidate.PackageId != packageId)
                    continue;
                if (match != null)
                {
                    error = $"Multiple generated animation catalogs found for '{packageId}'.";
                    return false;
                }
                match = candidate;
            }

            if (match == null)
            {
                error = $"Generated animation catalog is missing for '{packageId}'.";
                return false;
            }
            if (match.SourceHash != entry.Identity.SourceHash)
            {
                error = $"Generated animation catalog source hash mismatch for '{packageId}'.";
                return false;
            }
            if (match.Rig == null)
            {
                error = $"Generated animation catalog rig is missing for '{packageId}'.";
                return false;
            }

            animationCatalog = match;
            rig = match.Rig;
            return true;
        }
    }
}
