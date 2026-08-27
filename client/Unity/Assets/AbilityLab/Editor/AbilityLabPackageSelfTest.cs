using System;
using System.IO;
using System.Linq;
using UnityEditor;
using SlopArena.Client.Tools;
using SlopArena.Client;
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

            var resolver = LocalContentResolver.CreateDefault();
            string repositoryCookedRoot = resolver.ContentRoots.Last();
            string cookedRoot = Path.Combine(repositoryCookedRoot, "fightguy");
            string[] cookedFiles =
            {
                CharacterPackageAssembler.ManifestPath,
                CharacterPackageAssembler.RuntimePath,
                CharacterPackageAssembler.PosePath,
                CharacterPackageAssembler.BindingPath,
            };
            var cookedBefore = cookedFiles.ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(cookedRoot, path)));
            string statusPath = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), CharacterCookOutput.FightGuy.IntermediateDirectory, CharacterCookOutput.FightGuy.StatusFileName);
            byte[] statusBefore = File.ReadAllBytes(statusPath);
            string catalogPath = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
            byte[] catalogBefore = File.ReadAllBytes(catalogPath);

            if (!workspace.OpenPackage("Assets/CharacterPackages/fightguy") || workspace.Preview == null || !workspace.Preview.IsAvailable)
                throw new InvalidOperationException("FightGuy package preview did not load: " + string.Join("\n", workspace.Diagnostics));
            int catalogSampleRate = workspace.Catalog.SampleRate;
            workspace.Catalog.SampleRate = 30;
            EditorUtility.SetDirty(workspace.Catalog);
            AssetDatabase.SaveAssets();
            if (workspace.SavePackage() ||
                !workspace.Diagnostics.Any(x => x.Code == "workspace.conflict" && x.Path.EndsWith("CharacterAssetCatalog.asset", StringComparison.Ordinal)))
                throw new InvalidOperationException("External catalog conflict was not blocked before source writes.");
            workspace.Catalog.SampleRate = catalogSampleRate;
            EditorUtility.SetDirty(workspace.Catalog);
            AssetDatabase.SaveAssets();
            if (!workspace.ReloadPackage()) throw new InvalidOperationException("Catalog conflict cleanup could not reload FightGuy.");
            statusBefore = File.ReadAllBytes(statusPath);
            var catalogRig = workspace.Catalog.Rig;
            if (!workspace.ReplaceCatalogRig(null) || workspace.SavePackage() ||
                workspace.Diagnostics.Any(x => x.Code == "workspace.conflict"))
                throw new InvalidOperationException("Owned catalog edit was incorrectly blocked or unexpectedly cooked.");
            if (!File.ReadAllBytes(Path.Combine(cookedRoot, CharacterPackageAssembler.ManifestPath)).SequenceEqual(cookedBefore[CharacterPackageAssembler.ManifestPath]) ||
                !File.ReadAllBytes(statusPath).SequenceEqual(statusBefore))
                throw new InvalidOperationException("Catalog edit with a failed cook changed cooked artifacts or status.");
            workspace.Undo();
            if (workspace.Catalog.Rig != catalogRig || !File.ReadAllBytes(catalogPath).SequenceEqual(catalogBefore))
                throw new InvalidOperationException("Catalog undo did not restore the saved fixture.");

            if (!workspace.OpenPackage("Assets/CharacterPackages/fightguy") || workspace.Preview == null || !workspace.Preview.IsAvailable)
                throw new InvalidOperationException("FightGuy package preview did not reload.");
            var preview = workspace.Preview;
            if (preview.Package == null || preview.BakedPoses == null || preview.AnimationCatalog == null || preview.Rig == null ||
                preview.Identity == null || preview.Slots.Count != 16 ||
                new[] { preview.Identity.PackageId, preview.Identity.Version, preview.Identity.SourceHash, preview.Identity.CookedContentHash, preview.Identity.PackageHash }.Any(string.IsNullOrEmpty))
                throw new InvalidOperationException("FightGuy preview is missing a verified field.");
            if (!preview.Slots.SequenceEqual(CanonicalSlotProjection.All))
                throw new InvalidOperationException("FightGuy preview slot projection is not canonical.");

            var unavailable = AbilityLabPackagePreviewLoader.Load("unavailable-package");
            if (unavailable.IsAvailable || unavailable.Identity != null || !unavailable.Diagnostics.Any(x => x.Code == "content.package.missing"))
                throw new InvalidOperationException("Unavailable package preview did not fail closed.");

            foreach (var file in cookedFiles)
                if (!File.ReadAllBytes(Path.Combine(cookedRoot, file)).SequenceEqual(cookedBefore[file]))
                    throw new InvalidOperationException("Opening a package changed cooked artifact bytes: " + file);
            if (!File.ReadAllBytes(statusPath).SequenceEqual(statusBefore))
                throw new InvalidOperationException("Opening a package changed cook status.");
            var fightGuyCharacterPath = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "Assets/CharacterPackages/fightguy/character.json");
            string fightGuyCharacterBefore = File.ReadAllText(fightGuyCharacterPath);
            var originalDraft = workspace.Draft;
            if (!workspace.ReplaceGeneral(
                    originalDraft.DisplayName, originalDraft.Weight + 1f, originalDraft.CapsuleRadius,
                    originalDraft.CapsuleHeight, originalDraft.HipHeight, originalDraft.HurtboxRadius) ||
                workspace.Draft.Weight != originalDraft.Weight + 1f ||
                workspace.Status != "Stale" || !workspace.IsDirty || workspace.CanRedo)
                throw new InvalidOperationException("General source edit did not update the immutable draft state.");
            workspace.Undo();
            if (workspace.Draft.Weight != originalDraft.Weight || !workspace.CanRedo)
                throw new InvalidOperationException("General source undo did not restore the prior value.");
            workspace.Redo();
            if (workspace.Draft.Weight != originalDraft.Weight + 1f)
                throw new InvalidOperationException("General source redo did not restore the edited value.");
            workspace.Undo();

            var movement = workspace.Draft.Movement;
            if (!workspace.ReplaceMovement(movement with { RunSpeed = movement.RunSpeed + 1f }) ||
                workspace.Draft.Movement.RunSpeed != movement.RunSpeed + 1f)
                throw new InvalidOperationException("Movement source edit did not update the immutable draft.");
            workspace.Undo();
            if (workspace.Draft.Movement.RunSpeed != movement.RunSpeed)
                throw new InvalidOperationException("Movement source undo did not restore the prior value.");

            var presentation = workspace.Draft.Presentation;
            bool presentationEdited = workspace.ReplacePresentation(presentation with { VisualScale = presentation.VisualScale + 0.1f });
            float expectedVisualScale = presentation.VisualScale + 0.1f;
            if (!presentationEdited || Math.Abs(workspace.Draft.Presentation.VisualScale - expectedVisualScale) > 0.0001f)
                throw new InvalidOperationException(
                    $"Presentation source edit did not update the immutable draft. accepted={presentationEdited} value={workspace.Draft.Presentation.VisualScale:R} expected={expectedVisualScale:R} status={workspace.Status} diagnostics={string.Join(",", workspace.Diagnostics.Select(x => x.Code))}");
            workspace.Undo();
            if (workspace.Draft.Presentation.VisualScale != presentation.VisualScale)
                throw new InvalidOperationException("Presentation source undo did not restore the prior value.");

            File.AppendAllText(fightGuyCharacterPath, "\n");
            if (workspace.SavePackage() || !workspace.Diagnostics.Any(x => x.Code == "workspace.conflict"))
                throw new InvalidOperationException("Dirty typed source edit did not block an external source conflict.");
            File.WriteAllText(fightGuyCharacterPath, fightGuyCharacterBefore);
            if (!workspace.ReloadPackage())
                throw new InvalidOperationException("Typed source conflict cleanup could not reload FightGuy.");

            var priorPreview = workspace.Preview;
            try
            {
                var sourceSlot = workspace.Draft.Slots.First(slot => slot.Id == "ground.1");
                int operationIndex = sourceSlot.Timeline.Stages
                    .SelectMany(stage => stage.Operations)
                    .Select((operation, index) => (operation, index))
                    .First(item => item.operation is SpawnHitboxOperationSource).index;
                var invalid = (SpawnHitboxOperationSource)sourceSlot.Timeline.Stages[0].Operations[operationIndex];
                if (!workspace.ReplaceHitbox("ground.1", 0, operationIndex, invalid.Hitbox with { DurationTicks = 0 }))
                    throw new InvalidOperationException("Invalid typed hitbox edit was rejected before cook.");
                if (workspace.SavePackage() || workspace.Status != "Failed")
                    throw new InvalidOperationException("Invalid typed hitbox cook did not fail.");
                if (!ReferenceEquals(priorPreview, workspace.Preview))
                    throw new InvalidOperationException("Failed cook replaced the last authoritative preview.");
                foreach (var file in cookedFiles)
                    if (!File.ReadAllBytes(Path.Combine(cookedRoot, file)).SequenceEqual(cookedBefore[file]))
                        throw new InvalidOperationException("Failed cook changed cooked artifact bytes: " + file);
            }
            finally
            {
                File.WriteAllText(fightGuyCharacterPath, fightGuyCharacterBefore);
                if (!workspace.ReloadPackage())
                    throw new InvalidOperationException("Failed-cook cleanup could not restore FightGuy source.");
            }

            if (!workspace.OpenPackage("Assets/CharacterPackages/fightguy") || workspace.Preview == null || !workspace.Preview.IsAvailable)
                throw new InvalidOperationException("FightGuy package was not valid after failed-cook cleanup.");
            var retimeSlot = workspace.Draft.Slots.First(slot => slot.Id == "ground.1");
            var retimeStage = retimeSlot.Timeline.Stages[0];
            int retimeOperationIndex = retimeStage.Operations
                .Select((operation, index) => (operation, index))
                .First(item => item.operation is SpawnHitboxOperationSource).index;
            var retimeHitbox = (SpawnHitboxOperationSource)retimeStage.Operations[retimeOperationIndex];
            int retimeTick = retimeHitbox.Tick > 0
                ? retimeHitbox.Tick - 1
                : Math.Min(retimeStage.DurationTicks - retimeHitbox.Hitbox.DurationTicks, retimeHitbox.Tick + 1);
            if (retimeTick == retimeHitbox.Tick ||
                !workspace.ReplaceOperationTick("ground.1", 0, retimeOperationIndex, retimeTick) ||
                ((SpawnHitboxOperationSource)workspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[0].Operations[retimeOperationIndex]).Tick != retimeTick)
                throw new InvalidOperationException("Safe timeline retime did not update the source tick.");
            workspace.Undo();
            var restoredRetime = (SpawnHitboxOperationSource)workspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[0].Operations[retimeOperationIndex];
            if (restoredRetime.Tick != retimeHitbox.Tick || restoredRetime.Hitbox.DurationTicks != retimeHitbox.Hitbox.DurationTicks)
                throw new InvalidOperationException("Timeline retime undo did not restore the source operation.");
            int retimeDuration = retimeHitbox.Hitbox.DurationTicks > 1
                ? retimeHitbox.Hitbox.DurationTicks - 1
                : retimeHitbox.Hitbox.DurationTicks < retimeStage.DurationTicks - retimeHitbox.Tick
                    ? retimeHitbox.Hitbox.DurationTicks + 1
                    : retimeHitbox.Hitbox.DurationTicks;
            if (retimeDuration == retimeHitbox.Hitbox.DurationTicks ||
                !workspace.ReplaceHitboxDuration("ground.1", 0, retimeOperationIndex, retimeDuration) ||
                ((SpawnHitboxOperationSource)workspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[0].Operations[retimeOperationIndex]).Hitbox.DurationTicks != retimeDuration)
                throw new InvalidOperationException("Safe hitbox endpoint retime did not update the source duration.");
            workspace.Undo();
            if (((SpawnHitboxOperationSource)workspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[0].Operations[retimeOperationIndex]).Hitbox.DurationTicks != retimeHitbox.Hitbox.DurationTicks)
                throw new InvalidOperationException("Hitbox endpoint undo did not restore the source duration.");

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
