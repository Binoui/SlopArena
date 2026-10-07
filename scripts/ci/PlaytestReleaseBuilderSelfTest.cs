using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

/// <summary>Run with the producer injected into an Editor assembly; never builds a player.</summary>
public static class PlaytestReleaseBuilderSelfTest
{
    public static string[] Run(string bindingPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Catalog regression requires Edit Mode.");
        string id = "playtest-catalog-test-" + Guid.NewGuid().ToString("N");
        string contentRoot = Path.Combine(Path.GetTempPath(), id);
        string assetRoot = "Assets/Resources/Generated/CharacterPackages/" + id;
        string assetPath = CharacterCookOutput.For(id).GeneratedAssetPath;
        var bindings = JObject.Parse(File.ReadAllText(bindingPath));
        bindings["packageId"] = id;
        var identity = new PlaytestReleaseBuilder.PackageIdentityRow("", id, "", (string)bindings["sourceHash"], "", "");
        var verified = new PlaytestReleaseBuilder.PackageVerification(new[] { identity }, "");
        string fixture = Path.Combine(contentRoot, id, CharacterPackageAssembler.BindingPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture));
        try
        {
            File.WriteAllText(fixture, bindings.ToString());
            Refuses(() => PlaytestReleaseBuilder.VerifyClientCatalogs(verified));
            PlaytestReleaseBuilder.GenerateClientCatalogs(contentRoot, verified);
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(assetPath);
            GlobalObjectId.TryParse((string)bindings["rigGlobalObjectId"], out var rigId);
            if (catalog.Rig != GlobalObjectId.GlobalObjectIdentifierToObjectSlow(rigId) || catalog.SourceHash != identity.sourceHash)
                throw new InvalidOperationException("Generated catalog does not resolve the bound rig/source identity.");
            catalog.SourceHash = new string('0', 64);
            Refuses(() => PlaytestReleaseBuilder.VerifyClientCatalogs(verified));
            catalog.SourceHash = identity.sourceHash;
            var rig = catalog.Rig;
            catalog.Rig = null;
            Refuses(() => PlaytestReleaseBuilder.VerifyClientCatalogs(verified));
            catalog.Rig = rig;
            string duplicate = assetRoot + "/duplicate.asset";
            AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(catalog), duplicate);
            Refuses(() => PlaytestReleaseBuilder.VerifyClientCatalogs(verified));
            AssetDatabase.DeleteAsset(duplicate);
            bindings["rigGlobalObjectId"] = "GlobalObjectId_V1-1-00000000000000000000000000000000-1-0";
            File.WriteAllText(fixture, bindings.ToString());
            Refuses(() => PlaytestReleaseBuilder.GenerateClientCatalogs(contentRoot, verified));
            PlaytestReleaseBuilder.VerifyClientCatalogs(verified);
            return new[] { "missing refused", "bound rig/source resolved", "stale refused", "rigless refused", "duplicate refused", "unresolved binding refused; valid catalog preserved" };
        }
        finally
        {
            AssetDatabase.DeleteAsset(assetRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.Delete(contentRoot, true);
        }
    }

    private static void Refuses(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception("Invalid client catalog was accepted.");
    }
}
