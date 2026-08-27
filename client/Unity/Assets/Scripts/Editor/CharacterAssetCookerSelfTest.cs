using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using System.IO;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Shared;

public static class CharacterAssetCookerSelfTest
{
    private static readonly string[] ExpectedIds =
    {
        "anim.idle", "anim.run", "anim.dash", "anim.jump", "anim.fall",
        "anim.hit-light", "anim.hit-medium", "anim.hit-hard", "anim.low-kick",
        "anim.double-punch", "anim.straight-punch", "anim.floating-kick", "anim.sweeping-kick",
        "anim.high-kick", "anim.double-kick", "anim.air-smash", "anim.ki-shot",
        "anim.rising-dragon", "anim.cyclone-kick", "anim.dragon-beam",
    };

    public static void RunFightGuySelfTest()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
        if (catalog == null) throw new InvalidOperationException("FightGuy catalog is missing.");
        CharacterAssetCookResult first = Cook(catalog);
        CharacterAssetCookResult second = Cook(catalog);
        CharacterPackageAssemblyResult firstPackage = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(first));
        CharacterPackageAssemblyResult secondPackage = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(second));
        AssertEqual(first.SourceHash, second.SourceHash, "unchanged source hash");
        AssertBytes(first.PoseBytes, second.PoseBytes, "unchanged pose bytes");
        AssertBytes(first.BindingBytes, second.BindingBytes, "unchanged binding bytes");
        AssertBytes(firstPackage.ManifestBytes, secondPackage.ManifestBytes, "unchanged manifest bytes");
        AssertBytes(firstPackage.RuntimeBytes, secondPackage.RuntimeBytes, "unchanged runtime bytes");
        AssertEqual(firstPackage.PackageHash, secondPackage.PackageHash, "unchanged package hash");
        AssertEqual(ExpectedIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(), first.Animations.Select(x => x.SemanticId).OrderBy(x => x, StringComparer.Ordinal).ToArray(), "semantic IDs");
        var baked = BakedAnimationData.LoadFromBin(first.PoseBytes);
        AssertEqual(ExpectedIds.Length, baked.Animations.Length, "pose animation count");
        foreach (string id in ExpectedIds)
            if (!first.Animations.Any(x => x.SemanticId == id)) throw new InvalidOperationException($"Missing generated binding: {id}");
        var committedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CharacterPackageAssembler.ManifestPath] = firstPackage.ManifestBytes,
            [CharacterPackageAssembler.RuntimePath] = firstPackage.RuntimeBytes,
            [CharacterPackageAssembler.PosePath] = firstPackage.PoseBytes,
            [CharacterPackageAssembler.BindingPath] = firstPackage.BindingBytes,
        };
        if (!CharacterPackageAssembler.Verify(committedFiles).IsValid)
            throw new InvalidOperationException("Assembled FightGuy package did not verify.");
        committedFiles[CharacterPackageAssembler.PosePath] = firstPackage.PoseBytes.Concat(new byte[] { 0 }).ToArray();
        if (CharacterPackageAssembler.Verify(committedFiles).IsValid)
            throw new InvalidOperationException("Cross-payload mismatch unexpectedly verified.");
        var generated = AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(
            "Assets/Resources/Generated/CharacterPackages/fightguy/FightGuy_AnimationCatalog.asset");
        if (generated == null || generated.Animations.Length != ExpectedIds.Length ||
            generated.Animations.Any(x => x == null || x.Clip == null))
            throw new InvalidOperationException("Generated catalog did not resolve every animation binding.");
        var invalidRig = new GameObject("FightGuyCookerInvalidRig");
        try
        {
            ExpectCode(catalog, c => c.Rig = invalidRig, "asset-catalog.rig.invalid");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(invalidRig);
        }
        var incompatibleRig = new GameObject("FightGuyCookerIncompatibleRig");
        incompatibleRig.AddComponent<Animator>();
        try
        {
            ExpectCode(catalog, c => c.Rig = incompatibleRig, "asset-catalog.rig.incompatible");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(incompatibleRig);
        }
        ExpectCode(catalog, c => c.SampleRate = 30, "asset-catalog.sample.invalid");
        ExpectCode(catalog, c => c.Bindings[0].Clip = null, "asset-catalog.clip.missing");
        ExpectCode(catalog, c => c.Bindings[0].Clip = new AnimationClip(), "asset-catalog.clip.unsupported");
        ExpectCode(catalog, c => c.Bindings[0].Clip = new AnimationClip(), "asset-catalog.clip.unresolved");
        ExpectCode(catalog, c => c.Bindings[1].SemanticId = c.Bindings[0].SemanticId, "asset-catalog.id.duplicate");
        ExpectCode(catalog, c => c.Bindings[0].PoseTrackId = c.Bindings[1].PoseTrackId, "asset-catalog.id.duplicate");
        ExpectCode(catalog, c => c.Bindings[0].PoseTrackId = "", "asset-catalog.id.invalid");
        ExpectCode(catalog, c => c.Bindings = c.Bindings.Where(x => x.SemanticId != "anim.cyclone-kick").ToArray(), "reference.animation.unresolved");

        var alternate = catalog.Bindings.FirstOrDefault(x => x.SemanticId != "anim.cyclone-kick" && x.Clip != null)?.Clip;
        if (alternate != null)
        {
            CharacterAssetCookResult changed = Cook(catalog, c => c.Bindings.First(x => x.SemanticId == "anim.cyclone-kick").Clip = alternate);
            if (changed.SourceHash == first.SourceHash) throw new InvalidOperationException("Changing the R clip did not change source hash.");
            if (changed.BindingBytes.SequenceEqual(first.BindingBytes)) throw new InvalidOperationException("Changing the R clip did not change bindings.");
            if (changed.PoseBytes.SequenceEqual(first.PoseBytes)) throw new InvalidOperationException("Changing the R clip did not change poses.");
            if (!changed.Animations.Any(x => x.SemanticId == "anim.idle")) throw new InvalidOperationException("Unrelated binding was lost.");
        }
        AssertDependencyIdentityInvalidatesHash(catalog, first);
        AssertFailedCookPreservesLastValidOutput(catalog);
        CharacterCookAssetPostprocessor.ResetQueueRequestCount();
        CharacterCookAssetPostprocessor.QueueRecook();
        CharacterCookAssetPostprocessor.QueueRecook();
        if (CharacterCookAssetPostprocessor.QueueRequestCount != 1)
            throw new InvalidOperationException("Repeated postprocessor notifications were not coalesced.");
        var finalCook = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook("fightguy");
        if (!finalCook.Success) throw new InvalidOperationException("Final FightGuy cook failed.");
        Debug.Log("[SlopArena] FightGuy cooker self-test passed.");
    }
    private static void AssertDependencyIdentityInvalidatesHash(CharacterAssetCatalog catalog, CharacterAssetCookResult result)
    {
        var altered = result.Dependencies.Select(x => new CharacterCookDependencyRecord
        {
            Kind = x.Kind,
            Identity = x.Identity,
            Guid = x.Guid,
            DependencyHash = x.DependencyHash + "-changed",
            MetaHash = x.MetaHash,
            ImporterSettings = x.ImporterSettings,
        }).ToList();
        string packageJson = System.IO.File.ReadAllText(UnityCharacterAssetCooker.ResolveFile("Assets/CharacterPackages/fightguy", "package.json"));
        string characterJson = System.IO.File.ReadAllText(UnityCharacterAssetCooker.ResolveFile("Assets/CharacterPackages/fightguy", "character.json"));
        string alteredHash = CharacterCookDependencyTracker.ComputeSourceHash(
            packageJson, characterJson, catalog, altered, result.Animations);
        if (alteredHash == result.SourceHash)
            throw new InvalidOperationException("Changing a dependency identity did not invalidate source hash.");
    }

    private static void AssertFailedCookPreservesLastValidOutput(CharacterAssetCatalog catalog)
    {
        string root = UnityCharacterAssetCooker.ProjectRoot();
        string posePath = System.IO.Path.Combine(root, CharacterCookOutput.FightGuy.IntermediateDirectory, "poses.bin");
        string bindingPath = System.IO.Path.Combine(root, CharacterCookOutput.FightGuy.IntermediateDirectory, "client.bindings");
        string generatedPath = System.IO.Path.Combine(root, "Assets/Resources/Generated/CharacterPackages/fightguy/FightGuy_AnimationCatalog.asset");
        string statusPath = System.IO.Path.Combine(root, CharacterCookOutput.FightGuy.IntermediateDirectory, "cook-status.json");
        byte[] status = System.IO.File.ReadAllBytes(statusPath);
        string canonicalPath = System.IO.Path.Combine(root, "content-cooked/fightguy");
        byte[] pose = System.IO.File.ReadAllBytes(posePath);
        byte[] binding = System.IO.File.ReadAllBytes(bindingPath);
        byte[] generated = System.IO.File.ReadAllBytes(generatedPath);
        var canonical = Directory.Exists(canonicalPath)
            ? Directory.GetFiles(canonicalPath).ToDictionary(x => System.IO.Path.GetFileName(x), x => System.IO.File.ReadAllBytes(x), StringComparer.Ordinal)
            : null;
        int sampleRate = catalog.SampleRate;
        try
        {
            catalog.SampleRate = 30;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            byte[] statusBeforeFailedCook = System.IO.File.ReadAllBytes(statusPath);
            var failedCook = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook("fightguy");
            if (failedCook.Success)
                throw new InvalidOperationException("Invalid sample rate unexpectedly cooked.");
            if (!System.IO.File.ReadAllBytes(posePath).SequenceEqual(pose) ||
                !System.IO.File.ReadAllBytes(bindingPath).SequenceEqual(binding) ||
                !System.IO.File.ReadAllBytes(generatedPath).SequenceEqual(generated))
                throw new InvalidOperationException("Failed recook replaced last-valid output.");
            if (canonical != null)
            {
                var after = Directory.GetFiles(canonicalPath).ToDictionary(x => System.IO.Path.GetFileName(x), x => System.IO.File.ReadAllBytes(x), StringComparer.Ordinal);
                if (after.Count != canonical.Count || canonical.Any(x => !after.TryGetValue(x.Key, out var bytes) || !bytes.SequenceEqual(x.Value)))
                    throw new InvalidOperationException("Failed recook replaced last-valid canonical package.");
            }
            if (!System.IO.File.ReadAllBytes(statusPath).SequenceEqual(statusBeforeFailedCook))
                throw new InvalidOperationException("Failed recook changed the last-valid status.");
        }
        finally
        {
            catalog.SampleRate = sampleRate;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            var restoredCook = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook("fightguy");
            if (!restoredCook.Success) throw new InvalidOperationException("Failed to restore FightGuy after invalid cook test.");
        }
    }


    private static CharacterAssetCookResult Cook(CharacterAssetCatalog catalog, Action<CharacterAssetCatalog> mutate = null)
    {
        var copy = Clone(catalog);
        try
        {
            mutate?.Invoke(copy);
            return UnityCharacterAssetCooker.Cook("Assets/CharacterPackages/fightguy", copy, CharacterCookOutput.FightGuy, CharacterCookProfile.TrustedBuiltIn);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    private static void ExpectCode(CharacterAssetCatalog source, Action<CharacterAssetCatalog> mutate, string code)
    {
        CharacterAssetCookResult result = Cook(source, mutate);
        if (!result.Diagnostics.Any(x => x.Code == code && x.Severity == CharacterDiagnosticSeverity.Error))
            throw new InvalidOperationException($"Expected diagnostic {code}.");
    }

    private static CharacterAssetCatalog Clone(CharacterAssetCatalog source)
    {
        var copy = ScriptableObject.CreateInstance<CharacterAssetCatalog>();
        copy.PackageId = source.PackageId;
        copy.CatalogSchemaVersion = source.CatalogSchemaVersion;
        copy.Rig = source.Rig;
        copy.SampleRate = source.SampleRate;
        copy.Bindings = source.Bindings.Select(x => new CharacterAssetCatalog.AnimationBinding
        {
            SemanticId = x.SemanticId,
            Clip = x.Clip,
            Extrapolation = x.Extrapolation,
            PoseTrackId = x.PoseTrackId,
        }).ToArray();
        return copy;
    }

    private static void AssertBytes(byte[] left, byte[] right, string label)
    {
        if (!left.SequenceEqual(right)) throw new InvalidOperationException($"{label} differ.");
    }

    private static void AssertEqual(string left, string right, string label)
    {
        if (!StringComparer.Ordinal.Equals(left, right)) throw new InvalidOperationException($"{label} differ.");
    }

    private static void AssertEqual(int left, int right, string label)
    {
        if (left != right) throw new InvalidOperationException($"{label} differ: {left} vs {right}.");
    }

    private static void AssertEqual(string[] left, string[] right, string label)
    {
        if (!left.SequenceEqual(right, StringComparer.Ordinal)) throw new InvalidOperationException($"{label} differ.");
    }
}
