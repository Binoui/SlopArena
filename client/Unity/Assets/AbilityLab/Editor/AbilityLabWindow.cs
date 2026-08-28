using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Client.Tools;
using SlopArena.Client.Animation;
using SlopArena.Shared;

namespace SlopArena.EditorTools;

public sealed class AbilityLabWindow : EditorWindow
{
    private sealed class PackageOption
    {
        public PackageOption(string packageId, string displayName, IReadOnlyList<CharacterDiagnostic> diagnostics)
        {
            PackageId = packageId;
            DisplayName = displayName;
            Diagnostics = diagnostics;
        }

        public string PackageId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    }

    private sealed class AnimationChoice
    {
        public AnimationChoice(string semanticId, string label)
        {
            SemanticId = semanticId;
            Label = label;
        }

        public string SemanticId { get; }
        public string Label { get; }
    }

    private AbilityLab? _lab;
    private AbilityLabPackageWorkspace _workspace = new();
    private AbilityLabPackagePreviewResult? _preview;
    private readonly List<PackageOption> _packages = new();
    private readonly Dictionary<string, PackageOption> _packagesByDisplay = new(StringComparer.Ordinal);
    private VisualElement _root = null!;
    private DropdownField _packageSelector = null!;
    private Label _compatibilityAuthority = null!;
    private DropdownField _legacySelector = null!;
    private Button _legacyLoad = null!;
    private Toggle _compatibilityAirborne = null!;
    private DropdownField _compatibilitySlotSelector = null!;
    private DropdownField _compatibilityStageSelector = null!;
    private Button _compatibilityPlay = null!;
    private Button _compatibilityStepBack = null!;
    private Button _compatibilityStepForward = null!;
    private SliderInt _compatibilitySlider = null!;
    private Label _compatibilityTick = null!;
    private Label _compatibilityDuration = null!;
    private Toggle _compatibilityShowHurtboxes = null!;
    private Toggle _compatibilityShowHitboxes = null!;
    private Toggle _compatibilityShowBakedBones = null!;
    private Toggle _compatibilityShowDummy = null!;
    private readonly List<CharacterClass> _compatibilityCharacters = new();
    private string _activePage = "moves-page";
    private Label _packageStatus = null!;
    private Button _packageStatusToggle = null!;
    private ScrollView _diagnosticsPanel = null!;
    private Label _rigSetupState = null!;
    private VisualElement _moveSelector = null!;
    private VisualElement _groundAirSelector = null!;
    private Button _groundMovesButton = null!;
    private Button _airMovesButton = null!;
    private Label _previewStatus = null!;
    private Label _sceneViewGuidance = null!;
    private VisualElement _inspector = null!;
    private Label _timelineTick = null!;
    private Button _timelinePlay = null!;
    private SliderInt _timelineSlider = null!;
    private Label _timelineDuration = null!;
    private DropdownField _stageSelector = null!;
    private AbilityLabTimelineElement _timelineTrack = null!;
    private AbilityLabTimelineProjection? _timelineProjection;
    private Slider _timelineZoom = null!;
    private ScrollView _timelineScroll = null!;
    private string _timelineProjectionSlotId = "";
    private CharacterAuthoringDocument? _timelineProjectionDraft;
    private AbilityLabOperationProjection? _selectedOperation;
    private readonly Dictionary<string, VisualElement> _pages = new(StringComparer.Ordinal);
    private bool _airborneSelector;
    private VisualElement _characterGeneral = null!;
    private VisualElement _characterMovement = null!;
    private VisualElement _movementGround = null!;
    private VisualElement _movementAir = null!;
    private VisualElement _movementJump = null!;
    private VisualElement _movementFalling = null!;
    private VisualElement _characterPresentation = null!;
    private VisualElement _characterHurtboxes = null!;
    private Foldout _characterHurtboxCapsules = null!;
    private Foldout _characterHurtboxBones = null!;
    private Label _characterUnavailable = null!;
    private VisualElement _assetsPage = null!;
    private ObjectField _assetsRigField = null!;
    private Label _assetsRigStatus = null!;
    private VisualElement _assetsSkeleton = null!;
    private VisualElement _assetsLocomotionBindings = null!;
    private VisualElement _assetsHitReactionBindings = null!;
    private VisualElement _assetsMoveBindings = null!;
    private VisualElement _assetsValidation = null!;
    private VisualElement _advancedPackagePaths = null!;
    private Label _advancedSourcePath = null!;
    private Label _advancedCookedPath = null!;
    private VisualElement _advancedHashes = null!;
    private VisualElement _advancedRawIds = null!;
    private VisualElement _advancedDiagnostics = null!;
    private VisualElement _advancedProvenance = null!;
    private VisualElement _advancedSchemaProfile = null!;
    private TextField _advancedRenameOld = null!;
    private TextField _advancedRenameNew = null!;
    private Label _advancedRenameStatus = null!;
    private Button _advancedRenameConfirm = null!;
    private Button _advancedMigrateAuthoring = null!;
    private Button _advancedMigrateCatalog = null!;
    private Label _advancedMigrationStatus = null!;
    private bool _sceneRadiusEditing;
    private float _sceneRadiusPending;
    private int _sceneRadiusStageIndex = -1;
    private int _sceneRadiusOperationIndex = -1;
    private CharacterPackageInspectionResult? _inspection;
    private bool _updatingControls;

    [MenuItem("Tools/SlopArena/Ability Lab")]
    public static void Open() => GetWindow<AbilityLabWindow>("Ability Lab");

    private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
    private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    public void CreateGUI()
    {
        _root = rootVisualElement;
        _root.Clear();
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/AbilityLab/Editor/AbilityLabWindow.uxml");
        if (tree == null)
        {
            _root.Add(new Label("Ability Lab UXML is missing."));
            return;
        }
        tree.CloneTree(_root);
        var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/AbilityLab/Editor/AbilityLabWindow.uss");
        if (stylesheet != null) _root.styleSheets.Add(stylesheet);

        BindElements();
        DiscoverPackages();
        BindTabs();
        BindToolbar();
        BindMovesPage();
        BindCharacterPage();
        BindAssetsPage();
        BindAdvancedPage();
        if (_packages.Any(option => option.PackageId == "fightguy"))
            OpenPackage("fightguy");
        else
            RefreshAll();
        _workspace.StatusChanged -= RefreshAll;
        _workspace.StatusChanged += RefreshAll;
    }

    private void Update()
    {
        if (_activePage == "compatibility-page")
            RefreshCompatibilityControls();
        if (_lab != null && _lab.Playing)
        {
            UpdateTimelineControls();
            Repaint();
        }
    }

    private void BindElements()
    {
        _packageSelector = Required<DropdownField>("package-selector");
        _packageStatus = Required<Label>("package-status");
        _packageStatusToggle = Required<Button>("package-status-toggle");
        _diagnosticsPanel = Required<ScrollView>("diagnostics-panel");
        _rigSetupState = Required<Label>("rig-setup-state");
        _moveSelector = Required<VisualElement>("move-selector");
        _groundAirSelector = Required<VisualElement>("ground-air-selector");
        _groundMovesButton = Required<Button>("ground-moves-button");
        _airMovesButton = Required<Button>("air-moves-button");
        _previewStatus = Required<Label>("preview-status");
        _sceneViewGuidance = Required<Label>("scene-view-guidance");
        _inspector = Required<VisualElement>("inspector");
        _timelineTick = Required<Label>("timeline-tick");
        _timelinePlay = Required<Button>("timeline-play");
        _timelineSlider = Required<SliderInt>("timeline-slider");
        _timelineDuration = Required<Label>("timeline-duration");
        _stageSelector = Required<DropdownField>("stage-selector");
        _compatibilityAuthority = Required<Label>("compatibility-authority");
        _timelineZoom = Required<Slider>("timeline-zoom");
        _timelineScroll = Required<ScrollView>("timeline-scroll");
        _legacySelector = Required<DropdownField>("legacy-selector");
        _legacyLoad = Required<Button>("legacy-load");
        _compatibilityAirborne = Required<Toggle>("compatibility-airborne");
        _compatibilitySlotSelector = Required<DropdownField>("compatibility-slot-selector");
        _compatibilityStageSelector = Required<DropdownField>("compatibility-stage-selector");
        _compatibilityPlay = Required<Button>("compatibility-play");
        _compatibilityStepBack = Required<Button>("compatibility-step-back");
        _compatibilityStepForward = Required<Button>("compatibility-step-forward");
        _compatibilitySlider = Required<SliderInt>("compatibility-slider");
        _compatibilityTick = Required<Label>("compatibility-tick");
        _compatibilityDuration = Required<Label>("compatibility-duration");
        _compatibilityShowHurtboxes = Required<Toggle>("compatibility-show-hurtboxes");
        _compatibilityShowHitboxes = Required<Toggle>("compatibility-show-hitboxes");
        _compatibilityShowBakedBones = Required<Toggle>("compatibility-show-baked-bones");
        _compatibilityShowDummy = Required<Toggle>("compatibility-show-dummy");
        var timelinePlaceholder = Required<VisualElement>("timeline-track");
        _timelineTrack = new AbilityLabTimelineElement { name = "timeline-track" };
        _timelineTrack.AddToClassList("timeline-track");
        int timelineIndex = timelinePlaceholder.parent.IndexOf(timelinePlaceholder);
        timelinePlaceholder.parent.Insert(timelineIndex, _timelineTrack);
        timelinePlaceholder.RemoveFromHierarchy();
        _timelineTrack.OperationSelected += SelectOperation;
        _timelineTrack.DragCompleted += CompleteTimelineDrag;
        _timelineTrack.TickScrubbed += ApplyCumulativeTick;
        _root.focusable = true;
        _root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
        foreach (string page in new[] { "moves-page", "character-page", "assets-page", "compatibility-page", "advanced-page" })
            _pages[page] = Required<VisualElement>(page);
        _characterGeneral = Required<VisualElement>("character-general");
        _characterMovement = Required<VisualElement>("character-movement");
        _movementGround = Required<VisualElement>("movement-ground");
        _assetsPage = Required<VisualElement>("assets-page");
        _assetsRigField = Required<ObjectField>("assets-rig-field");
        _assetsRigStatus = Required<Label>("assets-rig-status");
        _assetsSkeleton = Required<VisualElement>("assets-skeleton");
        _assetsLocomotionBindings = Required<VisualElement>("assets-locomotion-bindings");
        _assetsHitReactionBindings = Required<VisualElement>("assets-hit-reaction-bindings");
        _assetsMoveBindings = Required<VisualElement>("assets-move-bindings");
        _assetsValidation = Required<VisualElement>("assets-validation");
        _advancedPackagePaths = Required<VisualElement>("advanced-package-paths");
        _advancedSourcePath = Required<Label>("advanced-source-path");
        _advancedCookedPath = Required<Label>("advanced-cooked-path");
        _advancedHashes = Required<VisualElement>("advanced-hashes");
        _advancedRawIds = Required<VisualElement>("advanced-raw-ids");
        _advancedDiagnostics = Required<VisualElement>("advanced-diagnostics");
        _advancedProvenance = Required<VisualElement>("advanced-provenance");
        _advancedSchemaProfile = Required<VisualElement>("advanced-schema-profile");
        _advancedRenameOld = Required<TextField>("advanced-rename-old");
        _advancedRenameNew = Required<TextField>("advanced-rename-new");
        _advancedRenameConfirm = Required<Button>("advanced-rename-confirm");
        _advancedRenameStatus = Required<Label>("advanced-rename-status");
        _advancedMigrateAuthoring = Required<Button>("advanced-migrate-authoring");
        _advancedMigrateCatalog = Required<Button>("advanced-migrate-catalog");
        _advancedMigrationStatus = Required<Label>("advanced-migration-status");
        _movementAir = Required<VisualElement>("movement-air");
        _movementJump = Required<VisualElement>("movement-jump");
        _movementFalling = Required<VisualElement>("movement-falling");
        _characterPresentation = Required<VisualElement>("character-presentation");
        _characterHurtboxes = Required<VisualElement>("character-hurtboxes");
        _characterHurtboxCapsules = Required<Foldout>("character-hurtbox-capsules");
        _characterHurtboxBones = Required<Foldout>("character-hurtbox-bones");
        _characterUnavailable = Required<Label>("character-unavailable");
    }

    private T Required<T>(string name) where T : VisualElement
        => _root.Q<T>(name) ?? throw new InvalidOperationException($"Ability Lab UXML control '{name}' is missing.");

    private void BindTabs()
    {
        BindTab("tab-moves", "moves-page");
        BindTab("tab-character", "character-page");
        BindTab("tab-assets", "assets-page");
        BindTab("tab-compatibility", "compatibility-page");
        BindTab("tab-advanced", "advanced-page");
        SelectTab("moves-page");
    }

    private void BindTab(string tabName, string pageName)
        => Required<Button>(tabName).clicked += () => SelectTab(pageName);

    private void SelectTab(string pageName)
    {
        bool compatibility = pageName == "compatibility-page";
        bool leavingCompatibility = _activePage == "compatibility-page" && !compatibility;
        _activePage = pageName;
        foreach (var page in _pages)
            page.Value.style.display = page.Key == pageName ? DisplayStyle.Flex : DisplayStyle.None;
        _root.Q<Label>("compatibility-banner")!.style.display = compatibility ? DisplayStyle.Flex : DisplayStyle.None;
        SetPackageControlsVisible(!compatibility);
        if (leavingCompatibility)
            RefreshPreview();
        RefreshCompatibilityControls();
    }

    private void SetPackageControlsVisible(bool visible)
    {
        DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        Required<Label>("package-label").style.display = display;
        _packageSelector.style.display = display;
        _packageStatusToggle.style.display = display;
        _packageStatus.style.display = display;
        Required<Button>("toolbar-undo").style.display = display;
        Required<Button>("toolbar-redo").style.display = display;
        Required<Button>("toolbar-save").style.display =
            visible && _workspace.HasPackage ? DisplayStyle.Flex : DisplayStyle.None;
        _diagnosticsPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void BindToolbar()
    {
        _packageSelector.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || !_packagesByDisplay.TryGetValue(evt.newValue, out var option)) return;
            OpenPackage(option.PackageId);
        });
        _packageStatusToggle.clicked += () =>
            _diagnosticsPanel.style.display = _diagnosticsPanel.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
        Required<Button>("toolbar-undo").clicked += () => { _workspace.Undo(); RefreshAll(); };
        Required<Button>("toolbar-redo").clicked += () => { _workspace.Redo(); RefreshAll(); };
        Required<Button>("toolbar-save").clicked += () => { _workspace.SavePackage(); RefreshAll(); };
        Required<Button>("create-lab-rig").clicked += CreateOrSelectLabRig;
        DiscoverCompatibilityCharacters();
        _legacyLoad.clicked += () =>
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Play Mode required", "Legacy compatibility preview is read-only and Play Mode-only.", "OK");
                return;
            }
            if (_lab != null && _compatibilityCharacters.Count > 0 &&
                Enum.TryParse(_legacySelector.value, out CharacterClass selector))
            {
                _lab.LoadCharacter(selector);
                RefreshCompatibilityControls();
                SceneView.RepaintAll();
            }
        };
        _compatibilityAirborne.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _lab != null && IsLoadedLegacy())
            {
                _lab.SetAirborne(evt.newValue);
                RefreshCompatibilityControls();
                SceneView.RepaintAll();
            }
        });
        _compatibilitySlotSelector.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _lab != null && IsLoadedLegacy())
            {
                int labelIndex = Array.IndexOf(AbilityLab.SlotNames, evt.newValue);
                if (labelIndex >= 0 && labelIndex < AbilityLab.SlotIndices.Length)
                {
                    _lab.SetSlot(AbilityLab.SlotIndices[labelIndex]);
                    RefreshCompatibilityControls();
                    SceneView.RepaintAll();
                }
            }
        });
        _compatibilityStageSelector.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _lab != null && IsLoadedLegacy() &&
                int.TryParse(evt.newValue.Replace("Stage ", ""), out int stage))
            {
                _lab.SetStage(stage - 1);
                RefreshCompatibilityControls();
                SceneView.RepaintAll();
            }
        });
        _compatibilityPlay.clicked += () =>
        {
            if (_lab == null || !IsLoadedLegacy()) return;
            _lab.Playing = !_lab.Playing;
            RefreshCompatibilityControls();
        };
        _compatibilityStepBack.clicked += () => SetCompatibilityTickDelta(-1);
        _compatibilityStepForward.clicked += () => SetCompatibilityTickDelta(1);
        _compatibilitySlider.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _lab != null && IsLoadedLegacy())
            {
                _lab.SetTick((ushort)Mathf.Clamp(evt.newValue, 0, ushort.MaxValue));
                RefreshCompatibilityControls();
                SceneView.RepaintAll();
            }
        });
        _compatibilityShowHurtboxes.RegisterValueChangedCallback(evt =>
        {
            if (_lab != null) _lab.ShowHurtboxes = evt.newValue;
            SceneView.RepaintAll();
        });
        _compatibilityShowHitboxes.RegisterValueChangedCallback(evt =>
        {
            if (_lab != null) _lab.ShowHitboxes = evt.newValue;
            SceneView.RepaintAll();
        });
        _compatibilityShowBakedBones.RegisterValueChangedCallback(evt =>
        {
            if (_lab != null) _lab.ShowBakedBones = evt.newValue;
            SceneView.RepaintAll();
        });
        _compatibilityShowDummy.RegisterValueChangedCallback(evt =>
        {
            if (_lab != null) _lab.ShowDummy = evt.newValue;
            SceneView.RepaintAll();
        });
    }

    private void BindMovesPage()
    {
        _groundMovesButton.clicked += () =>
        {
            if (_updatingControls) return;
            _airborneSelector = false;
            BuildMoveButtons(false);
        };
        _airMovesButton.clicked += () =>
        {
            if (_updatingControls) return;
            _airborneSelector = true;
            BuildMoveButtons(true);
        };
        _timelinePlay.clicked += () =>
        {
            if (_lab == null || !_lab.IsPackagePreview) return;
            _lab.Playing = !_lab.Playing;
            UpdateTimelineControls();
        };
        Required<Button>("timeline-step-back").clicked += () => SetTickDelta(-1);
        Required<Button>("timeline-step-forward").clicked += () => SetTickDelta(1);
        _timelineSlider.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls) ApplyCumulativeTick(evt.newValue);
        });
        _timelineZoom.RegisterValueChangedCallback(evt => SetTimelineZoom(evt.newValue));
        SetTimelineZoom(_timelineZoom.value);
        _stageSelector.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && int.TryParse(evt.newValue.Replace("Stage ", ""), out var stage))
            {
                _lab?.SetStage(stage - 1);
                UpdateTimelineControls();
                RefreshInspector();
            }
        });
    }
    private void BindCharacterPage()
    {
        BindDelayedText("character-display-name", value => CommitGeneral(current => current with { DisplayName = value }));
        BindDelayedFloat("character-weight", value => CommitGeneral(current => current with { Weight = value }));
        BindDelayedFloat("character-capsule-radius", value => CommitGeneral(current => current with { CapsuleRadius = value }));
        BindDelayedFloat("character-capsule-height", value => CommitGeneral(current => current with { CapsuleHeight = value }));
        BindDelayedFloat("character-hip-height", value => CommitGeneral(current => current with { HipHeight = value }));
        BindDelayedFloat("character-hurtbox-radius", value => CommitGeneral(current => current with { HurtboxRadius = value }));

        BindDelayedFloat("movement-run-speed", value => CommitMovement(current => current with { RunSpeed = value }));
        BindDelayedFloat("movement-run-acceleration-a", value => CommitMovement(current => current with { RunAccelerationA = value }));
        BindDelayedFloat("movement-run-acceleration-b", value => CommitMovement(current => current with { RunAccelerationB = value }));
        BindDelayedFloat("movement-dash-speed", value => CommitMovement(current => current with { DashSpeed = value }));
        BindDelayedFloat("movement-ground-friction", value => CommitMovement(current => current with { GroundFriction = value }));
        BindDelayedInteger("movement-dash-duration-ticks", value => CommitMovement(current => current with { DashDurationTicks = ClampUShort(value) }));
        BindDelayedInteger("movement-dash-cooldown-ticks", value => CommitMovement(current => current with { DashCooldownTicks = ClampUShort(value) }));
        BindDelayedInteger("movement-rush-ticks", value => CommitMovement(current => current with { RushTicks = ClampUShort(value) }));

        BindDelayedFloat("movement-air-speed-max", value => CommitMovement(current => current with { AirSpeedMax = value }));
        BindDelayedFloat("movement-air-acceleration-stick", value => CommitMovement(current => current with { AirAccelStick = value }));
        BindDelayedFloat("movement-air-acceleration-base", value => CommitMovement(current => current with { AirAccelBase = value }));
        BindDelayedFloat("movement-air-friction", value => CommitMovement(current => current with { AirFriction = value }));

        BindDelayedFloat("movement-jump-force", value => CommitMovement(current => current with { JumpForce = value }));
        BindDelayedFloat("movement-short-hop-force", value => CommitMovement(current => current with { ShortHopForce = value }));
        BindDelayedFloat("movement-air-jump-vertical-multiplier", value => CommitMovement(current => current with { AirJumpVMultiplier = value }));
        BindDelayedFloat("movement-air-jump-horizontal-multiplier", value => CommitMovement(current => current with { AirJumpHMultiplier = value }));
        BindDelayedInteger("movement-max-jumps", value => CommitMovement(current => current with { MaxJumps = ClampByte(value) }));
        BindDelayedInteger("movement-jump-squat-ticks", value => CommitMovement(current => current with { JumpSquatTicks = ClampUShort(value) }));

        BindDelayedFloat("movement-gravity", value => CommitMovement(current => current with { Gravity = value }));
        BindDelayedFloat("movement-air-float-gravity", value => CommitMovement(current => current with { AirFloatGravity = value }));
        BindDelayedFloat("movement-max-fall-speed", value => CommitMovement(current => current with { MaxFallSpeed = value }));
        BindDelayedFloat("movement-fast-fall-speed", value => CommitMovement(current => current with { FastFallSpeed = value }));
        BindDelayedInteger("movement-float-window-ticks", value => CommitMovement(current => current with { FloatWindowTicks = ClampUShort(value) }));

        BindPresentationSelector("presentation-idle", (current, value) => current with { Idle = value });
        BindPresentationSelector("presentation-run", (current, value) => current with { Run = value });
        BindPresentationSelector("presentation-dash", (current, value) => current with { Dash = value });
        BindPresentationSelector("presentation-jump", (current, value) => current with { Jump = value });
        BindPresentationSelector("presentation-fall", (current, value) => current with { Fall = value });
        BindPresentationSelector("presentation-hit-small", (current, value) => current with { HitSmall = value });
        BindPresentationSelector("presentation-hit-medium", (current, value) => current with { HitMedium = value });
        BindPresentationSelector("presentation-hit-hard", (current, value) => current with { HitHard = value });
        BindDelayedFloat("presentation-land-start-offset-seconds", value => CommitPresentation(current => current with { LandStartOffsetSeconds = value }));
        BindDelayedText("presentation-model-resource-path", value => CommitPresentation(current => current with { ModelResourcePath = value }));
        BindDelayedFloat("presentation-visual-scale", value => CommitPresentation(current => current with { VisualScale = value }));
        BindDelayedFloat("presentation-hurtbox-bone-scale", value => CommitPresentation(current => current with { HurtboxBoneScale = value }));
        BindDelayedFloat("presentation-model-y-offset", value => CommitPresentation(current => current with { ModelYOffset = value }));
        BindDelayedFloat("presentation-model-sole-offset", value => CommitPresentation(current => current with { ModelSoleOffset = value }));
        Required<Toggle>("presentation-auto-model-y-offset").RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _workspace.HasPackage)
                CommitPresentation(current => current with { AutoModelYOffset = evt.newValue });
        });
    }
    private void BindAssetsPage()
    {
        _assetsRigField.objectType = typeof(GameObject);
        _assetsRigField.allowSceneObjects = false;
        _assetsRigField.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || !_workspace.HasPackage) return;
            _workspace.ReplaceCatalogRig(evt.newValue as GameObject);
            RefreshAll();
        });
    }

    private void BindAdvancedPage()
    {
        _advancedRenameConfirm.clicked += () =>
            ConfirmAndRenameSemanticId(
                _advancedRenameOld.value,
                _advancedRenameNew.value,
                () => EditorUtility.DisplayDialog(
                    "Rename semantic ID",
                    $"Rename '{_advancedRenameOld.value}' to '{_advancedRenameNew.value}' across the source document and asset catalog?",
                    "Rename",
                    "Cancel"));
        _advancedMigrateAuthoring.clicked += () =>
            _advancedMigrationStatus.text = "Current; no migration required";
        _advancedMigrateCatalog.clicked += () =>
            _advancedMigrationStatus.text = "Current; no migration required";
    }

    internal bool ConfirmAndRenameSemanticId(string oldId, string newId, Func<bool> confirm)
    {
        if (_updatingControls || !_workspace.HasPackage || confirm == null || !confirm()) return false;
        bool renamed = _workspace.RenameSemanticId(oldId, newId);
        _advancedRenameStatus.text = renamed ? "Renamed; save and cook to publish the change." : "Rename rejected; see diagnostics.";
        RefreshAll();
        return renamed;
    }

    private void BindDelayedText(string name, Action<string> commit)
    {
        var field = Required<TextField>(name);
        field.isDelayed = true;
        field.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _workspace.HasPackage) commit(evt.newValue);
        });
    }

    private void BindDelayedFloat(string name, Action<float> commit)
    {
        var field = Required<FloatField>(name);
        field.isDelayed = true;
        field.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _workspace.HasPackage) commit(evt.newValue);
        });
    }

    private void BindDelayedInteger(string name, Action<int> commit)
    {
        var field = Required<IntegerField>(name);
        field.isDelayed = true;
        field.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _workspace.HasPackage) commit(evt.newValue);
        });
    }

    private void BindPresentationSelector(string name,
        Func<CharacterPresentationSource, string, CharacterPresentationSource> write)
    {
        var field = Required<DropdownField>(name);
        field.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || !_workspace.HasPackage) return;
            var choices = BuildAnimationChoices(PresentationSemanticIds());
            var choice = choices.FirstOrDefault(item => item.Label == evt.newValue);
            if (choice != null)
                CommitPresentation(current => write(current, choice.SemanticId));
        });
    }

    private IEnumerable<string> PresentationSemanticIds()
    {
        if (_preview?.AnimationCatalog?.Animations != null)
            foreach (var entry in _preview.AnimationCatalog.Animations)
                if (entry != null) yield return entry.SemanticId;
        if (_workspace.HasPackage)
        {
            var presentation = _workspace.Draft.Presentation;
            yield return presentation.Idle;
            yield return presentation.Run;
            yield return presentation.Dash;
            yield return presentation.Jump;
            yield return presentation.Fall;
            yield return presentation.HitSmall;
            yield return presentation.HitMedium;
            yield return presentation.HitHard;
        }
    }

    private void CommitGeneral(Func<CharacterAuthoringDocument, CharacterAuthoringDocument> edit)
    {
        if (_updatingControls || !_workspace.HasPackage) return;
        var current = edit(_workspace.Draft);
        if (_workspace.ReplaceGeneral(current.DisplayName, current.Weight, current.CapsuleRadius, current.CapsuleHeight, current.HipHeight, current.HurtboxRadius))
            RefreshCharacterPage();
    }

    private void CommitMovement(Func<CharacterMovementSource, CharacterMovementSource> edit)
    {
        if (_updatingControls || !_workspace.HasPackage) return;
        if (_workspace.ReplaceMovement(edit(_workspace.Draft.Movement)))
            RefreshCharacterPage();
    }

    private void CommitPresentation(Func<CharacterPresentationSource, CharacterPresentationSource> edit)
    {
        if (_updatingControls || !_workspace.HasPackage) return;
        if (_workspace.ReplacePresentation(edit(_workspace.Draft.Presentation)))
            RefreshCharacterPage();
    }

    private void RefreshCharacterPage()
    {
        bool available = _workspace.HasPackage;
        _characterUnavailable.style.display = available ? DisplayStyle.None : DisplayStyle.Flex;
        _characterGeneral.SetEnabled(available);
        _characterMovement.SetEnabled(available);
        _characterPresentation.SetEnabled(available);
        _characterHurtboxes.SetEnabled(available);
        if (!available) return;

        var character = _workspace.Draft;
        SetText("character-display-name", character.DisplayName);
        SetFloat("character-weight", character.Weight);
        SetFloat("character-capsule-radius", character.CapsuleRadius);
        SetFloat("character-capsule-height", character.CapsuleHeight);
        SetFloat("character-hip-height", character.HipHeight);
        SetFloat("character-hurtbox-radius", character.HurtboxRadius);

        var movement = character.Movement;
        SetFloat("movement-run-speed", movement.RunSpeed);
        SetFloat("movement-run-acceleration-a", movement.RunAccelerationA);
        SetFloat("movement-run-acceleration-b", movement.RunAccelerationB);
        SetFloat("movement-dash-speed", movement.DashSpeed);
        SetFloat("movement-ground-friction", movement.GroundFriction);
        SetInteger("movement-dash-duration-ticks", movement.DashDurationTicks);
        SetInteger("movement-dash-cooldown-ticks", movement.DashCooldownTicks);
        SetInteger("movement-rush-ticks", movement.RushTicks);
        SetFloat("movement-air-speed-max", movement.AirSpeedMax);
        SetFloat("movement-air-acceleration-stick", movement.AirAccelStick);
        SetFloat("movement-air-acceleration-base", movement.AirAccelBase);
        SetFloat("movement-air-friction", movement.AirFriction);
        SetFloat("movement-jump-force", movement.JumpForce);
        SetFloat("movement-short-hop-force", movement.ShortHopForce);
        SetFloat("movement-air-jump-vertical-multiplier", movement.AirJumpVMultiplier);
        SetFloat("movement-air-jump-horizontal-multiplier", movement.AirJumpHMultiplier);
        SetInteger("movement-max-jumps", movement.MaxJumps);
        SetInteger("movement-jump-squat-ticks", movement.JumpSquatTicks);
        SetFloat("movement-gravity", movement.Gravity);
        SetFloat("movement-air-float-gravity", movement.AirFloatGravity);
        SetFloat("movement-max-fall-speed", movement.MaxFallSpeed);
        SetFloat("movement-fast-fall-speed", movement.FastFallSpeed);
        SetInteger("movement-float-window-ticks", movement.FloatWindowTicks);

        var presentation = character.Presentation;
        var animationChoices = BuildAnimationChoices(PresentationSemanticIds());
        SetAnimation("presentation-idle", presentation.Idle, animationChoices);
        SetAnimation("presentation-run", presentation.Run, animationChoices);
        SetAnimation("presentation-dash", presentation.Dash, animationChoices);
        SetAnimation("presentation-jump", presentation.Jump, animationChoices);
        SetAnimation("presentation-fall", presentation.Fall, animationChoices);
        SetAnimation("presentation-hit-small", presentation.HitSmall, animationChoices);
        SetAnimation("presentation-hit-medium", presentation.HitMedium, animationChoices);
        SetAnimation("presentation-hit-hard", presentation.HitHard, animationChoices);
        SetFloat("presentation-land-start-offset-seconds", presentation.LandStartOffsetSeconds);
        SetText("presentation-model-resource-path", presentation.ModelResourcePath);
        SetFloat("presentation-visual-scale", presentation.VisualScale);
        SetFloat("presentation-hurtbox-bone-scale", presentation.HurtboxBoneScale);
        SetFloat("presentation-model-y-offset", presentation.ModelYOffset);
        SetFloat("presentation-model-sole-offset", presentation.ModelSoleOffset);
        Required<Toggle>("presentation-auto-model-y-offset").SetValueWithoutNotify(presentation.AutoModelYOffset);
        RefreshHurtboxes(character);
    }

    private void RefreshHurtboxes(CharacterAuthoringDocument character)
    {
        _characterHurtboxCapsules.Clear();
        var capsules = character.HurtboxCapsules ?? Array.Empty<HurtboxCapsuleSource>();
        for (int index = 0; index < capsules.Count; index++)
        {
            var capsule = capsules[index];
            _characterHurtboxCapsules.Add(new Label(
                $"Capsule {index + 1} · Start ({Number(capsule.StartX)}, {Number(capsule.StartY)}, {Number(capsule.StartZ)}) · " +
                $"End ({Number(capsule.EndX)}, {Number(capsule.EndY)}, {Number(capsule.EndZ)}) · Radius {Number(capsule.Radius)}"));
        }
        if (capsules.Count == 0) _characterHurtboxCapsules.Add(new Label("No authored capsule hurtboxes."));

        _characterHurtboxBones.Clear();
        var bones = character.HurtboxBoneDefs ?? Array.Empty<HurtboxBoneSource>();
        for (int index = 0; index < bones.Count; index++)
        {
            var bone = bones[index];
            _characterHurtboxBones.Add(new Label(
                $"Bone {index + 1} · {bone.BoneId} · Offset ({Number(bone.OffsetX)}, {Number(bone.OffsetY)}, {Number(bone.OffsetZ)}) · Radius {Number(bone.Radius)}"));
        }
        if (bones.Count == 0) _characterHurtboxBones.Add(new Label("No authored bone hurtboxes."));
    }

    private void SetAnimation(string name, string semanticId, IReadOnlyList<AnimationChoice> choices)
    {
        var field = Required<DropdownField>(name);
        field.choices = choices.Select(choice => choice.Label).ToList();
        var selected = choices.FirstOrDefault(choice => choice.SemanticId == semanticId);
        field.SetValueWithoutNotify(selected?.Label ?? $"Unknown ({semanticId})");
    }

    private List<AnimationChoice> BuildAnimationChoices(IEnumerable<string> semanticIds)
    {
        var entries = (_preview?.AnimationCatalog?.Animations ?? Array.Empty<CharacterAnimationCatalog.AnimationEntry>())
            .Where(entry => entry != null && !string.IsNullOrEmpty(entry.SemanticId))
            .ToList();
        var ids = entries.Select(entry => entry.SemanticId)
            .Concat(semanticIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var bases = ids.ToDictionary(id => id, FriendlyAnimationLabel, StringComparer.Ordinal);
        var duplicateBases = bases.Values.GroupBy(label => label, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AnimationChoice>(ids.Count);
        foreach (string id in ids)
        {
            string label = duplicateBases.Contains(bases[id]) ? $"{bases[id]} ({id})" : bases[id];
            if (!used.Add(label))
            {
                string disambiguated = $"{label} ({id})";
                int suffix = 2;
                while (!used.Add(disambiguated))
                    disambiguated = $"{label} ({id}) #{suffix++}";
                label = disambiguated;
            }
            result.Add(new AnimationChoice(id, label));
        }
        return result;
    }

    private string FriendlyAnimationLabel(string semanticId)
    {
        var binding = (_workspace.Catalog?.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .FirstOrDefault(candidate => candidate != null && candidate.SemanticId == semanticId);
        if (binding?.Clip != null && !string.IsNullOrEmpty(binding.Clip.name))
            return binding.Clip.name;
        if (_workspace.HasPackage)
        {
            var presentation = _workspace.Draft.Presentation;
            if (semanticId == presentation.Idle) return "Idle";
            if (semanticId == presentation.Run) return "Run";
            if (semanticId == presentation.Dash) return "Dash";
            if (semanticId == presentation.Jump) return "Jump";
            if (semanticId == presentation.Fall) return "Fall";
            if (semanticId == presentation.HitSmall) return "HitSmall";
            if (semanticId == presentation.HitMedium) return "HitMedium";
            if (semanticId == presentation.HitHard) return "HitHard";
        }
        return $"Unknown ({semanticId})";
    }

    private void SetText(string name, string value) => Required<TextField>(name).SetValueWithoutNotify(value ?? "");
    private void SetFloat(string name, float value) => Required<FloatField>(name).SetValueWithoutNotify(value);
    private void SetInteger(string name, int value) => Required<IntegerField>(name).SetValueWithoutNotify(value);
    private static ushort ClampUShort(int value) => (ushort)Mathf.Clamp(value, 0, ushort.MaxValue);
    private static byte ClampByte(int value) => (byte)Mathf.Clamp(value, 0, byte.MaxValue);
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);


    private void DiscoverPackages()
    {
        _packages.Clear();
        _packagesByDisplay.Clear();
        string packageRoot = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "Assets/CharacterPackages");
        if (!Directory.Exists(packageRoot))
        {
            _packages.Add(new PackageOption("", "No source packages", new[]
            {
                Diagnostic("package.discovery.missing", packageRoot, "Assets/CharacterPackages does not exist."),
            }));
        }
        else
        {
            foreach (string directory in Directory.GetDirectories(packageRoot).OrderBy(path => path, StringComparer.Ordinal))
            {
                string directoryName = Path.GetFileName(directory);
                string packagePath = Path.Combine(directory, "package.json");
                string characterPath = Path.Combine(directory, "character.json");
                var diagnostics = new List<CharacterDiagnostic>();
                string packageId = directoryName;
                string displayName = directoryName;
                try
                {
                    using var packageDocument = JsonDocument.Parse(File.ReadAllText(packagePath));
                    packageId = packageDocument.RootElement.GetProperty("packageId").GetString() ?? directoryName;
                    using var characterDocument = JsonDocument.Parse(File.ReadAllText(characterPath));
                    displayName = characterDocument.RootElement.GetProperty("displayName").GetString() ?? packageId;
                    if (!MatchContentCatalogBuilder.IsStablePackageId(packageId))
                        diagnostics.Add(Diagnostic("package.discovery.id-invalid", packagePath, "Source package ID is not stable."));
                }
                catch (Exception ex)
                {
                    diagnostics.Add(Diagnostic("package.discovery.invalid", directory, ex.Message));
                }
                var option = new PackageOption(packageId, displayName, diagnostics);
                _packages.Add(option);
                _packagesByDisplay[Display(option)] = option;
            }
        }
        _packageSelector.choices = _packages.Select(Display).ToList();
    }
    private void DiscoverCompatibilityCharacters()
    {
        _compatibilityCharacters.Clear();
        var resolution = SlopArena.Client.LocalContentResolver.CreateDefault().ResolveRoster();
        if (resolution.Success && resolution.Roster != null)
        {
            foreach (var selector in new[] { CharacterClass.Manki, CharacterClass.Kistu, CharacterClass.Nilus })
                if (resolution.Roster.Resolve(selector) != null)
                    _compatibilityCharacters.Add(selector);
        }

        _legacySelector.choices = _compatibilityCharacters.Select(selector => selector.ToString()).ToList();
        if (_legacySelector.choices.Count > 0)
            _legacySelector.SetValueWithoutNotify(_legacySelector.choices[0]);
    }

    private static string Display(PackageOption option) => string.IsNullOrEmpty(option.PackageId)
        ? option.DisplayName
        : $"{option.DisplayName} ({option.PackageId})";

    private void OpenPackage(string packageId)
    {
        if (string.IsNullOrEmpty(packageId)) return;
        if (!_workspace.OpenPackage($"Assets/CharacterPackages/{packageId}"))
        {
            RefreshAll();
            return;
        }
        RefreshAll();
    }

    private void RefreshAll()
    {
        string focusedName = (_root?.panel?.focusController?.focusedElement as VisualElement)?.name ?? "";
        _lab = FindLab();
        _inspection = _workspace.HasPackage
            ? new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(_workspace.PackageRoot)
            : null;
        BindPackageSelection();
        RefreshWorkspaceControls();
        RefreshPreview();
        RefreshCharacterPage();
        RefreshAssets();
        RefreshAdvanced();
        RefreshDiagnostics();
        RefreshRigState();
        UpdateTimelineControls();
        RefreshCompatibilityControls();
        RestoreFocus(focusedName);
    }

    private void RestoreFocus(string focusedName)
    {
        if (!string.IsNullOrEmpty(focusedName))
        {
            var target = _root.Q<VisualElement>(focusedName);
            if (target != null && target.focusable)
            {
                target.Focus();
                return;
            }
        }
        if (_timelineTrack != null)
            _timelineTrack.Focus();
    }


    private void BindPackageSelection()
    {
        string selected = _workspace.HasPackage ? _workspace.PackageId : _packages.FirstOrDefault()?.PackageId ?? "";
        string display = _packages.FirstOrDefault(option => option.PackageId == selected) is { } option ? Display(option) : _packageSelector.choices.FirstOrDefault() ?? "";
        _updatingControls = true;
        _packageSelector.SetValueWithoutNotify(display);
        _updatingControls = false;
    }
    private void RefreshWorkspaceControls()
    {
        _packageStatus.text = PackageStatus();
        _packageStatusToggle.text = _packageStatus.text;
        Required<Label>("package-display").text = _workspace.HasPackage ? $"{_workspace.Draft.DisplayName} · {_workspace.PackageId}" : "No package selected";
        Required<Button>("toolbar-undo").SetEnabled(_workspace.CanUndo);
        Required<Button>("toolbar-redo").SetEnabled(_workspace.CanRedo);
        SetPackageControlsVisible(_activePage != "compatibility-page");
    }

    private string PackageStatus()
    {
        if (!_workspace.HasPackage) return "No package";
        if (_workspace.IsDirty) return "Unsaved";
        if (_workspace.Status == "Cooking") return "Cooking…";
        if (_workspace.Status == "Failed" || _workspace.Preview == null || !_workspace.Preview.IsAvailable) return "Cook failed";
        if (_workspace.Status == "Stale") return "Stale";
        return "Cooked";
    }

    private void RefreshPreview()
    {
        _preview = _workspace.Preview;
        if (_activePage == "compatibility-page")
            return;
        if (_preview != null && _preview.IsAvailable)
        {
            _previewStatus.text = "Authoritative cooked package preview";
            _sceneViewGuidance.text = "Edit Mode preview: scrub the stopped timeline to update the rig, baked bones, hurtboxes, and hitboxes in SceneView.";
            string priorSlot = _lab?.SelectedSlotId ?? CanonicalSlotProjection.All[0].Id;
            if (_lab != null && _lab.SelectedPackageId != _preview.Identity.PackageId)
            {
                _lab.ApplyPackagePreview(_preview);
                if (CanonicalSlotProjection.TryGet(priorSlot, out var priorAddress))
                    _lab.SetSlot(priorAddress);
            }
            _airborneSelector = _lab?.SelectedSlotId.StartsWith("air.", StringComparison.Ordinal) ?? false;
            _updatingControls = true;
            _groundMovesButton.EnableInClassList("ground-air-selected", !_airborneSelector);
            _airMovesButton.EnableInClassList("ground-air-selected", _airborneSelector);
            _updatingControls = false;
            BuildMoveButtons(_airborneSelector);
        }
        else
        {
            _previewStatus.text = "Preview unavailable";
            _sceneViewGuidance.text = "Select a verified cooked package. Legacy characters are available only in Compatibility.";
            _moveSelector.Clear();
        }
    }
    private bool IsLoadedLegacy()
        => _lab != null && !_lab.IsPackagePreview &&
           (_lab.Character == CharacterClass.Manki || _lab.Character == CharacterClass.Kistu || _lab.Character == CharacterClass.Nilus);

    private void RefreshCompatibilityControls()
    {
        if (_compatibilityAuthority == null) return;
        bool loaded = IsLoadedLegacy();
        _compatibilityAuthority.text = loaded
            ? _lab!.PreviewStatus
            : "Compatibility Preview · Legacy authority · Read-only";
        _legacySelector.SetEnabled(_compatibilityCharacters.Count > 0);
        _legacyLoad.SetEnabled(_compatibilityCharacters.Count > 0);

        _updatingControls = true;
        if (loaded)
        {
            _legacySelector.SetValueWithoutNotify(_lab!.Character.ToString());
            _compatibilityAirborne.SetValueWithoutNotify(_lab.Airborne);
            _compatibilitySlotSelector.choices = AbilityLab.SlotNames.ToList();
            int slotLabelIndex = Array.IndexOf(AbilityLab.SlotIndices, _lab.SlotIndex);
            if (slotLabelIndex >= 0)
                _compatibilitySlotSelector.SetValueWithoutNotify(AbilityLab.SlotNames[slotLabelIndex]);

            int stageCount = _lab.CurrentSpec()?.Stages?.Length ?? 0;
            var stageChoices = Enumerable.Range(0, stageCount).Select(index => $"Stage {index + 1}").ToList();
            _compatibilityStageSelector.choices = stageChoices;
            if (stageChoices.Count > 0)
                _compatibilityStageSelector.SetValueWithoutNotify(stageChoices[Mathf.Clamp(_lab.StageIndex, 0, stageChoices.Count - 1)]);

            int duration = _lab.TryGetStage(out var stage) ? stage.DurationTicks : 0;
            int maxTick = Mathf.Max(0, duration - 1);
            int tick = Mathf.Clamp(_lab.Tick, 0, maxTick);
            _compatibilitySlider.lowValue = 0;
            _compatibilitySlider.highValue = maxTick;
            _compatibilitySlider.SetValueWithoutNotify(tick);
            _compatibilityTick.text = $"Tick {tick}";
            _compatibilityDuration.text = $"Duration {duration} ticks · {duration / AbilityLab.TickRate:0.00}s";
            _compatibilityPlay.text = _lab.Playing ? "Pause" : "Play";
            _compatibilityPlay.SetEnabled(stageCount > 0);
            _compatibilityStageSelector.SetEnabled(stageCount > 0);
            _compatibilitySlider.SetEnabled(stageCount > 0);
            _compatibilityStepBack.SetEnabled(stageCount > 0);
            _compatibilityStepForward.SetEnabled(stageCount > 0);
        }
        else
        {
            _compatibilitySlotSelector.choices = AbilityLab.SlotNames.ToList();
            _compatibilityStageSelector.choices = new List<string>();
            _compatibilitySlider.SetValueWithoutNotify(0);
            _compatibilitySlider.lowValue = 0;
            _compatibilitySlider.highValue = 0;
            _compatibilityTick.text = "Tick 0";
            _compatibilityDuration.text = "Duration —";
            _compatibilityPlay.text = "Play";
            _compatibilityPlay.SetEnabled(false);
            _compatibilityAirborne.SetValueWithoutNotify(false);
            _compatibilitySlotSelector.SetValueWithoutNotify(AbilityLab.SlotNames[0]);
            _compatibilityStageSelector.SetValueWithoutNotify("");
            _compatibilityStageSelector.SetEnabled(false);
            _compatibilitySlider.SetEnabled(false);
            _compatibilityStepBack.SetEnabled(false);
            _compatibilityStepForward.SetEnabled(false);
        }
        _compatibilityAirborne.SetEnabled(loaded);
        _compatibilitySlotSelector.SetEnabled(loaded);
        _compatibilityShowHurtboxes.SetValueWithoutNotify(_lab?.ShowHurtboxes ?? true);
        _compatibilityShowHitboxes.SetValueWithoutNotify(_lab?.ShowHitboxes ?? true);
        _compatibilityShowBakedBones.SetValueWithoutNotify(_lab?.ShowBakedBones ?? false);
        _compatibilityShowDummy.SetValueWithoutNotify(_lab?.ShowDummy ?? false);
        _compatibilityShowHurtboxes.SetEnabled(loaded);
        _compatibilityShowHitboxes.SetEnabled(loaded);
        _compatibilityShowBakedBones.SetEnabled(loaded);
        _compatibilityShowDummy.SetEnabled(loaded);
        _updatingControls = false;
    }

    private void SetCompatibilityTickDelta(int delta)
    {
        if (_lab == null || !IsLoadedLegacy() || !_lab.TryGetStage(out var stage)) return;
        int maxTick = Mathf.Max(0, stage.DurationTicks - 1);
        int next = Mathf.Clamp(_lab.Tick + delta, 0, maxTick);
        _lab.SetTick((ushort)next);
        RefreshCompatibilityControls();
        SceneView.RepaintAll();
    }

    private void BuildMoveButtons(bool airborne)
    {
        _moveSelector.Clear();
        foreach (var address in CanonicalSlotProjection.All.Where(slot => slot.IsAirborne == airborne))
        {
            string sourceName = _workspace.TryResolveCanonicalSlot(address.Id, out _, out var sourceSlot)
                ? FriendlyMoveLabel(address, sourceSlot)
                : $"Unknown ({address.Id})";
            var button = new Button(() =>
            {
                _lab?.SetSlot(address);
                UpdateTimelineControls();
                RefreshInspector();
                BuildMoveButtons(airborne);
            })
            {
                text = $"{address.InputLabel} · {sourceName}",
                userData = address,
            };
            button.name = address.Id == "ground.1" ? "selected-ground-1" : address.Id;
            button.AddToClassList("move-slot");
            if (_lab?.SelectedSlotId == address.Id) button.AddToClassList("move-slot-selected");
            _moveSelector.Add(button);
        }
    }

    private void RefreshAssets()
    {
        bool available = _workspace.HasPackage && _workspace.Catalog != null;
        _assetsPage.SetEnabled(available);
        _assetsRigField.SetValueWithoutNotify(available ? _workspace.Catalog.Rig : null);
        _assetsRigStatus.text = !available
            ? "No package or catalog is open."
            : DescribeRig(_workspace.Catalog.Rig);
        _assetsSkeleton.Clear();
        if (available)
        {
            var bakedNames = _lab?.BakedBoneNames ?? Array.Empty<string>();
            var names = bakedNames.Length > 0
                ? bakedNames
                : DeterministicPoseTrackBaker.RequiredBones.Select(bone => bone.ToString()).ToArray();
            foreach (string name in names)
                _assetsSkeleton.Add(new Label(name));
        }

        _assetsLocomotionBindings.Clear();
        var presentation = available ? _workspace.Draft.Presentation : null;
        if (presentation != null)
        {
            AddBindingRow(_assetsLocomotionBindings, "Idle", presentation.Idle);
            AddBindingRow(_assetsLocomotionBindings, "Run", presentation.Run);
            AddBindingRow(_assetsLocomotionBindings, "Dash", presentation.Dash);
            AddBindingRow(_assetsLocomotionBindings, "Jump", presentation.Jump);
            AddBindingRow(_assetsLocomotionBindings, "Fall", presentation.Fall);
        }

        _assetsHitReactionBindings.Clear();
        if (presentation != null)
        {
            AddBindingRow(_assetsHitReactionBindings, "HitSmall", presentation.HitSmall);
            AddBindingRow(_assetsHitReactionBindings, "HitMedium", presentation.HitMedium);
            AddBindingRow(_assetsHitReactionBindings, "HitHard", presentation.HitHard);
        }

        _assetsMoveBindings.Clear();
        if (available)
        {
            foreach (var address in CanonicalSlotProjection.All)
            {
                if (!_workspace.TryResolveCanonicalSlot(address.Id, out _, out var sourceSlot))
                {
                    var unknown = BuildBindingRow(address.Id, $"Unknown ({address.Id})", null);
                    unknown.tooltip = address.Id;
                    _assetsMoveBindings.Add(unknown);
                    continue;
                }
                for (int stageIndex = 0; stageIndex < sourceSlot.Timeline.Stages.Count; stageIndex++)
                {
                    var stage = sourceSlot.Timeline.Stages[stageIndex];
                    for (int animationIndex = 0; animationIndex < (stage.AnimationIds ?? Array.Empty<string>()).Count; animationIndex++)
                    {
                        string semanticId = stage.AnimationIds[animationIndex];
                        var row = BuildBindingRow(semanticId, $"Stage {stageIndex + 1} · Animation {animationIndex + 1}", FindBinding(semanticId));
                        row.tooltip = $"{address.Id} · source stage {stageIndex} · animation {animationIndex}";
                        _assetsMoveBindings.Add(row);
                    }
                }
            }
        }
        RefreshAssetsValidation();
    }

    private void AddBindingRow(VisualElement parent, string label, string semanticId)
        => parent.Add(BuildBindingRow(semanticId, label, FindBinding(semanticId)));

    private VisualElement BuildBindingRow(string semanticId, string label, CharacterAssetCatalog.AnimationBinding binding)
    {
        var row = new VisualElement { userData = semanticId ?? "" };
        row.AddToClassList("asset-binding-row");
        row.Add(new Label($"{label} · {FriendlyAnimationLabel(semanticId)}"));
        var clip = new ObjectField("Clip")
        {
            objectType = typeof(AnimationClip),
            allowSceneObjects = false,
        };
        clip.SetValueWithoutNotify(binding?.Clip);
        clip.SetEnabled(binding != null);
        var extrapolation = new EnumField("Extrapolation", binding?.Extrapolation ?? ExtrapolationMode.None);
        extrapolation.SetEnabled(binding != null);
        clip.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || binding == null) return;
            _workspace.ReplaceCatalogBinding((string)row.userData, evt.newValue as AnimationClip, (ExtrapolationMode)extrapolation.value);
            RefreshAll();
        });
        extrapolation.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || binding == null) return;
            _workspace.ReplaceCatalogBinding((string)row.userData, clip.value as AnimationClip, (ExtrapolationMode)evt.newValue);
            RefreshAll();
        });
        row.Add(clip);
        row.Add(extrapolation);
        return row;
    }

    private CharacterAssetCatalog.AnimationBinding FindBinding(string semanticId)
        => (_workspace.Catalog?.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .FirstOrDefault(binding => binding != null && binding.SemanticId == semanticId);

    private string DescribeRig(GameObject rig)
    {
        if (rig == null) return "Catalog rig missing; cooker will report asset-catalog.rig.missing.";
        var animator = rig.GetComponent<Animator>();
        if (animator == null) return $"{rig.name} · Animator missing";
        if (animator.avatar == null) return $"{rig.name} · Avatar missing";
        return $"{rig.name} · Avatar {(animator.avatar.isValid && animator.avatar.isHuman ? "valid Humanoid" : "invalid")}";
    }

    private void RefreshAssetsValidation()
    {
        _assetsValidation.Clear();
        if (!_workspace.HasPackage)
        {
            _assetsValidation.Add(new Label("No package selected."));
            return;
        }
        var diagnostics = _inspection?.RawDiagnostics ?? _workspace.Diagnostics;
        if (diagnostics.Count == 0)
        {
            _assetsValidation.Add(new Label("No diagnostics."));
            return;
        }
        foreach (var diagnostic in diagnostics)
            _assetsValidation.Add(new Label($"{diagnostic.Severity} · {diagnostic.Code} · {diagnostic.Path}\n{diagnostic.Message}"));
    }

    private string FriendlyMoveLabel(SlotAddress address, CharacterSlotSource sourceSlot)
        => !string.IsNullOrEmpty(sourceSlot?.Name) ? sourceSlot.Name : $"Unknown ({address.Id})";

    private void RefreshAdvanced()
    {
        bool available = _workspace.HasPackage && _inspection != null;
        _advancedPackagePaths.SetEnabled(available);
        _advancedSourcePath.text = available ? $"Source: {_inspection!.SourcePath}" : "Source: —";
        _advancedCookedPath.text = available ? $"Cooked: content-cooked/{_inspection!.PackageId}" : "Cooked: —";
        foreach (var group in new[] { _advancedHashes, _advancedRawIds, _advancedDiagnostics, _advancedProvenance, _advancedSchemaProfile })
            group.Clear();
        if (!available)
        {
            _advancedHashes.Add(new Label("No package selected."));
            _advancedMigrationStatus.text = "Current; no migration required";
            _advancedRenameConfirm.SetEnabled(false);
            _advancedMigrateAuthoring.SetEnabled(false);
            _advancedMigrateCatalog.SetEnabled(false);
            return;
        }

        var inspection = _inspection!;
        _advancedHashes.Add(new Label($"Source: {inspection.SourceHash}\nCooked source: {inspection.CookedSourceHash}\nCooked content: {inspection.CookedContentHash}\nPackage: {inspection.PackageHash}"));
        _advancedRawIds.Add(new Label($"Package ID: {inspection.PackageId}\nSemantic IDs: {string.Join(", ", _workspace.Catalog.Bindings.Where(x => x != null).Select(x => x.SemanticId))}\nPose IDs: {string.Join(", ", _workspace.Catalog.Bindings.Where(x => x != null).Select(x => x.PoseTrackId))}"));
        var diagnostics = new List<CharacterDiagnostic>(inspection.RawDiagnostics);
        if (inspection.Provenance != null)
            diagnostics.AddRange(inspection.Provenance.CookStatusDiagnostics.Select(x => new CharacterDiagnostic(
                x.Severity == "error" ? CharacterDiagnosticSeverity.Error : CharacterDiagnosticSeverity.Warning,
                x.Code, x.Path, x.Message)));
        if (diagnostics.Count == 0) _advancedDiagnostics.Add(new Label("No diagnostics."));
        foreach (var diagnostic in diagnostics)
            _advancedDiagnostics.Add(new Label($"{diagnostic.Severity} · {diagnostic.Code} · {diagnostic.Path}\n{diagnostic.Message}"));

        var provenance = inspection.Provenance;
        if (provenance == null)
        {
            _advancedProvenance.Add(new Label("No verified cooked manifest."));
            _advancedSchemaProfile.Add(new Label("No verified schema/profile metadata."));
        }
        else
        {
            _advancedProvenance.Add(new Label(
                $"Package: {provenance.PackageId} {provenance.Version}\nCreator: {provenance.Creator}\nLicense: {provenance.License}\nAttribution: {provenance.Attribution}\nCook status: {provenance.CookStatus}\nDependencies: {provenance.Dependencies.Count}\nUnity dependencies: {provenance.UnityDependencies.Count}\nPayloads: {string.Join(", ", provenance.Payloads.Select(x => $"{x.Path} [{x.Sha256}, {x.Size} bytes]"))}\nWarnings: {provenance.Warnings.Count}"));
            _advancedSchemaProfile.Add(new Label(
                $"Profile: {provenance.Profile}\nAuthoring schema: {provenance.AuthoringSchemaVersion}\nCooked schema: {provenance.CookedSchemaVersion}\nRuntime API: {provenance.RuntimeApiMin} – {provenance.RuntimeApiMax}\nCooker: {provenance.CookerVersion}\nUnity: {provenance.UnityVersion}\nBinding schema: {provenance.BindingSchemaVersion}\nPose: {provenance.PoseFormat} v{provenance.PoseVersion}\nSample rate: {provenance.SampleRate} Hz\nCapability requirements: {string.Join(", ", provenance.CapabilityRequirements.Select(x => $"{x.CapabilityId}@{x.CapabilityVersion}"))}"));
        }
        _advancedRenameConfirm.SetEnabled(true);
        _advancedMigrateAuthoring.SetEnabled(false);
        _advancedMigrateCatalog.SetEnabled(false);
        _advancedMigrationStatus.text = "Current; no migration required";
    }
    private void RefreshDiagnostics()
    {
        _diagnosticsPanel.Clear();
        var diagnostics = new List<CharacterDiagnostic>(_workspace.Diagnostics);
        if (_inspection != null) diagnostics.AddRange(_inspection.RawDiagnostics);
        if (_preview != null) diagnostics.AddRange(_preview.Diagnostics);
        foreach (var option in _packages.Where(option => option.Diagnostics.Count > 0)) diagnostics.AddRange(option.Diagnostics);
        var unique = diagnostics
            .GroupBy(x => $"{x.Severity}|{x.Code}|{x.Path}|{x.Message}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (unique.Count == 0) _diagnosticsPanel.Add(new Label("No diagnostics."));
        foreach (var diagnostic in unique)
            _diagnosticsPanel.Add(new Label($"{diagnostic.Code} · {diagnostic.Path}\n{diagnostic.Message}"));
    }

    private void RefreshRigState()
    {
        _lab = FindLab();
        if (_lab == null)
        {
            _rigSetupState.text = "No Ability Lab scene rig. Create Lab Rig to select or create the scene component.";
            return;
        }
        _rigSetupState.text = _preview != null && !_preview.IsAvailable
            ? "Ability Lab rig ready; package preview unavailable. No legacy fallback is active."
            : _preview != null && _preview.IsAvailable
                ? "Ability Lab rig ready; verified package rig is active."
                : "Ability Lab rig ready; select a package to preview.";
    }

    private void UpdateTimelineControls()
    {
        _timelineProjection = BuildTimelineProjection();
        if (_lab == null || !_lab.IsPackagePreview || _timelineProjection == null || _timelineProjection.Stages.Count == 0)
        {
            _timelineTick.text = "Tick 0";
            _timelineDuration.text = "Duration —";
            _timelineSlider.SetEnabled(false);
            _timelinePlay.SetEnabled(false);
            _stageSelector.style.display = DisplayStyle.None;
            _stageSelector.SetEnabled(false);
            _timelineTrack.Projection = _timelineProjection ?? EmptyTimeline();
            _timelineTrack.CurrentTick = 0;
            _timelineTrack.SelectedOperation = _selectedOperation = null;
            return;
        }

        int cumulativeTick = CumulativeTick(_timelineProjection, _lab.StageIndex, _lab.Tick);
        int duration = _timelineProjection.DurationTicks;
        _updatingControls = true;
        _timelineSlider.lowValue = 0;
        _timelineSlider.highValue = Mathf.Max(0, duration);
        _timelineSlider.SetValueWithoutNotify(Mathf.Clamp(cumulativeTick, 0, duration));
        _timelineSlider.SetEnabled(true);
        _timelinePlay.SetEnabled(true);
        _timelinePlay.text = _lab.Playing ? "Pause" : "Play";
        _timelineTick.text = $"Tick {cumulativeTick}";
        _timelineDuration.text = $"Duration {duration} ticks · {cumulativeTick / (float)AbilityLab.TickRate:0.00}s";
        if (!ReferenceEquals(_timelineTrack.Projection, _timelineProjection))
            _timelineTrack.Projection = _timelineProjection;
        _timelineTrack.CurrentTick = cumulativeTick;
        if (!ReferenceEquals(_timelineTrack.SelectedOperation, _selectedOperation))
            _timelineTrack.SelectedOperation = _selectedOperation;
        _updatingControls = false;
        UpdateStageSelector();
    }

    private AbilityLabTimelineProjection? BuildTimelineProjection()
    {
        if (!_workspace.HasPackage || _lab == null || !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var sourceSlot))
        {
            _timelineProjection = null;
            _timelineProjectionSlotId = "";
            _timelineProjectionDraft = null;
            return null;
        }
        if (ReferenceEquals(_timelineProjectionDraft, _workspace.Draft) && _timelineProjectionSlotId == _lab.SelectedSlotId)
            return _timelineProjection;
        _timelineProjectionSlotId = _lab.SelectedSlotId;
        _timelineProjectionDraft = _workspace.Draft;
        return _timelineProjection = AbilityLabTimelineProjection.Build(sourceSlot);
    }

    private static AbilityLabTimelineProjection EmptyTimeline()
        => AbilityLabTimelineProjection.Build(new CharacterSlotSource(
            "empty", "Empty", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false, new CharacterTimelineSource(Array.Empty<CharacterStageSource>())));

    private static int CumulativeTick(AbilityLabTimelineProjection projection, int stageIndex, ushort localTick)
    {
        if (projection.Stages.Count == 0) return 0;
        var stage = projection.Stages[Mathf.Clamp(stageIndex, 0, projection.Stages.Count - 1)];
        return Mathf.Clamp(stage.StartTick + localTick, 0, projection.DurationTicks);
    }

    private void SetTimelineZoom(float value)
    {
        if (_timelineTrack == null) return;
        float zoom = Mathf.Clamp(value, 0.5f, 4f);
        _timelineTrack.style.width = new Length(zoom * 100f, LengthUnit.Percent);
        _timelineTrack.MarkDirtyRepaint();
    }

    private void ApplyCumulativeTick(int cumulativeTick)
    {
        if (_updatingControls || _lab == null || _timelineProjection == null || _timelineProjection.Stages.Count == 0) return;
        int clamped = Mathf.Clamp(cumulativeTick, 0, _timelineProjection.DurationTicks);
        var stage = _timelineProjection.Stages[^1];
        foreach (var candidate in _timelineProjection.Stages)
            if (clamped < candidate.EndTick) { stage = candidate; break; }
        int local = Mathf.Clamp(clamped - stage.StartTick, 0, Mathf.Max(0, stage.DurationTicks - 1));
        _lab.SetStage(stage.SourceStageIndex);
        _lab.SetTick((ushort)local);
        UpdateTimelineControls();
    }


    private void UpdateStageSelector()
    {
        if (_timelineProjection == null || _timelineProjection.Stages.Count <= 1)
        {
            _stageSelector.style.display = DisplayStyle.None;
            _stageSelector.SetEnabled(false);
            _stageSelector.choices = new List<string>();
            _stageSelector.SetValueWithoutNotify("");
            return;
        }
        var choices = _timelineProjection.Stages.Select(stage => $"Stage {stage.SourceStageIndex + 1}").ToList();
        _stageSelector.style.display = DisplayStyle.Flex;
        _stageSelector.choices = choices;
        _stageSelector.SetValueWithoutNotify(choices[Mathf.Clamp(_lab!.StageIndex, 0, choices.Count - 1)]);
        _stageSelector.SetEnabled(true);
    }

    private void SetTickDelta(int delta)
    {
        if (_timelineProjection == null || _lab == null) return;
        ApplyCumulativeTick(CumulativeTick(_timelineProjection, _lab.StageIndex, _lab.Tick) + delta);
    }

    private void OnRootKeyDown(KeyDownEvent evt)
    {
        if (IsTextInput(evt.target as VisualElement)) return;
        bool packageMode = _activePage != "compatibility-page" && _workspace.HasPackage;
        if (evt.keyCode == KeyCode.Escape)
        {
            _timelineTrack.CancelDrag();
            _timelineTrack.Focus();
            evt.StopPropagation();
            return;
        }
        if (evt.ctrlKey && evt.shiftKey && evt.keyCode == KeyCode.Z && packageMode)
        {
            _workspace.Redo();
            RefreshAll();
            evt.StopPropagation();
            return;
        }
        if (evt.ctrlKey && evt.keyCode == KeyCode.Z && packageMode)
        {
            _workspace.Undo();
            RefreshAll();
            evt.StopPropagation();
            return;
        }
        if (evt.ctrlKey && evt.keyCode == KeyCode.S && packageMode)
        {
            _workspace.SavePackage();
            RefreshAll();
            evt.StopPropagation();
            return;
        }
        if (packageMode && _lab != null && _lab.IsPackagePreview &&
            (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow))
        {
            SetTickDelta(evt.keyCode == KeyCode.LeftArrow ? -1 : 1);
            evt.StopPropagation();
        }
    }

    private static bool IsTextInput(VisualElement? target)
    {
        for (var current = target; current != null; current = current.parent)
            if (current is TextField || current is IntegerField || current is FloatField)
                return true;
        return false;
    }

    private void SelectOperation(AbilityLabOperationProjection operation)
    {
        _selectedOperation = operation;
        if (_lab == null) return;
        _lab.SetStage(operation.SourceStageIndex);
        var projection = _timelineProjection ?? BuildTimelineProjection();
        int local = projection == null || operation.SourceStageIndex >= projection.Stages.Count
            ? 0
            : Mathf.Clamp(operation.StartTick - projection.Stages[operation.SourceStageIndex].StartTick, 0,
                Mathf.Max(0, projection.Stages[operation.SourceStageIndex].DurationTicks - 1));
        _lab.SetTick((ushort)local);
        int hitboxIndex = -1;
        if (operation.Source is SpawnHitboxOperationSource)
        {
            if (_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var sourceSlot) &&
                operation.SourceStageIndex < sourceSlot.Timeline.Stages.Count)
            {
                var operations = sourceSlot.Timeline.Stages[operation.SourceStageIndex].Operations;
                for (int i = 0; i < operation.SourceOperationIndex; i++)
                    if (operations[i] is SpawnHitboxOperationSource) hitboxIndex++;
                hitboxIndex++;
            }
        }
        _lab.SelectHitbox(hitboxIndex);
        UpdateTimelineControls();
        RefreshInspector();
    }
    private void CompleteTimelineDrag(AbilityLabTimelineDrag drag)
    {
        if (_lab == null || !_lab.IsPackagePreview || !_workspace.HasPackage) return;
        bool accepted = drag.Mode == TimelineDragMode.Move
            ? _workspace.ReplaceOperationTick(_lab.SelectedSlotId, drag.SourceStageIndex, drag.SourceOperationIndex, drag.Tick)
            : _workspace.ReplaceHitboxDuration(_lab.SelectedSlotId, drag.SourceStageIndex, drag.SourceOperationIndex, drag.DurationTicks);
        if (!accepted) return;

        _lab.SetStage(drag.SourceStageIndex);
        _lab.SetTick((ushort)Mathf.Clamp(drag.Tick, 0, ushort.MaxValue));
        _timelineProjection = BuildTimelineProjection();
        _selectedOperation = FindProjectedOperation(drag.SourceStageIndex, drag.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private AbilityLabOperationProjection? FindProjectedOperation(int stageIndex, int operationIndex)
    {
        if (_timelineProjection == null || stageIndex < 0 || stageIndex >= _timelineProjection.Stages.Count) return null;
        foreach (var operation in _timelineProjection.Stages[stageIndex].Operations)
            if (operation.SourceOperationIndex == operationIndex) return operation;
        return null;
    }


    private void RefreshInspector()
    {
        _inspector.Clear();
        if (_lab == null || !_workspace.HasPackage ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var slot) ||
            _lab.StageIndex < 0 || _lab.StageIndex >= slot.Timeline.Stages.Count)
        {
            _stageSelector.style.display = DisplayStyle.None;
            _stageSelector.SetEnabled(false);
            _inspector.Add(new Label("Select a cooked package and move."));
            return;
        }

        bool multiStage = slot.Timeline.Stages.Count > 1;
        _stageSelector.style.display = multiStage ? DisplayStyle.Flex : DisplayStyle.None;
        _stageSelector.SetEnabled(multiStage);
        if (multiStage)
            _inspector.Add(_stageSelector);

        var stage = slot.Timeline.Stages[_lab.StageIndex];
        var moveGroup = new Foldout { text = $"Move · {slot.Name}", value = true };
        moveGroup.Add(new Label($"Duration {stage.DurationTicks} · IASA {stage.IasaTicks} · Landing lag {stage.LandingLagTicks}"));
        moveGroup.Add(new Label($"Auto-cancel before {stage.AutoCancelBeforeTicks} · after {stage.AutoCancelAfterTicks}"));
        var animationIds = (_preview?.AnimationCatalog?.Animations ?? Array.Empty<CharacterAnimationCatalog.AnimationEntry>())
            .Where(animation => animation != null && !string.IsNullOrEmpty(animation.SemanticId))
            .Select(animation => animation.SemanticId)
            .Concat(stage.AnimationIds ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var animationChoices = BuildAnimationChoices(animationIds);
        foreach (string animationId in stage.AnimationIds ?? Array.Empty<string>())
        {
            var selectedChoice = animationChoices.FirstOrDefault(choice => choice.SemanticId == animationId);
            var choices = animationChoices.Select(choice => choice.Label).ToList();
            var field = new PopupField<string>($"Animation · {selectedChoice?.Label ?? $"Unknown ({animationId})"}", choices,
                selectedChoice == null ? 0 : choices.IndexOf(selectedChoice.Label));
            field.RegisterValueChangedCallback(evt =>
            {
                var choice = animationChoices.FirstOrDefault(item => item.Label == evt.newValue);
                if (choice != null)
                    CommitStage(current => current with
                    {
                        AnimationIds = current.AnimationIds.Select(id => id == animationId ? choice.SemanticId : id).ToArray()
                    }, _lab.StageIndex);
            });
            moveGroup.Add(field);
        }
        _inspector.Add(moveGroup);

        var selected = _selectedOperation;
        if (selected?.Source is SpawnHitboxOperationSource)
        {
            var hitbox = ((SpawnHitboxOperationSource)selected.Source).Hitbox;
            var group = new Foldout { text = "Hitbox", value = true };
            AddHitboxTiming(group, hitbox);
            AddHitboxCombat(group, hitbox);
            AddHitboxShape(group, hitbox);
            AddHitboxAttachment(group, hitbox);
            _inspector.Add(group);
        }
        else if (selected != null)
        {
            _inspector.Add(new Foldout { text = selected.Summary, value = true });
            _inspector.Add(new Label($"Authored range [{selected.StartTick}, {selected.EndTick}) · {selected.Source.Unit}"));
        }
    }

    private void CommitStage(Func<CharacterStageSource, CharacterStageSource> edit, int stageIndex)
    {
        if (_updatingControls || _lab == null) return;
        _updatingControls = true;
        try { _workspace.ReplaceStage(_lab.SelectedSlotId, stageIndex, edit(CurrentStage())); }
        finally { _updatingControls = false; }
        UpdateTimelineControls();
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private CharacterStageSource CurrentStage()
    {
        if (_lab == null || !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var slot))
            throw new InvalidOperationException("No selected source slot.");
        return slot.Timeline.Stages[_lab.StageIndex];
    }

    private void CommitHitbox(Func<HitboxSource, HitboxSource> edit)
    {
        if (_updatingControls || _lab == null || _selectedOperation?.Source is not SpawnHitboxOperationSource original) return;
        int stageIndex = _selectedOperation.SourceStageIndex;
        int operationIndex = _selectedOperation.SourceOperationIndex;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceHitbox(_lab.SelectedSlotId, stageIndex, operationIndex, edit(original.Hitbox));
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void AddHitboxTiming(Foldout group, HitboxSource value)
    {
        var timing = new Foldout { text = "Timing", value = true };
        AddDelayedInteger(timing, "Stun ticks", value.StunTicks, v => CommitHitbox(h => h with { StunTicks = (ushort)Mathf.Clamp(v, 0, ushort.MaxValue) }));
        AddDelayedInteger(timing, "Active duration ticks", value.DurationTicks, v => CommitHitbox(h => h with { DurationTicks = (ushort)Mathf.Clamp(v, 0, ushort.MaxValue) }));
        AddToggle(timing, "Interruptible", value.Interruptible, v => CommitHitbox(h => h with { Interruptible = v }));
        AddDelayedInteger(timing, "Hit group", value.HitGroup, v => CommitHitbox(h => h with { HitGroup = (byte)Mathf.Clamp(v, 0, byte.MaxValue) }));
        group.Add(timing);
    }

    private void AddHitboxCombat(Foldout group, HitboxSource value)
    {
        var combat = new Foldout { text = "Combat", value = true };
        AddDelayedFloat(combat, "Damage", value.Damage, v => CommitHitbox(h => h with { Damage = v }));
        AddDelayedFloat(combat, "Angle", value.Angle, v => CommitHitbox(h => h with { Angle = v }));
        AddDelayedFloat(combat, "Base knockback", value.BaseKnockback, v => CommitHitbox(h => h with { BaseKnockback = v }));
        AddDelayedFloat(combat, "Knockback growth", value.KnockbackGrowth, v => CommitHitbox(h => h with { KnockbackGrowth = v }));
        group.Add(combat);
    }

    private void AddHitboxShape(Foldout group, HitboxSource value)
    {
        var shape = new Foldout { text = "Shape", value = true };
        var enumField = new EnumField("Shape", value.Shape);
        enumField.RegisterValueChangedCallback(evt => CommitHitbox(h => h with { Shape = (AuthoringHitboxShape)evt.newValue }));
        shape.Add(enumField);
        AddDelayedFloat(shape, "Radius", value.Radius, v => CommitHitbox(h => h with { Radius = v }));
        AddDelayedFloat(shape, "Offset X", value.OffsetX, v => CommitHitbox(h => h with { OffsetX = v }));
        AddDelayedFloat(shape, "Offset Y", value.OffsetY, v => CommitHitbox(h => h with { OffsetY = v }));
        AddDelayedFloat(shape, "Offset Z", value.OffsetZ, v => CommitHitbox(h => h with { OffsetZ = v }));
        AddDelayedFloat(shape, "End offset X", value.EndOffsetX, v => CommitHitbox(h => h with { EndOffsetX = v }));
        AddDelayedFloat(shape, "End offset Y", value.EndOffsetY, v => CommitHitbox(h => h with { EndOffsetY = v }));
        AddDelayedFloat(shape, "End offset Z", value.EndOffsetZ, v => CommitHitbox(h => h with { EndOffsetZ = v }));
        group.Add(shape);
    }

    private void AddHitboxAttachment(Foldout group, HitboxSource value)
    {
        var attachment = new Foldout { text = "Attachment", value = true };
        AddBonePopup(attachment, "Start bone", value.StartBoneId, id => CommitHitbox(h => h with { StartBoneId = id }));
        AddBonePopup(attachment, "End bone", value.EndBoneId, id => CommitHitbox(h => h with { EndBoneId = id }));
        group.Add(attachment);
    }

    private void AddBonePopup(Foldout group, string label, string? value, Action<string?> commit)
    {
        var choices = new List<string> { "" };
        choices.AddRange(_lab?.BakedBoneNames ?? Array.Empty<string>());
        if (!string.IsNullOrEmpty(value) && !choices.Contains(value, StringComparer.Ordinal)) choices.Add(value);
        var field = new PopupField<string>(label, choices, choices.IndexOf(value ?? ""));
        field.RegisterValueChangedCallback(evt => commit(string.IsNullOrEmpty(evt.newValue) ? null : evt.newValue));
        group.Add(field);
    }

    private void AddDelayedFloat(VisualElement parent, string label, float value, Action<float> commit)
    {
        var field = new FloatField(label) { value = value, isDelayed = true };
        field.RegisterValueChangedCallback(evt => commit(evt.newValue));
        parent.Add(field);
    }

    private void AddDelayedInteger(VisualElement parent, string label, int value, Action<int> commit)
    {
        var field = new IntegerField(label) { value = value, isDelayed = true };
        field.RegisterValueChangedCallback(evt => commit(evt.newValue));
        parent.Add(field);
    }

    private void AddToggle(VisualElement parent, string label, bool value, Action<bool> commit)
    {
        var field = new Toggle(label) { value = value };
        field.RegisterValueChangedCallback(evt => commit(evt.newValue));
        parent.Add(field);
    }

    private void CreateOrSelectLabRig()
    {
        _lab = FindLab();
        if (_lab == null)
        {
            var go = new GameObject("AbilityLab");
            _lab = go.AddComponent<AbilityLab>();
        }
        _lab.EnsureCamera();
        Selection.activeGameObject = _lab.gameObject;
        RefreshAll();
    }

    private AbilityLab? FindLab() => AbilityLab.Instance != null ? AbilityLab.Instance : FindObjectOfType<AbilityLab>();

    private void OnSceneGUI(SceneView sceneView)
    {
        if (_activePage != "moves-page" || _lab == null || !_lab.IsPackagePreview ||
            !_workspace.HasPackage || _preview == null || !_preview.IsAvailable || _lab.Playing)
            return;

        var hitboxes = _lab.ResolveHitboxes();
        if (hitboxes.Count == 0)
        {
            _sceneRadiusEditing = false;
            return;
        }

        Handles.BeginGUI();
        GUILayout.Label("Ability Lab package preview · click a hitbox to select", EditorStyles.miniLabel);
        Handles.EndGUI();

        AbilityLabOperationProjection? selectedOperation = null;
        foreach (var hitbox in hitboxes)
        {
            var operation = FindSourceHitboxOperation(hitbox.index);
            float size = HandleUtility.GetHandleSize(hitbox.start) * 0.12f;
            if (Handles.Button(hitbox.start, Quaternion.identity, size, size, Handles.SphereHandleCap) && operation != null)
            {
                SelectOperation(operation);
                SceneView.RepaintAll();
            }
            if (hitbox.index == _lab.SelectedHitboxEventIndex && operation != null)
                selectedOperation = operation;
        }

        if (selectedOperation == null || _lab.SelectedHitboxEventIndex < 0) return;
        var selectedHitbox = hitboxes.FirstOrDefault(item => item.index == _lab.SelectedHitboxEventIndex);
        if (selectedHitbox.evt.DurationTicks == 0) return;
        float radius = _sceneRadiusEditing ? _sceneRadiusPending : selectedHitbox.evt.Radius;
        EditorGUI.BeginChangeCheck();
        float changedRadius = Handles.RadiusHandle(Quaternion.identity, selectedHitbox.start, radius);
        if (EditorGUI.EndChangeCheck())
        {
            _sceneRadiusPending = Mathf.Max(0.0001f, changedRadius);
            _sceneRadiusStageIndex = selectedOperation.SourceStageIndex;
            _sceneRadiusOperationIndex = selectedOperation.SourceOperationIndex;
            _sceneRadiusEditing = true;
        }
        if (_sceneRadiusEditing && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            CommitSceneRadius();
    }

    private AbilityLabOperationProjection? FindSourceHitboxOperation(int hitboxIndex)
    {
        if (_lab == null || _timelineProjection == null || _lab.StageIndex < 0 || _lab.StageIndex >= _timelineProjection.Stages.Count)
            return null;
        int index = 0;
        foreach (var operation in _timelineProjection.Stages[_lab.StageIndex].Operations)
            if (operation.Source is SpawnHitboxOperationSource)
            {
                if (index++ == hitboxIndex) return operation;
            }
        return null;
    }

    private void CommitSceneRadius()
    {
        if (_lab == null || !_workspace.HasPackage || _sceneRadiusStageIndex < 0 || _sceneRadiusOperationIndex < 0)
        {
            _sceneRadiusEditing = false;
            return;
        }
        if (!_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var slot) ||
            _sceneRadiusStageIndex >= slot.Timeline.Stages.Count)
        {
            _sceneRadiusEditing = false;
            return;
        }
        var operations = slot.Timeline.Stages[_sceneRadiusStageIndex].Operations;
        if (_sceneRadiusOperationIndex >= operations.Count || operations[_sceneRadiusOperationIndex] is not SpawnHitboxOperationSource hitbox)
        {
            _sceneRadiusEditing = false;
            return;
        }
        bool accepted = _workspace.ReplaceHitbox(_lab.SelectedSlotId, _sceneRadiusStageIndex, _sceneRadiusOperationIndex,
            hitbox.Hitbox with { Radius = _sceneRadiusPending });
        _sceneRadiusEditing = false;
        _sceneRadiusStageIndex = -1;
        _sceneRadiusOperationIndex = -1;
        if (accepted) SceneView.RepaintAll();
    }


    private static CharacterDiagnostic Diagnostic(string code, string path, string message)
        => new(CharacterDiagnosticSeverity.Error, code, path, message);
}
