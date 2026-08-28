using System;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using SlopArena.Client.Animation;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Client.Tools;
using SlopArena.Shared;

namespace SlopArena.EditorTools;

public static class AbilityLabFrontendSelfTest
{
    [MenuItem("Tools/SlopArena/Tests/Ability Lab Frontend")]
    public static void Run()
    {
        var labObject = AbilityLab.Instance != null ? null : new GameObject("AbilityLabFrontendSelfTest");
        if (labObject != null)
        {
            labObject.hideFlags = HideFlags.HideAndDontSave;
            labObject.AddComponent<AbilityLab>();
        }
        var window = EditorWindow.GetWindow<AbilityLabWindow>(true, "Ability Lab Self-Test", false);
        AbilityLabPackageWorkspace? fixtureWorkspace = null;
        try
        {
            window.CreateGUI();
            var root = window.rootVisualElement;
            var packageSelector = root.Q<DropdownField>("package-selector");
            var groundOne = root.Q<Button>("selected-ground-1");
            var timeline = root.Q<AbilityLabTimelineElement>("timeline-track");
            if (packageSelector == null || groundOne == null || timeline == null ||
                !packageSelector.choices.Any(choice => choice.Contains("FightGuy", StringComparison.Ordinal)) ||
                !packageSelector.value.Contains("FightGuy", StringComparison.Ordinal) ||
                !groundOne.text.StartsWith("1 · ", StringComparison.Ordinal) ||
                !groundOne.text.Contains("Low Kick", StringComparison.Ordinal))
                throw new InvalidOperationException("FightGuy package, canonical move selector, or friendly label is unavailable.");

            var moveRows = BindingRows(root.Q<VisualElement>("assets-move-bindings"));
            string[] expectedMoveSlots = CanonicalSlotProjection.All.Select(x => x.Id).ToArray();
            if (moveRows.Count != expectedMoveSlots.Length ||
                !moveRows.Select(row => row.tooltip.Split('·')[0].Trim()).SequenceEqual(expectedMoveSlots))
                throw new InvalidOperationException("Assets move rows are not in canonical ground-then-air order.");
            if (root.Q<Label>("compatibility-banner") == null ||
                !root.Q<Label>("compatibility-banner").text.Contains("read-only", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Compatibility mode boundary is not visible.");

            fixtureWorkspace = (AbilityLabPackageWorkspace)typeof(AbilityLabWindow)
                .GetField("_workspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var windowWorkspace = fixtureWorkspace;
            var priorCharacterPreview = windowWorkspace.Preview;
            float priorWeight = windowWorkspace.Draft.Weight;
            if (!windowWorkspace.ReplaceGeneral(
                    windowWorkspace.Draft.DisplayName, priorWeight + 1f, windowWorkspace.Draft.CapsuleRadius,
                    windowWorkspace.Draft.CapsuleHeight, windowWorkspace.Draft.HipHeight, windowWorkspace.Draft.HurtboxRadius) ||
                !windowWorkspace.IsDirty || windowWorkspace.Status != "Stale")
                throw new InvalidOperationException("Source edits did not remain an unsaved authoritative draft.");
            Refresh(window);
            RefreshInspector(window);
            if (root.Q<FloatField>("character-weight").value != priorWeight + 1f ||
                root.Q<Label>("package-status").text != "Unsaved" ||
                !ReferenceEquals(priorCharacterPreview, windowWorkspace.Preview))
                throw new InvalidOperationException("Unsaved source edit did not refresh the Character page.");

            var lab = AbilityLab.Instance;
            var sourceWorkspace = new AbilityLabPackageWorkspace();
            if (!sourceWorkspace.OpenPackage("Assets/CharacterPackages/fightguy") ||
                !sourceWorkspace.TryResolveCanonicalSlot("air.A", out _, out var airSource) ||
                airSource.Name != "Ki Shot")
                throw new InvalidOperationException("Air selector source resolver did not resolve air.A to Ki Shot.");
            lab.SetSlot(CanonicalSlotProjection.All[12]);
            if (lab.SelectedSlotId != "air.A")
                throw new InvalidOperationException("Air selector did not preserve canonical air.A identity.");
            lab.SetSlot(CanonicalSlotProjection.All[0]);
            typeof(AbilityLabWindow).GetMethod("RefreshAll", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, null);
            typeof(AbilityLabWindow).GetMethod("RefreshInspector", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, null);
            var moveAnimation = root.Q<VisualElement>("inspector").Query<PopupField<string>>().ToList()
                .FirstOrDefault(field => field.label.StartsWith("Animation ·", StringComparison.Ordinal));
            var groundAnimationId = windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[0].AnimationIds[0];
            var animationEntry = windowWorkspace.Preview?.AnimationCatalog?.Animations
                .FirstOrDefault(entry => entry != null && entry.SemanticId == groundAnimationId);
            var idleEntry = windowWorkspace.Preview?.AnimationCatalog?.Animations
                .FirstOrDefault(entry => entry != null && entry.SemanticId == windowWorkspace.Draft.Presentation.Idle);
            string moveValue = moveAnimation?.value ?? "<null>";
            string expectedMoveValue = animationEntry?.Clip?.name ?? "<null>";
            string idleValue = root.Q<DropdownField>("presentation-idle").value;
            string expectedIdleValue = idleEntry?.Clip?.name ?? "<null>";
            if (moveAnimation == null || animationEntry?.Clip == null || idleEntry?.Clip == null ||
                !moveValue.Equals(expectedMoveValue, StringComparison.Ordinal) ||
                !idleValue.Equals(expectedIdleValue, StringComparison.Ordinal) ||
                windowWorkspace.Draft.Presentation.Idle != "anim.idle")
                throw new InvalidOperationException(
                    $"Character and Moves animation labels do not preserve semantic IDs. move={moveValue} expectedMove={expectedMoveValue} idle={idleValue} expectedIdle={expectedIdleValue} rawIdle={windowWorkspace.Draft.Presentation.Idle}");
            var catalog = windowWorkspace.Catalog;
            var originalRig = catalog.Rig;
            var catalogBinding = catalog.Bindings.First(binding => binding != null);
            string catalogBindingId = catalogBinding.SemanticId;
            var originalClip = catalogBinding.Clip;
            var originalExtrapolation = catalogBinding.Extrapolation;
            if (!windowWorkspace.ReplaceCatalogRig(null) || catalog.Rig != null)
                throw new InvalidOperationException("Catalog rig replacement did not update the authoritative catalog.");
            windowWorkspace.Undo();
            if (catalog.Rig != originalRig)
                throw new InvalidOperationException("Catalog rig undo did not restore the catalog.");
            windowWorkspace.Redo();
            if (catalog.Rig != null) throw new InvalidOperationException("Catalog rig redo did not restore the edited value.");
            windowWorkspace.Undo();
            Refresh(window);
            if (catalog.Rig != originalRig || root.Q<ObjectField>("assets-rig-field").value != originalRig)
                throw new InvalidOperationException("Catalog rig undo did not restore the catalog and ObjectField together.");
            var replacementBinding = catalog.Bindings.First(binding => binding != null && binding.SemanticId != catalogBindingId);
            if (!windowWorkspace.ReplaceCatalogBinding(catalogBindingId, replacementBinding.Clip, ExtrapolationMode.Continuous) ||
                catalog.Bindings.First(binding => binding != null && binding.SemanticId == catalogBindingId).Clip != replacementBinding.Clip ||
                catalog.Bindings.First(binding => binding != null && binding.SemanticId == catalogBindingId).Extrapolation != ExtrapolationMode.Continuous)
                throw new InvalidOperationException("Catalog clip/extrapolation replacement was rejected.");
            windowWorkspace.Undo();
            var restoredBinding = catalog.Bindings.First(binding => binding != null && binding.SemanticId == catalogBindingId);
            if (restoredBinding.Clip != originalClip || restoredBinding.Extrapolation != originalExtrapolation)
                throw new InvalidOperationException("Catalog binding undo did not restore clip and extrapolation atomically.");
            windowWorkspace.Redo();
            windowWorkspace.Undo();
            if (!windowWorkspace.ReplaceCatalogBinding(catalogBindingId, null, originalExtrapolation))
                throw new InvalidOperationException("Catalog clip clear was rejected.");
            typeof(AbilityLabWindow).GetMethod("RefreshAll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            if (!root.Q<VisualElement>("assets-validation").Query<Label>().ToList()
                    .Any(label => label.text.Contains("asset-catalog.clip.missing", StringComparison.Ordinal)) ||
                !root.Q<ScrollView>("diagnostics-panel").Query<Label>().ToList()
                    .Any(label => label.text.Contains("asset-catalog.clip.missing", StringComparison.Ordinal)))
                throw new InvalidOperationException("Missing catalog clip validation is not visible on Assets and diagnostics.");
            windowWorkspace.Undo();
            var renamedId = catalogBindingId + "-renamed";
            bool canceled = (bool)typeof(AbilityLabWindow).GetMethod("ConfirmAndRenameSemanticId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { catalogBindingId, renamedId, (Func<bool>)(() => false) })!;
            if (canceled || !catalog.Bindings.Any(binding => binding != null && binding.SemanticId == catalogBindingId) ||
                windowWorkspace.Draft.Presentation.Idle == renamedId)
                throw new InvalidOperationException("Canceled semantic-ID rename mutated source or catalog state.");
            bool renamed = (bool)typeof(AbilityLabWindow).GetMethod("ConfirmAndRenameSemanticId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { catalogBindingId, renamedId, (Func<bool>)(() => true) })!;
            if (!renamed || !catalog.Bindings.Any(binding => binding != null && binding.SemanticId == renamedId))
                throw new InvalidOperationException("Confirmed semantic-ID rename did not update the catalog.");
            string collisionId = catalog.Bindings.First(binding => binding != null && binding.SemanticId != renamedId).SemanticId;
            bool collision = (bool)typeof(AbilityLabWindow).GetMethod("ConfirmAndRenameSemanticId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { renamedId, collisionId, (Func<bool>)(() => true) })!;
            if (collision || !windowWorkspace.Diagnostics.Any(diagnostic => diagnostic.Code == "rename.collision"))
                throw new InvalidOperationException("Semantic-ID collision was not rejected.");
            windowWorkspace.Undo();
            if (!catalog.Bindings.Any(binding => binding != null && binding.SemanticId == catalogBindingId) ||
                catalog.Bindings.Any(binding => binding != null && binding.SemanticId == renamedId))
                throw new InvalidOperationException("Rename undo did not restore source and catalog IDs.");
            var editableSlot = sourceWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1");
            int hitboxIndex = editableSlot.Timeline.Stages[0].Operations
                .Select((operation, index) => (operation, index))
                .First(item => item.operation is SpawnHitboxOperationSource).index;
            var beforeDamage = ((SpawnHitboxOperationSource)editableSlot.Timeline.Stages[0].Operations[hitboxIndex]).Hitbox.Damage;
            if (!sourceWorkspace.ReplaceHitbox("ground.1", 0, hitboxIndex,
                ((SpawnHitboxOperationSource)editableSlot.Timeline.Stages[0].Operations[hitboxIndex]).Hitbox with { Damage = beforeDamage + 1f }) ||
                !sourceWorkspace.CanUndo)
                throw new InvalidOperationException("Source-owned hitbox edit did not create one undo snapshot.");
            sourceWorkspace.Undo();
            if (((SpawnHitboxOperationSource)sourceWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[0].Operations[hitboxIndex]).Hitbox.Damage != beforeDamage)
                throw new InvalidOperationException("Source-owned hitbox undo did not restore the prior value.");
            if (timeline.Projection == null || timeline.Projection.DurationTicks <= 0 || timeline.Projection.Stages.Count == 0)
                throw new InvalidOperationException("Cumulative authored timeline projection is unavailable.");
            var selectedProjection = timeline.Projection.Stages.SelectMany(stage => stage.Operations)
                .First(operation => operation.Source is SpawnHitboxOperationSource);
            timeline.SelectedOperation = selectedProjection;
            int selectedStage = selectedProjection.SourceStageIndex;
            int selectedOperation = selectedProjection.SourceOperationIndex;
            var selectedSourceSlot = sourceWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1");
            var selectedSource = (SpawnHitboxOperationSource)selectedSourceSlot.Timeline.Stages[selectedStage].Operations[selectedOperation];
            if (!sourceWorkspace.ReplaceHitbox("ground.1", selectedStage, selectedOperation,
                    selectedSource.Hitbox with { Damage = selectedSource.Hitbox.Damage + 1f }) ||
                !sourceWorkspace.ReplaceHitbox("ground.1", selectedStage, selectedOperation,
                    ((SpawnHitboxOperationSource)sourceWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1")
                        .Timeline.Stages[selectedStage].Operations[selectedOperation]).Hitbox with { Angle = selectedSource.Hitbox.Angle + 1f }))
                throw new InvalidOperationException("Sequential source hitbox edits were rejected.");
            timeline.Projection = AbilityLabTimelineProjection.Build(
                sourceWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1"));
            if (timeline.SelectedOperation == null ||
                timeline.SelectedOperation.SourceStageIndex != selectedStage ||
                timeline.SelectedOperation.SourceOperationIndex != selectedOperation)
                throw new InvalidOperationException("Operation selection was lost after sequential hitbox edits.");
            sourceWorkspace.Undo();
            sourceWorkspace.Undo();
            if (timeline.Projection == null || timeline.Projection.DurationTicks <= 0 || timeline.Projection.Stages.Count == 0)
                throw new InvalidOperationException("Cumulative authored timeline projection is unavailable.");
            int operationCount = timeline.Projection.Stages.Sum(stage => stage.Operations.Count);
            float minimumTimelineHeight = 18f + operationCount * 20f;
            if (timeline.resolvedStyle.height < Math.Max(84f, minimumTimelineHeight))
                throw new InvalidOperationException("Timeline content height clips projected operation rows.");
            if (lab.WorkingEvents.Count != 0)
                throw new InvalidOperationException("Transient WorkingEvents is still the package edit path.");
            var timelineScroll = root.Q<ScrollView>("timeline-scroll");
            var timelineZoom = root.Q<Slider>("timeline-zoom");
            var stageSelector = root.Q<DropdownField>("stage-selector");
            if (stageSelector != null && stageSelector.style.display != DisplayStyle.None)
                throw new InvalidOperationException("Single-stage move still exposes the Moves stage selector.");
            if (!root.focusable || !timeline.focusable || timelineScroll == null ||
                timelineZoom.lowValue != 0.5f || timelineZoom.highValue != 4f || timelineZoom.value != 1f)
                throw new InvalidOperationException("Timeline zoom, scroll, or keyboard focus contract is missing.");

            var timelineSlot = windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1");
            int moveStageIndex = 0;
            int moveOperationIndex = 0;
            var editableStage = timelineSlot.Timeline.Stages[moveStageIndex];
            var moveOperation = editableStage.Operations[moveOperationIndex];
            int oldMoveTick = moveOperation.Tick;
            int moveDuration = moveOperation is SpawnHitboxOperationSource moveHitbox ? moveHitbox.Hitbox.DurationTicks : 0;
            int maxMoveTick = editableStage.DurationTicks - moveDuration - (moveOperation is SpawnHitboxOperationSource ? 0 : 1);
            int newMoveTick = oldMoveTick > 0 ? oldMoveTick - 1 : oldMoveTick + 1;
            if (newMoveTick > maxMoveTick)
                newMoveTick = oldMoveTick - 1;
            if (newMoveTick < 0 || newMoveTick == oldMoveTick)
                throw new InvalidOperationException("FightGuy move operation has no playable drag target.");
            lab.SetSlot(CanonicalSlotProjection.All[0]);
            var dragMethod = typeof(AbilityLabWindow).GetMethod("CompleteTimelineDrag", BindingFlags.Instance | BindingFlags.NonPublic)!;
            dragMethod.Invoke(window, new object[] { new AbilityLabTimelineDrag(moveStageIndex, moveOperationIndex, TimelineDragMode.Move, newMoveTick, 0) });
            var moved = windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[moveStageIndex].Operations[moveOperationIndex];
            if (moved.Tick != newMoveTick || timeline.SelectedOperation == null ||
                timeline.SelectedOperation.SourceStageIndex != moveStageIndex ||
                timeline.SelectedOperation.SourceOperationIndex != moveOperationIndex || !windowWorkspace.CanUndo)
                throw new InvalidOperationException("Timeline move drag did not commit one source-addressed edit.");
            windowWorkspace.Undo();
            if (windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[moveStageIndex].Operations[moveOperationIndex].Tick != oldMoveTick)
                throw new InvalidOperationException("One timeline Undo did not restore the prior operation tick.");

            int hitboxStageIndex = -1;
            int hitboxOperationIndex = -1;
            for (int stageIndex = 0; stageIndex < timelineSlot.Timeline.Stages.Count && hitboxStageIndex < 0; stageIndex++)
                for (int operationIndex = 0; operationIndex < timelineSlot.Timeline.Stages[stageIndex].Operations.Count; operationIndex++)
                    if (timelineSlot.Timeline.Stages[stageIndex].Operations[operationIndex] is SpawnHitboxOperationSource)
                    {
                        hitboxStageIndex = stageIndex;
                        hitboxOperationIndex = operationIndex;
                        break;
                    }
            if (hitboxStageIndex < 0)
                throw new InvalidOperationException("FightGuy has no hitbox timeline operation for endpoint verification.");
            var hitboxStage = timelineSlot.Timeline.Stages[hitboxStageIndex];
            var originalHitbox = (SpawnHitboxOperationSource)hitboxStage.Operations[hitboxOperationIndex];
            int maxDuration = hitboxStage.DurationTicks - originalHitbox.Tick;
            int newDuration = originalHitbox.Hitbox.DurationTicks > 1
                ? originalHitbox.Hitbox.DurationTicks - 1
                : originalHitbox.Hitbox.DurationTicks < maxDuration ? originalHitbox.Hitbox.DurationTicks + 1 : originalHitbox.Hitbox.DurationTicks;
            if (newDuration == originalHitbox.Hitbox.DurationTicks)
                throw new InvalidOperationException("FightGuy hitbox has no safe endpoint drag target.");
            if (!windowWorkspace.ReplaceHitboxDuration("ground.1", hitboxStageIndex, hitboxOperationIndex, newDuration))
                throw new InvalidOperationException("Timeline endpoint duration edit was rejected.");
            var resized = (SpawnHitboxOperationSource)windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1").Timeline.Stages[hitboxStageIndex].Operations[hitboxOperationIndex];
            if (resized.Hitbox.DurationTicks != newDuration || !windowWorkspace.CanUndo)
                throw new InvalidOperationException("Timeline endpoint edit did not commit one source duration snapshot.");
            windowWorkspace.Undo();
            var inspectorIdentity = root.Q<VisualElement>("inspector").ElementAt(0);
            ApplyTick(window, 1);
            if (!ReferenceEquals(inspectorIdentity, root.Q<VisualElement>("inspector").ElementAt(0)))
                throw new InvalidOperationException("Timeline scrubbing rebuilt inspector controls.");

            float keyboardWeight = windowWorkspace.Draft.Weight;
            if (!windowWorkspace.ReplaceGeneral(windowWorkspace.Draft.DisplayName, keyboardWeight + 1f, windowWorkspace.Draft.CapsuleRadius,
                    windowWorkspace.Draft.CapsuleHeight, windowWorkspace.Draft.HipHeight, windowWorkspace.Draft.HurtboxRadius))
                throw new InvalidOperationException("Keyboard routing fixture edit was rejected.");
            var guardedKey = KeyDownEvent.GetPooled('\0', KeyCode.Z, EventModifiers.Control);
            guardedKey.target = root.Q<FloatField>("character-weight");
            typeof(AbilityLabWindow).GetMethod("OnRootKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { guardedKey });
            guardedKey.Dispose();
            if (Math.Abs(windowWorkspace.Draft.Weight - (keyboardWeight + 1f)) > 0.0001f)
                throw new InvalidOperationException("Editor shortcut was not guarded inside an integer/float input.");
            var undoKey = KeyDownEvent.GetPooled('\0', KeyCode.Z, EventModifiers.Control);
            undoKey.target = root;
            typeof(AbilityLabWindow).GetMethod("OnRootKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { undoKey });
            undoKey.Dispose();
            if (Math.Abs(windowWorkspace.Draft.Weight - keyboardWeight) > 0.0001f)
                throw new InvalidOperationException("Ctrl+Z did not route to workspace Undo.");

            var preview = AbilityLabPackagePreviewLoader.Load("fightguy");
            if (!preview.IsAvailable || preview.Identity == null || preview.Identity.PackageId != "fightguy" || preview.Slots.Count != 16)
                throw new InvalidOperationException("FightGuy read-only preview seam is unavailable.");
            var unavailable = AbilityLabPackagePreviewLoader.Load("unavailable-package");
            if (unavailable.IsAvailable || unavailable.Diagnostics.Count == 0 || unavailable.Identity != null)
                throw new InvalidOperationException("Unavailable package did not expose structured diagnostics.");

            var compatibility = root.Q<Label>("compatibility-banner");
            if (compatibility == null || !compatibility.text.Contains("read-only", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Compatibility legacy-authority banner is missing.");
            var legacySelector = root.Q<DropdownField>("legacy-selector");
            if (legacySelector == null || !legacySelector.choices.SequenceEqual(new[] { "Manki", "Kistu", "Nilus" }))
                throw new InvalidOperationException("Compatibility selector must expose exactly Manki, Kistu, and Nilus.");
            var authority = root.Q<Label>("compatibility-authority");
            if (authority == null ||
                !authority.text.Contains("Compatibility Preview", StringComparison.Ordinal) ||
                !authority.text.Contains("legacy authority", StringComparison.OrdinalIgnoreCase) ||
                !authority.text.Contains("read-only", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Compatibility authority status is missing.");

            SelectTab(window, "compatibility-page");
            if (root.Q<DropdownField>("package-selector").style.display != DisplayStyle.None ||
                root.Q<Button>("package-status-toggle").style.display != DisplayStyle.None ||
                root.Q<Label>("package-status").style.display != DisplayStyle.None ||
                root.Q<Button>("toolbar-undo").style.display != DisplayStyle.None ||
                root.Q<Button>("toolbar-redo").style.display != DisplayStyle.None ||
                root.Q<Button>("toolbar-save").style.display != DisplayStyle.None ||
                root.Q<ScrollView>("diagnostics-panel").style.display != DisplayStyle.None)
                throw new InvalidOperationException("Compatibility mode left package controls or diagnostics visible.");

            SelectTab(window, "moves-page");
            if (lab.SelectedPackageId != "fightguy")
                throw new InvalidOperationException("Leaving Compatibility did not restore the last valid FightGuy preview.");

            if (Application.isPlaying)
            {
                legacySelector.value = "Manki";
                InvokeButton(root.Q<Button>("tab-compatibility"));
                InvokeButton(root.Q<Button>("legacy-load"));
                if (lab.Character != CharacterClass.Manki || lab.Renderer == null ||
                    !authority.text.Contains("Compatibility Preview · Manki · Legacy authority · Read-only", StringComparison.Ordinal))
                    throw new InvalidOperationException("Manki compatibility preview did not load through the rooted resolver.");

                if (!lab.TryGetStage(out var stage) || stage.DurationTicks < 2)
                    throw new InvalidOperationException("Loaded legacy stage is not scrub-able.");
                int scrubTick = Math.Min(1, stage.DurationTicks - 1);
                var compatibilitySlider = root.Q<SliderInt>("compatibility-slider");
                compatibilitySlider.SetValueWithoutNotify(scrubTick);
                var sliderChange = ChangeEvent<int>.GetPooled(0, scrubTick);
                sliderChange.target = compatibilitySlider;
                compatibilitySlider.SendEvent(sliderChange);
                sliderChange.Dispose();
                var compatibilitySlot = root.Q<DropdownField>("compatibility-slot-selector");
                compatibilitySlot.SetValueWithoutNotify(AbilityLab.SlotNames[1]);
                lab.SetSlot(AbilityLab.SlotIndices[1]);
                if (lab.SlotIndex != AbilityLab.SlotIndices[1])
                    throw new InvalidOperationException("Compatibility slot selection did not update the runtime preview.");
                int stageCount = lab.CurrentSpec()?.Stages?.Length ?? 0;
                if (stageCount > 1)
                {
                    var compatibilityStage = root.Q<DropdownField>("compatibility-stage-selector");
                    compatibilityStage.SetValueWithoutNotify("Stage 2");
                    lab.SetStage(1);
                    if (lab.StageIndex != 1)
                        throw new InvalidOperationException("Compatibility stage selection did not update the runtime preview.");
                }

                int workingEventCount = lab.WorkingEvents.Count;
                int hitstopOverrideCount = lab.WorkingHitstopOverrides.Count;
                lab.SetHitstopMultiplier(2f);
                lab.SetWorkingEvent(0, default);
                lab.AddWorkingEvent();
                lab.RemoveWorkingEvent(0);
                if (lab.WorkingEvents.Count != workingEventCount ||
                    lab.WorkingHitstopOverrides.Count != hitstopOverrideCount)
                    throw new InvalidOperationException("Legacy compatibility mutation guard changed transient edit state.");
            }

            Debug.Log("[AbilityLabFrontendSelfTest] Passed canonical controls, compatibility mode boundary, legacy bindings, package preview seam, and source-edit boundary checks.");
        }
        finally
        {
            EditorApplication.delayCall += () =>
            {
                Selection.activeObject = null;
                if (window != null)
                {
                    window.rootVisualElement?.Clear();
                    window.Close();
                }
                void DestroyTemporaryObject()
                {
                    EditorApplication.update -= DestroyTemporaryObject;
                    if (labObject != null) UnityEngine.Object.DestroyImmediate(labObject);
                }
                EditorApplication.update += DestroyTemporaryObject;
            };
        }
    }
    private static void ApplyTick(AbilityLabWindow window, int tick)
        => typeof(AbilityLabWindow).GetMethod("ApplyCumulativeTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, new object[] { tick });
    private static void Refresh(AbilityLabWindow window)
        => typeof(AbilityLabWindow).GetMethod("RefreshAll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    private static void RefreshInspector(AbilityLabWindow window)
        => typeof(AbilityLabWindow).GetMethod("RefreshInspector", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    private static System.Collections.Generic.List<VisualElement> BindingRows(VisualElement parent)
        => parent.Query<VisualElement>().ToList().Where(element => element.userData is string).ToList();
    private static void SelectTab(AbilityLabWindow window, string pageName)
        => typeof(AbilityLabWindow).GetMethod("SelectTab", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, new object[] { pageName });
    private static void InvokeButton(Button button)
    {
        var method = typeof(Clickable).GetMethod("SimulateSingleClick",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method!.Invoke(button.clickable, new object[] { null, 0 });
    }
}
