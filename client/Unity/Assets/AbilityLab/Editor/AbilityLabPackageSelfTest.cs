using System;
using System.IO;
using System.Linq;
using UnityEditor;
using SlopArena.Shared;

namespace SlopArena.EditorTools;

public static class AbilityLabPackageSelfTest
{
    public static void RunPackageEditorSelfTest()
    {
        string packageId = "selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string root = "Assets/CharacterPackages/" + packageId;
        string full = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), root);
        var workspace = new AbilityLabPackageWorkspace();
        try
        {
            if (!workspace.NewPackage(packageId, "Self Test")) throw new InvalidOperationException(string.Join("\n", workspace.Diagnostics));
            if (workspace.Draft.Slots.Count != 16 || workspace.Draft.CapabilityRequirements.Count != 0) throw new InvalidOperationException("Minimal package contract failed.");
            string characterPath = Path.Combine(full, "character.json");
            string before = File.ReadAllText(characterPath);
            File.AppendAllText(characterPath, "\n");
            if (workspace.SavePackage() || !workspace.Diagnostics.Any(x => x.Code == "workspace.conflict")) throw new InvalidOperationException("External source conflict was not blocked.");
            File.WriteAllText(characterPath, before);
            if (!workspace.ReloadPackage()) throw new InvalidOperationException("Package reload failed.");
            UnityEngine.Debug.Log("AbilityLabPackageSelfTest passed: New/Open, 16 slots, and conflict blocking.");
        }
        finally
        {
            Selection.activeObject = null;
            AssetDatabase.DeleteAsset(root);
            if (Directory.Exists(full)) Directory.Delete(full, true);
            AssetDatabase.Refresh();
        }
    }
}
