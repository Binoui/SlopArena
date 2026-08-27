using System.IO;
using UnityEngine;

namespace SlopArena.Client
{
    /// <summary>
    /// Resolves baked content (arena .arena files, skeleton .bin files) to an
    /// absolute path that exists. Order: the player build's StreamingAssets
    /// (staged by scripts/build-release.sh), then the repo-root data/ directory
    /// (Editor/dev checkouts, where StreamingAssets is empty and gitignored).
    /// Issue #77: the old Application.dataPath/../../.. resolution only worked
    /// when the exe sat inside a repo-shaped tree — distributed builds silently
    /// fell back to hardcoded, collision-less arenas and players dropped through
    /// the floor (Simulation grounded at KillHeight + 1).
    /// </summary>
    public static class BakedContentPaths
    {
        /// <summary>Absolute path to a baked arena file, or null if not found anywhere.</summary>
        public static string? ResolveArena(string arenaName)
        {
            string relative = Path.Combine("arenas", arenaName + ".arena");
            return FirstExisting(relative);
        }

        /// <summary>Absolute path for a res:// path (e.g. "res://data/manki_skeleton.bin"),
        /// or null if not found anywhere.
        /// </summary>
        public static string? ResolveBaked(string resPath)
        {
            string relative = resPath.StartsWith("res://")
                ? resPath.Substring("res://".Length)
                : resPath;
            return FirstExisting(relative);
        }


        /// <summary>
        /// First arena directory that actually contains baked .arena files:
        /// the player build's StreamingAssets/arenas, then the repo-root
        /// data/arenas (Editor/dev). An existing-but-empty directory is skipped
        /// (issue #77 / build-release.sh leaves an empty StreamingAssets/arenas
        /// behind when the Editor is open), otherwise StageSelect would offer
        /// zero stages. Null if neither directory has any .arena files.
        /// </summary>
        public static string? ArenaDirectory()
        {
            string inBuild = Path.Combine(Application.streamingAssetsPath, "arenas");
            if (HasArenas(inBuild)) return inBuild;
            string? repoRoot = RepoRoot();
            if (repoRoot != null)
            {
                string inRepo = Path.GetFullPath(Path.Combine(repoRoot, "data", "arenas"));
                if (HasArenas(inRepo)) return inRepo;
            }
            return null;
        }

        /// <summary>True if <paramref name="dir"/> exists and contains at least one *.arena file.</summary>
        private static bool HasArenas(string dir)
        {
            return Directory.Exists(dir) &&
                   Directory.GetFiles(dir, "*.arena").Length > 0;
        }

        private static string? FirstExisting(string relative)
        {
            string inBuild = Path.Combine(Application.streamingAssetsPath, relative);
            if (File.Exists(inBuild)) return inBuild;

            string? repoRoot = RepoRoot();
            if (repoRoot != null)
            {
                // Repo nests all baked content under <repo>/data/. StreamingAssets
                // flattens differently (arenas at StreamingAssets/arenas, bins at
                // StreamingAssets/data), so strip a leading "data/" segment before
                // joining onto the repo's data root.
                string repoRelative = relative.StartsWith("data/")
                    ? relative.Substring("data/".Length)
                    : relative;
                string inRepo = Path.GetFullPath(Path.Combine(repoRoot, "data", repoRelative));
                if (File.Exists(inRepo)) return inRepo;
            }
            return null;
        }

        /// <summary>
        /// Repo root in Editor/dev checkouts: Application.dataPath is
        /// &lt;repo&gt;/client/Unity/Assets, so three parents up. Null in built
        /// players (no repo tree), where only the StreamingAssets lookup applies.
        /// </summary>
        private static string? RepoRoot()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            return Directory.Exists(Path.Combine(root, "data")) ? root : null;
        }
    }
}
