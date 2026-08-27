using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using SlopArena.Client.Animation;
using SlopArena.Shared;

public static class CharacterPackageAuthoringSelfTest
{
    public static void RunFightGuyAuthoringSelfTest()
    {
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        CharacterPackageInspectionResult initial = service.Inspect("fightguy");
        Require(initial.Success, "FightGuy inspection failed.");
        Require(initial.PackageId == "fightguy", "Inspection returned the wrong package ID.");
        Require(initial.Status == "valid", "FightGuy inspection did not report valid content.");
        Require(!initial.DirtyOrStale, "Fresh FightGuy inspection reported stale content.");
        Require(initial.Slots.Count == CharacterPackageCompiler.CanonicalSlotIds.Count, "Inspection did not return all canonical slots.");
        Require(initial.Slots[0].Id == "ground.1" && initial.Slots[0].Name == "Low Kick" && initial.Slots[0].StageCount == 1,
            "Inspection returned incorrect ground.1 metadata.");

        CharacterPackageCookResult directCook = service.Cook("fightguy");
        Require(directCook.Success, "Direct FightGuy cook failed.");
        Require(!string.IsNullOrEmpty(directCook.SourceHash) && !string.IsNullOrEmpty(directCook.CookedContentHash) && !string.IsNullOrEmpty(directCook.PackageHash),
            "Successful cook did not return hashes.");

        CharacterPackageInspectionResult commandInspect = SlopArenaCharacterCommands.Inspect("fightguy");
        Require(commandInspect.Success && commandInspect.PackageId == directCook.PackageId && commandInspect.SourceHash == directCook.SourceHash,
            "Inspect command did not delegate to the service result.");
        CharacterPackageCookResult commandCook = SlopArenaCharacterCommands.Cook("fightguy");
        Require(commandCook.Success && commandCook.SourceHash == directCook.SourceHash && commandCook.PackageHash == directCook.PackageHash,
            "Cook command did not delegate to the service result.");

        string projectRoot = UnityCharacterAssetCooker.ProjectRoot();
        string packageRoot = Path.Combine(projectRoot, "Assets/CharacterPackages/fightguy");
        string characterPath = Path.Combine(packageRoot, "character.json");
        string manifestPath = Path.Combine(packageRoot, "package.json");
        byte[] originalCharacter = File.ReadAllBytes(characterPath);
        string canonicalPath = Path.Combine(projectRoot, "..", "..", "content-cooked/fightguy");
        Dictionary<string, byte[]> canonical = SnapshotDirectory(canonicalPath);
        byte[] pose = File.ReadAllBytes(Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "poses.bin"));
        byte[] binding = File.ReadAllBytes(Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "client.bindings"));
        string statusPath = Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "cook-status.json");
        byte[] status = File.ReadAllBytes(statusPath);
        string generatedPath = Path.Combine(projectRoot, "Assets/Resources/Generated/CharacterPackages/fightguy/FightGuy_AnimationCatalog.asset");
        byte[] generated = File.ReadAllBytes(generatedPath);

        try
        {
            string invalidCharacter = MakeInvalidCharacter(
                File.ReadAllText(manifestPath),
                Encoding.UTF8.GetString(originalCharacter));
            File.WriteAllText(characterPath, invalidCharacter);

            CharacterPackageInspectionResult invalidInspect = service.Inspect("fightguy");
            Require(invalidInspect.Success && invalidInspect.Status == "invalid" && invalidInspect.DirtyOrStale,
                "Invalid source inspection did not report invalid content.");
            Require(invalidInspect.Diagnostics.Any(x => x.Code == "value.out-of-range" && x.Path == "character.operation.tick"),
                "Invalid source diagnostic was not propagated with its compiler path.");

            CharacterPackageCookResult invalidCook = SlopArenaCharacterCommands.Cook("fightguy");
            Require(!invalidCook.Success, "Invalid source unexpectedly cooked.");
            Require(invalidCook.Diagnostics.Any(x => x.Code == "value.out-of-range" && x.Path == "character.operation.tick"),
                "Cook command did not return the compiler diagnostic.");
            Require(File.ReadAllBytes(Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "poses.bin")).SequenceEqual(pose),
                "Invalid cook changed pose output.");
            Require(File.ReadAllBytes(Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "client.bindings")).SequenceEqual(binding),
                "Invalid cook changed binding output.");
            Require(File.ReadAllBytes(statusPath).SequenceEqual(status), "Invalid cook changed persisted status.");
            Require(File.ReadAllBytes(generatedPath).SequenceEqual(generated), "Invalid cook changed generated catalog.");
            AssertDirectorySnapshot(canonicalPath, canonical);
        }
        finally
        {
            File.WriteAllBytes(characterPath, originalCharacter);
            AssetDatabase.Refresh();
            CharacterPackageCookResult restored = service.Cook("fightguy");
            Require(restored.Success, "Could not restore FightGuy after authoring self-test.");
        }

        UnityEngine.Debug.Log("[SlopArena] Character package authoring self-test passed.");
    }

    private static string MakeInvalidCharacter(string manifestJson, string characterJson)
    {
        CharacterPackageSource source = CharacterPackageSourceCodec.Load(manifestJson, characterJson).Source
            ?? throw new InvalidDataException("FightGuy source could not be loaded for the authoring self-test.");
        int slotIndex = source.Character.Slots.ToList().FindIndex(x => x.Id == "ground.1");
        if (slotIndex < 0) throw new InvalidDataException("FightGuy ground.1 source slot is missing.");
        CharacterSlotSource slot = source.Character.Slots[slotIndex];
        CharacterStageSource stage = slot.Timeline.Stages[0];
        var invalidOperation = stage.Operations[0] switch
        {
            SpawnHitboxOperationSource hitbox => hitbox with { Tick = stage.DurationTicks },
            _ => throw new InvalidDataException("FightGuy ground.1 first operation is not a hitbox.")
        };
        var operations = stage.Operations.ToList();
        operations[0] = invalidOperation;
        var stages = slot.Timeline.Stages.ToList();
        stages[0] = stage with { Operations = operations };
        var slots = source.Character.Slots.ToList();
        slots[slotIndex] = slot with { Timeline = new CharacterTimelineSource(stages) };
        return CharacterPackageSourceCodec.SerializeCharacter(source.Character with { Slots = slots });
    }

    private static Dictionary<string, byte[]> SnapshotDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                x => x.Substring(directory.Length + 1).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);
    }

    private static void AssertDirectorySnapshot(string directory, Dictionary<string, byte[]> expected)
    {
        Dictionary<string, byte[]> actual = SnapshotDirectory(directory);
        Require(expected != null && actual != null && expected.Count == actual.Count, "Cooked package file set changed.");
        foreach (var entry in expected)
            Require(actual.TryGetValue(entry.Key, out var bytes) && bytes.SequenceEqual(entry.Value),
                "Cooked package changed after invalid cook: " + entry.Key);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
