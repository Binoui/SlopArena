using System.Collections.Generic;
using System;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using SlopArena.Client.Entities;
using SlopArena.Client.Animation;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Client.Tools;
using SlopArena.Shared;

namespace SlopArena.EditorTools;

public static class AbilityLabFrontendSelfTest
{
    public static void RunAttachmentDraftInvariants()
    {
        var config = ScriptableObject.CreateInstance<WeaponAttachConfig>();
        var catalog = ScriptableObject.CreateInstance<CharacterAssetCatalog>();
        var workspace = new AbilityLabPackageWorkspace();
        WeaponAttachConfig preview = null;
        string assetPath = $"Assets/__AttachmentDraftTest-{Guid.NewGuid():N}.asset";
        var entry = new WeaponEntry
        {
            AttackSlot = 7,
            BoneName = "hand",
            PositionOffset = new Vector3(1f, 2f, 3f),
            RotationOffset = new Vector3(4f, 5f, 6f),
            TrailBladeWidth = 0.63f,
            TrailHiltAnchor = "hilt",
        };
        config.Entries = new[] { entry, new WeaponEntry() };
        catalog.WeaponConfig = config;
        typeof(AbilityLabPackageWorkspace).GetProperty(nameof(AbilityLabPackageWorkspace.Catalog))!
            .SetValue(workspace, catalog);
        try
        {
            AssetDatabase.CreateAsset(config, assetPath);
            Vector3 originalPosition = entry.PositionOffset;
            Vector3 originalRotation = entry.RotationOffset;
            if (!workspace.BeginAttachmentDraft(0) ||
                !workspace.SetAttachmentPositionOffset(new Vector3(11f, 12f, 13f)) ||
                entry.PositionOffset != originalPosition ||
                workspace.AttachmentHasFirePhaseOverride ||
                !workspace.SetAttachmentFirePhaseOverride(true) ||
                workspace.AttachmentFirePositionOffset != new Vector3(11f, 12f, 13f) ||
                workspace.AttachmentFireRotationOffset != originalRotation ||
                workspace.BeginAttachmentDraft(1) ||
                workspace.OpenPackage("different-package"))
                throw new InvalidOperationException("Attachment edits mutated source, failed fire override initialization, or lost a dirty selection.");

            if (workspace.SetAttachmentFirePositionOffset(new Vector3(float.PositiveInfinity, 0f, 0f)) ||
                workspace.AttachmentFirePositionOffset != new Vector3(11f, 12f, 13f))
                throw new InvalidOperationException("Nonfinite attachment offsets were accepted.");
            preview = workspace.CreateAttachmentPreviewConfig();
            var previewEntry = preview.Entries[0];
            if (ReferenceEquals(previewEntry, entry) ||
                previewEntry.PositionOffset != new Vector3(11f, 12f, 13f) ||
                previewEntry.FirePositionOffset != new Vector3(11f, 12f, 13f) ||
                previewEntry.TrailBladeWidth != entry.TrailBladeWidth ||
                previewEntry.TrailHiltAnchor != entry.TrailHiltAnchor ||
                entry.PositionOffset != originalPosition)
                throw new InvalidOperationException("Attachment preview did not clone draft placement while preserving source and trail data.");
            UnityEngine.Object.DestroyImmediate(preview);
            preview = null;

            if (!workspace.SetAttachmentFirePositionOffset(new Vector3(21f, 22f, 23f)) ||
                !workspace.UndoAttachmentDraft() ||
                workspace.AttachmentFirePositionOffset != new Vector3(11f, 12f, 13f) ||
                !workspace.RedoAttachmentDraft() ||
                workspace.AttachmentFirePositionOffset != new Vector3(21f, 22f, 23f))
                throw new InvalidOperationException("Attachment draft Undo/Redo did not restore placement values.");

            entry.PositionOffset = new Vector3(31f, 32f, 33f);
            if (workspace.SaveAttachmentDraft() || entry.PositionOffset != new Vector3(31f, 32f, 33f))
                throw new InvalidOperationException("Attachment source conflict did not block save without overwriting the external value.");
            if (!workspace.RevertAttachmentDraft() || workspace.HasAttachmentDraft)
                throw new InvalidOperationException("Attachment Revert did not discard the transient draft.");

            if (!workspace.BeginAttachmentDraft(0) ||
                !workspace.SetAttachmentPositionOffset(new Vector3(41f, 42f, 43f)) ||
                !workspace.SetAttachmentFirePhaseOverride(true) ||
                !workspace.SetAttachmentFireRotationOffset(new Vector3(51f, 52f, 53f)) ||
                !workspace.SaveAttachmentDraft() ||
                entry.PositionOffset != new Vector3(41f, 42f, 43f) ||
                !entry.HasFirePhaseOverride ||
                entry.FireRotationOffset != new Vector3(51f, 52f, 53f) ||
                entry.TrailBladeWidth != 0.63f || entry.TrailHiltAnchor != "hilt" ||
                entry.AttackSlot != 7 || entry.BoneName != "hand")
                throw new InvalidOperationException("Attachment Save did not persist only attachment fields while preserving trail and unrelated entry data.");
            if (!workspace.SetAttachmentRotationOffset(new Vector3(61f, 62f, 63f)) ||
                !workspace.RevertAttachmentDraft() ||
                entry.RotationOffset != originalRotation)
                throw new InvalidOperationException("Attachment Revert failed to discard unsaved changes after save.");
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<WeaponAttachConfig>(assetPath);
            if (reloaded == null || reloaded.Entries[0].PositionOffset != new Vector3(41f, 42f, 43f)
                || reloaded.Entries[0].FireRotationOffset != new Vector3(51f, 52f, 53f)
                || reloaded.Entries[0].RotationOffset != originalRotation)
                throw new InvalidOperationException("Saved attachment values did not survive source asset reimport.");
        }
        finally
        {
            if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
            UnityEngine.Object.DestroyImmediate(catalog);
            if (!AssetDatabase.DeleteAsset(assetPath) && config != null) UnityEngine.Object.DestroyImmediate(config);
        }
    }

    public static void RunCommandInvariants()
    {
        RunAttachmentDraftInvariants();
        var stages = new[]
        {
            new CharacterStageSource(4, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<CharacterTimelineOperationSource>()),
            new CharacterStageSource(5, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<CharacterTimelineOperationSource>()),
        };
        var projection = AbilityLabTimelineProjection.Build(new CharacterSlotSource(
            "ground.1", "Test", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false, new CharacterTimelineSource(stages)));

        AssertApplied(3, 0, 3, 3);
        AssertApplied(4, 1, 0, 4);
        AssertApplied(9, 1, 4, 8);
        if (!AbilityLabWindow.CanCommandOpenPackage(true, "wibou", true, "wibou") ||
            AbilityLabWindow.CanCommandOpenPackage(true, "wibou", true, "manki") ||
            !AbilityLabWindow.CanCommandOpenPackage(true, "wibou", false, "manki"))
            throw new InvalidOperationException("Dirty same-target retention or dirty package switch guard regressed.");
        if (!SlopArenaAbilityLabCommands.TryParseTicks(
                string.Join(",", Enumerable.Range(0, 64)), out var batch, out _) || batch.Count != 64 ||
            SlopArenaAbilityLabCommands.TryParseTicks("19,19", out _, out _) ||
            SlopArenaAbilityLabCommands.TryParseTicks(
                string.Join(",", Enumerable.Range(0, 65)), out _, out _))
            throw new InvalidOperationException("Capture batch parsing did not enforce distinct ticks and the 64-sample limit.");
        if (SlopArenaAbilityLabCommands.TryResolveCaptureDirectory(".ability-lab-cache/../escape", out _, out _) ||
            SlopArenaAbilityLabCommands.TryResolveCaptureDirectory(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ability-lab"), out _, out _))
            throw new InvalidOperationException("Capture output validation accepted traversal or an absolute path.");
        string repositoryRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "..", ".."));
        if (!SlopArenaAbilityLabCommands.TryResolveCaptureDirectory(
                ".ability-lab-cache/command-invariants", out string captureDirectory, out _) ||
            System.IO.Path.GetRelativePath(repositoryRoot, captureDirectory).Replace('\\', '/') !=
                ".ability-lab-cache/command-invariants")
            throw new InvalidOperationException("Capture paths are not rooted in the repository artifact cache.");


        void AssertApplied(int requested, int expectedStage, ushort expectedLocal, int expectedCumulative)
        {
            if (!AbilityLabWindow.TryResolveCumulativeTick(
                    projection, requested, out var stage, out ushort local, out int cumulative) ||
                stage.SourceStageIndex != expectedStage || local != expectedLocal || cumulative != expectedCumulative)
                throw new InvalidOperationException(
                    $"Cumulative tick {requested} resolved to stage {stage?.SourceStageIndex}, local {local}, cumulative {cumulative}.");
        }
    }

    [MenuItem("Tools/SlopArena/Tests/Ability Lab Frontend")]
    public static void Run()
    {
        CleanupOrphanedFixture();
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
            var moveSelector = root.Q<VisualElement>("move-selector");
            var moveList = root.Q<VisualElement>("move-list");
            var groundAirSelector = root.Q<VisualElement>("ground-air-selector");
            var diagnosticsPanel = root.Q<ScrollView>("diagnostics-panel");
            var scenarioDistance = root.Q<FloatField>("scenario-distance");
            var scenarioFacing = root.Q<FloatField>("scenario-facing");
            var scenarioDamage = root.Q<IntegerField>("scenario-damage");
            var scenarioOpponent = root.Q<DropdownField>("scenario-opponent");
            var scenarioHorizon = root.Q<IntegerField>("scenario-horizon");
            var scenarioRun = root.Q<Button>("scenario-run");
            var scenarioExit = root.Q<Button>("scenario-exit");
            var scenarioOutcomes = root.Q<Label>("scenario-outcomes");
            var scenarioAction = root.Q<Label>("scenario-action");
            var rowLabels = timeline?.Query<Label>().ToList()
                .Where(label => label.ClassListContains("timeline-row-label"))
                .Select(label => label.text)
                .ToList() ?? new List<string>();
            if (packageSelector == null || groundOne == null || timeline == null ||
                scenarioDistance == null || scenarioFacing == null || scenarioDamage == null ||
                scenarioOpponent == null || scenarioHorizon == null || scenarioRun == null ||
                scenarioExit == null || scenarioOutcomes == null || scenarioAction == null ||
                !scenarioRun.enabledSelf ||
                scenarioDistance.label != "Opponent distance (m)" ||
                scenarioFacing.label != "Opponent relative facing (°)" ||
                scenarioDamage.label != "Opponent starting damage (%)" ||
                scenarioOpponent.label != "Opponent behavior" || scenarioHorizon.label != "Last frame" ||
                scenarioRun.text != "Run scenario" || scenarioExit.text != "Exit to authoring" ||
                !ReferenceEquals(groundAirSelector.parent, moveSelector) ||
                !ReferenceEquals(moveList.parent, moveSelector) ||
                moveList.childCount != 9 ||
                diagnosticsPanel.Query<Label>().ToList().Any(label => label.text == "No diagnostics.") ||
                (diagnosticsPanel.childCount == 0 && diagnosticsPanel.style.display != DisplayStyle.None) ||
                !packageSelector.choices.Any(choice => choice.Contains("FightGuy", StringComparison.Ordinal)) ||
                !packageSelector.value.Contains("FightGuy", StringComparison.Ordinal) ||
                !moveList.Query<Button>().ToList().Select(button => button.text)
                    .SequenceEqual(new[] { "1", "2", "3", "4", "Q", "E", "R", "F", "Grab" }) ||
                rowLabels.Count == 0 ||
                !rowLabels.Any(label => label == "Hitbox" || label == "Projectile" || label == "Presentation" ||
                    label == "Capability" || label == "Velocity" || label == "Aim" || label == "Complete") ||
                rowLabels.Any(label => label.Contains("Operation", StringComparison.Ordinal) || label.Contains("Source", StringComparison.Ordinal)))
                throw new InvalidOperationException("FightGuy package, compact move selector, diagnostics collapse, or friendly timeline labels are unavailable.");

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
            var inspector = root.Q<VisualElement>("inspector");
            var addGravityWindow = inspector.Query<Button>().ToList()
                .FirstOrDefault(button => button.text == "Add gravity window");
            if (addGravityWindow == null ||
                !inspector.Query<Button>().ToList().Any(button => button.text == "Add forward lunge"))
                throw new InvalidOperationException("Timeline gravity-window or forward-lunge control is unavailable.");
            InvokeButton(addGravityWindow);
            if (windowWorkspace.Draft.Slots.Single(slot => slot.Id == "ground.1")
                    .Timeline.Stages[0].Operations.LastOrDefault() is not GravityWindowOperationSource gravityWindow ||
                !inspector.Query<FloatField>().ToList().Any(field => field.label == "Gravity scale") ||
                !inspector.Query<IntegerField>().ToList().Any(field => field.label == "Start tick") ||
                !inspector.Query<IntegerField>().ToList().Any(field => field.label == "Duration ticks"))
                throw new InvalidOperationException("Gravity-window insertion or its editable timing/scale controls are unavailable.");

            if (root.Q<FloatField>("character-weight").value != priorWeight + 1f ||
                root.Q<Button>("package-status-toggle").text != "Unsaved" ||
                root.Q<Label>("package-status") != null ||
                !ReferenceEquals(priorCharacterPreview, windowWorkspace.Preview))
                throw new InvalidOperationException("Unsaved source edit did not refresh the Character page.");

            var lab = AbilityLab.Instance;
            if (!lab.IsPackagePreview || lab.ShowHurtboxes || !lab.ShowHitboxes || lab.ShowBakedBones || lab.ShowDummy)
                throw new InvalidOperationException("Package preview debug defaults are not readable.");
            var characterRoot = lab.Renderer;
            var dummyRoot = lab.DummyRenderer;
            if (characterRoot == null || dummyRoot == null)
                throw new InvalidOperationException("Ability Lab did not bind both stable preview renderer slots.");
            int characterRootId = characterRoot.GetInstanceID();
            int dummyRootId = dummyRoot.GetInstanceID();
            Refresh(window);
            Refresh(window);
            if (lab.Renderer.GetInstanceID() != characterRootId ||
                lab.DummyRenderer.GetInstanceID() != dummyRootId ||
                lab.transform.Cast<Transform>().Count(child =>
                    child.name == "LabCharacter" || child.name == "LabDummy") != 2)
                throw new InvalidOperationException("Repeated preview refresh did not reuse exactly one renderer slot pair.");

            if (characterRoot.transform.childCount != 1)
                throw new InvalidOperationException("Stable character renderer does not contain exactly one model child.");
            UnityEngine.Object.DestroyImmediate(characterRoot.transform.GetChild(0).gameObject);
            Refresh(window);
            if (lab.Renderer.GetInstanceID() != characterRootId || characterRoot.transform.childCount != 1)
                throw new InvalidOperationException(
                    $"Refreshing a deleted model did not restore it under the stable character slot. childCount={characterRoot.transform.childCount} children={string.Join(",", characterRoot.transform.Cast<Transform>().Select(child => child.name))}");
            int restoredModelId = characterRoot.transform.GetChild(0).GetInstanceID();
            Refresh(window);
            if (characterRoot.transform.childCount != 1 ||
                characterRoot.transform.GetChild(0).GetInstanceID() != restoredModelId)
                throw new InvalidOperationException("Repeated refresh replaced a valid recovered model.");
            var sourceWorkspace = new AbilityLabPackageWorkspace();
            lab.SetSlot(CanonicalSlotProjection.All[0]);
            lab.SetStage(0);
            var liveSlot = windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.1");
            int liveHitboxOperationIndex = liveSlot.Timeline.Stages[0].Operations
                .Select((operation, index) => (operation, index))
                .First(item => item.operation is SpawnHitboxOperationSource).index;
            var liveSourceHitbox = (SpawnHitboxOperationSource)liveSlot.Timeline.Stages[0].Operations[liveHitboxOperationIndex];
            int liveHitboxOrdinal = liveSlot.Timeline.Stages[0].Operations
                .Take(liveHitboxOperationIndex)
                .Count(operation => operation is SpawnHitboxOperationSource);
            lab.SetTick(liveSourceHitbox.Tick);
            var beforeLive = lab.ResolveHitboxes().Single(item => item.index == liveHitboxOrdinal);
            var persistedPreview = windowWorkspace.Preview;
            var editedLiveHitbox = liveSourceHitbox.Hitbox with
            {
                OffsetZ = liveSourceHitbox.Hitbox.OffsetZ + 0.25f,
                Radius = liveSourceHitbox.Hitbox.Radius + 0.1f,
            };
            if (!windowWorkspace.ReplaceHitbox("ground.1", 0, liveHitboxOperationIndex, editedLiveHitbox) ||
                !windowWorkspace.IsDirty || windowWorkspace.Status != "Stale" ||
                !ReferenceEquals(persistedPreview, windowWorkspace.Preview) ||
                windowWorkspace.LiveDraftPackage == null ||
                lab.PreviewStatus != "Live draft" || !lab.IsPackagePreview)
                throw new InvalidOperationException("Accepted hitbox edit did not publish a live in-memory preview.");
            var afterLive = lab.ResolveHitboxes().Single(item => item.index == liveHitboxOrdinal);
            if (Math.Abs(afterLive.evt.Radius - beforeLive.evt.Radius) < 0.0001f ||
                afterLive.start == beforeLive.start && afterLive.end == beforeLive.end)
                throw new InvalidOperationException("Live hitbox edit did not update event or world endpoint geometry.");

            var invalidLiveHitbox = editedLiveHitbox with { DurationTicks = 0 };
            if (!windowWorkspace.ReplaceHitbox("ground.1", 0, liveHitboxOperationIndex, invalidLiveHitbox) ||
                windowWorkspace.Status != "Failed" || !windowWorkspace.LiveDraftInvalid ||
                windowWorkspace.LiveDraftPackage != null || lab.IsPackagePreview ||
                !ReferenceEquals(persistedPreview, windowWorkspace.Preview))
                throw new InvalidOperationException("Compiler-invalid hitbox edit did not block the package preview.");
            windowWorkspace.Undo();
            if (windowWorkspace.LiveDraftPackage == null || windowWorkspace.LiveDraftInvalid ||
                lab.PreviewStatus != "Live draft" || !lab.IsPackagePreview)
                throw new InvalidOperationException("Undo did not restore the valid live package preview.");
            var restoredLive = lab.ResolveHitboxes().Single(item => item.index == liveHitboxOrdinal);
            if (restoredLive.evt.Radius != afterLive.evt.Radius ||
                restoredLive.start != afterLive.start || restoredLive.end != afterLive.end)
                throw new InvalidOperationException("Undo did not restore live hitbox geometry.");
            if (!windowWorkspace.OpenPackage("Assets/CharacterPackages/manki"))
                throw new InvalidOperationException("Manki package could not be opened for presentation preview tests.");
            Refresh(window);
            var mankiLab = AbilityLab.Instance;
            var mankiAddress = CanonicalSlotProjection.All.First(address => address.Id == "ground.F");
            mankiLab.SetSlot(mankiAddress);
            mankiLab.SetStage(0);
            var mankiSlot = windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.F");
            var mankiPresentationOperation = mankiSlot.Timeline.Stages[0].Operations
                .OfType<EmitPresentationOperationSource>().Single();
            var authoredMankiPlacement = mankiPresentationOperation.Placement with
            {
                AttachmentMode = AuthoringPresentationAttachmentMode.Bone,
                BoneId = "bone.right-hand",
                LocalRotationX = 90f,
                LocalScaleX = 1.15f,
                LocalScaleY = 1.15f,
                LocalScaleZ = 1.15f,
                DurationTicks = 28,
            };
            int mankiPresentationOperationIndex = mankiSlot.Timeline.Stages[0].Operations
                .Select((operation, index) => (operation, index))
                .Single(item => item.operation is EmitPresentationOperationSource).index;
            if (!windowWorkspace.ReplacePresentationPlacement("ground.F", 0,
                    mankiPresentationOperationIndex, authoredMankiPlacement))
                throw new InvalidOperationException("Manki presentation bone placement was rejected.");
            Refresh(window);
            mankiSlot = windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.F");
            mankiPresentationOperation = mankiSlot.Timeline.Stages[0].Operations
                .OfType<EmitPresentationOperationSource>().Single();
            var mankiPresentationProjection = AbilityLabTimelineProjection.Build(mankiSlot).Stages[0].Operations
                .Single(operation => operation.Source is EmitPresentationOperationSource);
            typeof(AbilityLabWindow).GetMethod("SelectOperation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { mankiPresentationProjection });
            mankiLab.SetTick(17);
            if (mankiLab.PresentationPreviewInstanceCount != 0)
                throw new InvalidOperationException("Manki presentation preview activated before its trigger tick.");
            mankiLab.SetTick(18);
            if (mankiLab.PresentationPreviewInstanceCount != 1 ||
                !mankiLab.PresentationPreviewInstances.Single().GetComponentsInChildren<ParticleSystem>(true).Any())
                throw new InvalidOperationException("Manki presentation preview did not resolve a particle VFX at its trigger tick.");
            mankiLab.SetTick(45);
            if (mankiLab.PresentationPreviewInstanceCount != 1)
                throw new InvalidOperationException("Manki presentation preview expired before the configured lifetime.");
            mankiLab.SetTick(46);
            if (mankiLab.PresentationPreviewInstanceCount != 0)
                throw new InvalidOperationException("Manki presentation preview remained active after the configured lifetime.");

            RefreshInspector(window);
            var presentationStartTick = root.Q<VisualElement>("inspector").Query<IntegerField>().ToList()
                .FirstOrDefault(field => field.label == "Start tick");
            if (presentationStartTick == null)
                throw new InvalidOperationException("Presentation operation does not expose an editable Start tick.");
            if (!windowWorkspace.ReplaceOperationTick("ground.F", 0,
                    mankiPresentationProjection.SourceOperationIndex, 19))
                throw new InvalidOperationException("Presentation operation could not be retimed through the workspace.");
            Refresh(window);
            var retimedPresentation = windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.F")
                .Timeline.Stages[0].Operations.OfType<EmitPresentationOperationSource>().Single();
            if (retimedPresentation.Tick != 19)
                throw new InvalidOperationException("Presentation retime did not update the source operation.");
            mankiLab.SetTick(18);
            if (mankiLab.PresentationPreviewInstanceCount != 0)
                throw new InvalidOperationException("Presentation preview remained at the old trigger tick after retiming.");
            mankiLab.SetTick(19);
            if (mankiLab.PresentationPreviewInstanceCount != 1)
                throw new InvalidOperationException("Presentation preview did not follow the retimed trigger tick.");
            windowWorkspace.Undo();
            Refresh(window);
            var restoredPresentation = windowWorkspace.Draft.Slots.First(slot => slot.Id == "ground.F")
                .Timeline.Stages[0].Operations.OfType<EmitPresentationOperationSource>().Single();
            mankiLab.SetTick(18);
            if (restoredPresentation.Tick != 18 || mankiLab.PresentationPreviewInstanceCount != 1)
                throw new InvalidOperationException("Presentation Undo did not restore source timing and visible timing.");

            var mankiCatalog = windowWorkspace.Catalog;
            var originalPresentationBinding = mankiCatalog.Presentations
                .First(binding => binding != null && binding.SemanticId == mankiPresentationOperation.PresentationId);
            var originalPresentationPrefab = originalPresentationBinding.Prefab;
            string addedPresentationId = "presentation.manki.frontend-self-test";
            if (!windowWorkspace.AddPresentationAsset(addedPresentationId, originalPresentationPrefab) ||
                !windowWorkspace.Draft.PresentationIds.Contains(addedPresentationId, StringComparer.Ordinal) ||
                !mankiCatalog.Presentations.Any(binding => binding != null && binding.SemanticId == addedPresentationId))
                throw new InvalidOperationException("Adding a package presentation asset did not update source and catalog together.");
            windowWorkspace.Undo();
            if (windowWorkspace.Draft.PresentationIds.Contains(addedPresentationId, StringComparer.Ordinal) ||
                mankiCatalog.Presentations.Any(binding => binding != null && binding.SemanticId == addedPresentationId))
                throw new InvalidOperationException("Presentation asset Undo did not restore source and catalog.");
            windowWorkspace.Redo();
            if (!windowWorkspace.Draft.PresentationIds.Contains(addedPresentationId, StringComparer.Ordinal) ||
                !mankiCatalog.Presentations.Any(binding => binding != null && binding.SemanticId == addedPresentationId))
                throw new InvalidOperationException("Presentation asset Redo did not restore source and catalog.");
            windowWorkspace.Undo();
            var replacementPresentationPrefab = mankiCatalog.Rig;
            if (replacementPresentationPrefab == null || replacementPresentationPrefab == originalPresentationPrefab)
                throw new InvalidOperationException("Manki presentation rebind regression fixture has no distinct prefab.");
            if (!windowWorkspace.ReplaceCatalogPresentation(
                    mankiPresentationOperation.PresentationId,
                    replacementPresentationPrefab) ||
                mankiCatalog.Presentations.First(binding => binding != null && binding.SemanticId == mankiPresentationOperation.PresentationId).Prefab != replacementPresentationPrefab)
                throw new InvalidOperationException("Presentation asset rebind did not update the package catalog.");
            windowWorkspace.Undo();
            if (mankiCatalog.Presentations.First(binding => binding != null && binding.SemanticId == mankiPresentationOperation.PresentationId).Prefab != originalPresentationPrefab)
                throw new InvalidOperationException("Presentation asset rebind Undo did not restore its prefab.");
            windowWorkspace.Redo();
            if (mankiCatalog.Presentations.First(binding => binding != null && binding.SemanticId == mankiPresentationOperation.PresentationId).Prefab != replacementPresentationPrefab)
                throw new InvalidOperationException("Presentation asset rebind Redo did not restore its prefab.");
            windowWorkspace.Undo();

            string renamedPresentationId = "presentation.manki.aerosol-inferno.renamed";
            bool renamedPresentation = (bool)typeof(AbilityLabWindow).GetMethod("ConfirmAndRenameSemanticId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { mankiPresentationOperation.PresentationId, renamedPresentationId, (Func<bool>)(() => true) })!;
            if (!renamedPresentation ||
                !windowWorkspace.Draft.PresentationIds.Contains(renamedPresentationId, StringComparer.Ordinal) ||
                !windowWorkspace.Draft.Slots.SelectMany(slot => slot.Timeline.Stages).SelectMany(stage => stage.Operations)
                    .OfType<EmitPresentationOperationSource>().Any(operation => operation.PresentationId == renamedPresentationId) ||
                !mankiCatalog.Presentations.Any(binding => binding != null && binding.SemanticId == renamedPresentationId))
                throw new InvalidOperationException("Presentation semantic-ID rename did not update source operation and catalog binding.");
            windowWorkspace.Undo();
            Refresh(window);

            if (!windowWorkspace.OpenPackage("Assets/CharacterPackages/manki"))
                throw new InvalidOperationException("Manki package could not be reloaded for fingerprint conflict testing.");
            Refresh(window);
            mankiCatalog = windowWorkspace.Catalog;
            originalPresentationBinding = mankiCatalog.Presentations
                .First(binding => binding != null && binding.SemanticId == mankiPresentationOperation.PresentationId);
            originalPresentationPrefab = originalPresentationBinding.Prefab;
            replacementPresentationPrefab = mankiCatalog.Rig;
            string fingerprintBefore = CharacterPackageAuthoringService.ComputeCatalogFingerprint(mankiCatalog);
            originalPresentationBinding.Prefab = replacementPresentationPrefab;
            string fingerprintAfter = CharacterPackageAuthoringService.ComputeCatalogFingerprint(mankiCatalog);
            EditorUtility.SetDirty(mankiCatalog);
            AssetDatabase.SaveAssets();
            if (fingerprintBefore == fingerprintAfter || windowWorkspace.SavePackage() ||
                !windowWorkspace.Diagnostics.Any(diagnostic => diagnostic.Code == "workspace.conflict"))
                throw new InvalidOperationException("External presentation-link mutation did not invalidate the catalog fingerprint.");
            originalPresentationBinding.Prefab = originalPresentationPrefab;
            EditorUtility.SetDirty(mankiCatalog);
            AssetDatabase.SaveAssets();
            windowWorkspace.ReloadPackage();
            Refresh(window);
            if (!windowWorkspace.OpenPackage("Assets/CharacterPackages/fightguy"))
                throw new InvalidOperationException("FightGuy package could not be restored after Manki presentation tests.");
            Refresh(window);
            var grabButton = root.Q<Button>("selected-grab");
            if (grabButton == null)
                throw new InvalidOperationException("Ground move selector does not offer Grab.");
            float originalReach = windowWorkspace.Draft.CaptureGeometry.Reach;
            InvokeButton(grabButton);
            var scenarioSlider = root.Q<SliderInt>("timeline-slider");
            var selectedScenario = lab.Scenario;
            if (selectedScenario == null || selectedScenario.Options.Action != "grab" ||
                selectedScenario.Options.LastFrame != 60 || !lab.IsScenarioPreview ||
                scenarioSlider == null || !scenarioSlider.enabledSelf ||
                scenarioOutcomes.text.Contains("No scenario run", StringComparison.Ordinal) ||
                scenarioAction.text != "Action: grab")
                throw new InvalidOperationException("Ground Grab did not produce an observable default Shared scenario.");
            scenarioSlider.value = 3;
            if (lab.ScenarioFrame != 3)
                throw new InvalidOperationException("Scenario timeline scrubbing did not seek the recorded Shared frame.");
            scenarioSlider.value = 0;
            var grabReach = root.Q<VisualElement>("inspector").Query<FloatField>().ToList()
                .FirstOrDefault(field => field.label == "Reach");
            if (grabReach == null || lab.ShowHitboxes ||
                root.Q<VisualElement>("move-timeline").style.display == DisplayStyle.None)
                throw new InvalidOperationException("Grab did not retain its numeric geometry inspector and recorded scenario timeline.");
            float editedReach = originalReach + 0.05f;
            grabReach.value = editedReach;
            if (Mathf.Abs(windowWorkspace.Draft.CaptureGeometry.Reach - editedReach) > 0.0001f ||
                windowWorkspace.LiveDraftPackage == null ||
                Mathf.Abs(windowWorkspace.LiveDraftPackage.Definition.CaptureGeometry.Reach - editedReach) > 0.0001f)
                throw new InvalidOperationException("Grab reach edit did not update the authoritative draft preview.");
            if (lab.Scenario != null)
                throw new InvalidOperationException("Editing Grab geometry left an obsolete recorded scenario available.");
            windowWorkspace.Undo();
            Refresh(window);
            if (Mathf.Abs(windowWorkspace.Draft.CaptureGeometry.Reach - originalReach) > 0.0001f)
                throw new InvalidOperationException("Grab edit did not participate in workspace undo.");
            InvokeButton(root.Q<Button>("selected-ground-1"));
            if (!lab.ShowHitboxes ||
                root.Q<VisualElement>("move-timeline").style.display == DisplayStyle.None)
                throw new InvalidOperationException("Returning to a move did not restore its hitboxes and timeline.");
            scenarioDistance.value = 2.5f;
            scenarioFacing.value = 90f;
            scenarioDamage.value = 7;
            scenarioOpponent.value = "Shield";
            scenarioHorizon.value = 12;
            InvokeButton(scenarioRun);
            if (lab.Scenario == null || lab.Scenario.Options.Action != "ground.1" ||
                lab.Scenario.Options.Distance != 2.5f || lab.Scenario.Options.RelativeFacingDegrees != 90f ||
                lab.Scenario.Options.OpponentDamage != 7 ||
                lab.Scenario.Options.OpponentBehavior != AbilityLabOpponentBehavior.Shield ||
                lab.Scenario.Options.LastFrame != 12)
                throw new InvalidOperationException("Scenario controls did not record the selected action and configured opponent inputs.");
            InvokeButton(scenarioExit);
            if (lab.IsScenarioPreview || windowWorkspace.IsDirty == false)
                throw new InvalidOperationException("Exit to authoring did not restore the draft workspace after scenario playback.");

            Refresh(window);

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
            var hitboxProjection = timeline.Projection.Stages
                .SelectMany(stage => stage.Operations)
                .First(operation => operation.Source is SpawnHitboxOperationSource);
            typeof(AbilityLabWindow).GetMethod("SelectOperation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { hitboxProjection });
            var operationFoldouts = root.Q<VisualElement>("inspector").Query<Foldout>().ToList()
                .Where(foldout => foldout.ClassListContains("timeline-operation"))
                .ToList();
            int projectedOperationCount = timeline.Projection.Stages.Sum(stage => stage.Operations.Count);
            if (operationFoldouts.Count != projectedOperationCount ||
                operationFoldouts.Count(foldout => foldout.value) != 1)
                throw new InvalidOperationException("Timeline operation events are not always visible with only the selected event expanded.");
            var startBoneField = root.Q<VisualElement>("inspector").Query<PopupField<string>>().ToList()
                .FirstOrDefault(field => field.label == "Start bone");
            if (startBoneField == null ||
                !startBoneField.choices.Contains("bone.left-hand", StringComparer.Ordinal) ||
                startBoneField.choices.Contains("mixamorig:LeftHand", StringComparer.Ordinal))
                throw new InvalidOperationException("Hitbox bone selector does not expose authoring bone IDs.");
            startBoneField.value = "bone.left-hand";
            var editedBone = (SpawnHitboxOperationSource)windowWorkspace.Draft.Slots
                .First(slot => slot.Id == "ground.1").Timeline.Stages[hitboxProjection.SourceStageIndex]
                .Operations[hitboxProjection.SourceOperationIndex];
            if (editedBone.Hitbox.StartBoneId != "bone.left-hand")
                throw new InvalidOperationException("Selecting a declared hurtbox bone did not update the source hitbox.");
            windowWorkspace.Undo();
            Refresh(window);

            var startTickField = root.Q<VisualElement>("inspector").Query<IntegerField>().ToList()
                .FirstOrDefault(field => field.label == "Start tick");
            var startTickBefore = ((SpawnHitboxOperationSource)hitboxProjection.Source).Tick;
            var startTickAfter = startTickBefore > 0 ? startTickBefore - 1 : startTickBefore + 1;
            if (startTickField == null || startTickAfter == startTickBefore)
                throw new InvalidOperationException("Selected hitbox does not expose an editable start tick field.");
            startTickField.value = startTickAfter;
            var editedStart = (SpawnHitboxOperationSource)windowWorkspace.Draft.Slots
                .First(slot => slot.Id == "ground.1").Timeline.Stages[hitboxProjection.SourceStageIndex]
                .Operations[hitboxProjection.SourceOperationIndex];
            if (editedStart.Tick != startTickAfter)
                throw new InvalidOperationException("Hitbox start tick field did not update the source timeline.");
            windowWorkspace.Undo();
            Refresh(window);
            var catalog = windowWorkspace.Catalog;
            var moveFields = root.Q<VisualElement>("assets-move-bindings").Query<ObjectField>().ToList();
            var moveRowsBySemanticId = moveFields
                .GroupBy(field => (string)field.parent.userData, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            if (moveRowsBySemanticId.Count < 2 || moveRowsBySemanticId.Count > moveFields.Count ||
                moveFields.Any(field => !(field.parent?.userData is string) || string.IsNullOrEmpty((string)field.parent.userData)))
                throw new InvalidOperationException("Move animation ObjectFields do not have stable semantic binding keys.");
            var beforeMoveClips = catalog.Bindings
                .Where(binding => binding != null)
                .ToDictionary(binding => binding.SemanticId, binding => binding.Clip, StringComparer.Ordinal);
            var firstMove = moveRowsBySemanticId.First();
            var replacementMove = moveRowsBySemanticId.Values
                .Select(field => field.value as AnimationClip)
                .FirstOrDefault(clip => clip != null && clip != firstMove.Value.value);
            if (replacementMove == null)
                throw new InvalidOperationException("Move animation regression fixture has no distinct replacement clip.");
            firstMove.Value.SendEvent(ChangeEvent<AnimationClip>.GetPooled(firstMove.Value.value as AnimationClip, replacementMove));
            if (beforeMoveClips.Any(pair =>
            {
                var current = catalog.Bindings.First(binding => binding != null && binding.SemanticId == pair.Key);
                return pair.Key != firstMove.Key && current.Clip != pair.Value;
            }))
                throw new InvalidOperationException("Dragging one Move Animations field changed every move binding.");
            windowWorkspace.ReplaceCatalogBinding(firstMove.Key, beforeMoveClips[firstMove.Key], ExtrapolationMode.None);
            typeof(AbilityLabWindow).GetMethod("RefreshAll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
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
            var moveBindingClipsBefore = catalog.Bindings
                .Where(binding => binding != null)
                .ToDictionary(binding => binding.SemanticId, binding => binding.Clip, StringComparer.Ordinal);
            var replacementBinding = catalog.Bindings.First(binding => binding != null && binding.SemanticId != catalogBindingId);
            if (!windowWorkspace.ReplaceCatalogBinding(catalogBindingId, replacementBinding.Clip, ExtrapolationMode.Continuous) ||
                catalog.Bindings.First(binding => binding != null && binding.SemanticId == catalogBindingId).Clip != replacementBinding.Clip ||
                catalog.Bindings.First(binding => binding != null && binding.SemanticId == catalogBindingId).Extrapolation != ExtrapolationMode.Continuous ||
                moveBindingClipsBefore.Any(pair =>
                {
                    var current = catalog.Bindings.First(binding => binding != null && binding.SemanticId == pair.Key);
                    return current.Clip != pair.Value && pair.Key != catalogBindingId;
                }))
                throw new InvalidOperationException("Catalog clip replacement mutated unrelated move-animation bindings.");
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
            string expectedDuration = $"Duration {timeline.Projection.DurationTicks} ticks · {timeline.Projection.DurationTicks / (float)AbilityLab.TickRate:0.00}s";
            if (root.Q<Label>("timeline-duration").text != expectedDuration)
                throw new InvalidOperationException("Timeline duration does not report total authored runtime.");
            int operationCount = timeline.Projection.Stages.Sum(stage => stage.Operations.Count);
            float minimumTimelineHeight = 18f + operationCount * 20f;
            if (timeline.resolvedStyle.height < Math.Max(84f, minimumTimelineHeight))
                throw new InvalidOperationException("Timeline content height clips projected operation rows.");
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


            Debug.Log("[AbilityLabFrontendSelfTest] Passed stable preview roots, missing-model recovery, canonical controls, package preview seam, and source-edit boundary checks.");
        }
        finally
        {
            Selection.activeObject = null;
            if (window != null)
            {
                window.rootVisualElement?.Clear();
                window.Close();
            }
            if (labObject != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(labObject);
                else
                    UnityEngine.Object.DestroyImmediate(labObject);
            }
        }
    }
    private static void CleanupOrphanedFixture()
    {
        foreach (var lab in Resources.FindObjectsOfTypeAll<AbilityLab>())
        {
            if (lab == null || lab.gameObject.name != "AbilityLabFrontendSelfTest" ||
                EditorUtility.IsPersistent(lab.gameObject))
                continue;
            UnityEngine.Object.DestroyImmediate(lab.gameObject);
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
