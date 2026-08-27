using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
public sealed class CharacterCookAssetPostprocessor : AssetPostprocessor
{
    private static bool _pending;
    private static bool _processing;
    private static double _dueTime;
    private static int _queueRequestCount;

    internal static bool Pending => _pending;
    internal static int QueueRequestCount => _queueRequestCount;
    internal static void ResetQueueRequestCount()
    {
        _queueRequestCount = 0;
        _pending = false;
        EditorApplication.update -= ProcessQueue;
    }

    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        var changed = new List<string>();
        changed.AddRange(importedAssets ?? Array.Empty<string>());
        changed.AddRange(deletedAssets ?? Array.Empty<string>());
        changed.AddRange(movedAssets ?? Array.Empty<string>());
        changed.AddRange(movedFromAssetPaths ?? Array.Empty<string>());
        if (changed.Any(IsCookDependency)) QueueRecook();
    }

    public static void QueueRecook()
    {
        if (_processing) return;
        if (!_pending) _queueRequestCount++;
        _pending = true;
        _dueTime = UnityEditor.EditorApplication.timeSinceStartup + 0.25;
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        CharacterCookStatus status = service.ReadStatus("fightguy");
        if (status.State == "Valid") service.MarkStale("fightguy");
        EditorApplication.update -= ProcessQueue;
        EditorApplication.update += ProcessQueue;
    }

    private static bool IsCookDependency(string path)
    {
        string normalized = (path ?? "").Replace('\\', '/');
        const string root = "Assets/CharacterPackages/";
        if (normalized.StartsWith(root, StringComparison.Ordinal)
            && (normalized.EndsWith("/package.json", StringComparison.Ordinal)
                || normalized.EndsWith("/character.json", StringComparison.Ordinal)
                || normalized.EndsWith("/CharacterAssetCatalog.asset", StringComparison.Ordinal)
                || normalized.EndsWith("/CharacterAssetCatalog.asset.meta", StringComparison.Ordinal)))
            return true;
        CharacterCookStatus status = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).ReadStatus("fightguy");
        return status.Dependencies != null && status.Dependencies.Any(x => x.Identity == normalized);
    }

    private static void ProcessQueue()
    {
        if (!_pending || _processing || EditorApplication.timeSinceStartup < _dueTime) return;
        _processing = true;
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" }))
            {
                string catalogPath = AssetDatabase.GUIDToAssetPath(guid);
                var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(catalogPath);
                if (catalog == null || string.IsNullOrEmpty(catalog.PackageId)) continue;
                string packageRoot = catalogPath.Substring(0, catalogPath.LastIndexOf('/'));
                new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook(packageRoot);
            }
        }
        finally
        {
            _pending = false;
            _processing = false;
            EditorApplication.update -= ProcessQueue;
        }
    }
}
