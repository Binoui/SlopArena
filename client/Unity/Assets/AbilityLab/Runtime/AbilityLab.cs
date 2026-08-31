using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using SlopArena.Shared;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;

namespace SlopArena.Client.Tools
{
    /// Ability Lab rig: frame-by-frame preview of hurtboxes + hitboxes for the selected
    /// legacy catalog entry or an in-memory cooked Character Package. Poses come from
    /// the baked skeleton through the same Shared resolvers used by the server.
    ///
    /// Package source ownership, typed DTO editing, hashes, persistence, and cooking live
    /// in AbilityLabPackageWorkspace. WorkingEvents is only a transient legacy/render
    /// projection and never writes package source.
    ///
    /// ExecuteAlways: the orbit camera and verified package preview work in edit mode.
    /// Legacy compatibility preview remains play-mode only.
    [ExecuteAlways]
    public class AbilityLab : MonoBehaviour
    {
        public const float TickRate = 60f; // sim ticks per second (matches bake sample rate)

        /// <summary>Fixed Ability Lab slot order: 1, 2, 3, 4, A, E, R, F.</summary>
        public static readonly int[] SlotIndices = { 2, 6, 7, 8, 10, 3, 4, 5 };
        public static readonly string[] SlotNames = { "1", "2", "3", "4", "A", "E", "R", "F" };

        public static AbilityLab Instance { get; private set; }

        // ── Selection state ──
        public CharacterClass Character { get; private set; } = CharacterClass.None;
        public string SelectedPackageId { get; private set; } = "";
        public string SelectedSlotId { get; private set; } = "";
        public bool IsPackagePreview => AuthoritativePreview && !string.IsNullOrEmpty(SelectedPackageId);
        public int SlotIndex { get; private set; }
        public bool Airborne { get; private set; }
        public int StageIndex { get; private set; }
        public ushort Tick { get; private set; }
        public int SelectedHitboxEventIndex { get; private set; } = -1;
        public bool Playing { get; set; }
        public float PlaySpeed { get; set; } = 1f;
        public float FacingYaw { get; set; }
        public bool ShowHurtboxes { get; set; }
        public bool ShowHitboxes { get; set; } = true;
        public bool ShowBakedBones { get; set; }
        public bool ShowDummy { get; set; }
        public float DummyDistance { get; set; } = 2.5f;

        // ── Knockback trajectory preview ──
        /// <summary>Draw the knockback arc for the selected hitbox on the dummy (opt-in).</summary>
        public bool ShowTrajectory { get; set; }
        /// <summary>Victim damage % used for the trajectory preview (shape is %-dependent).</summary>
        public float TrajectoryPercent { get; set; } = 0f;
        /// <summary>Hitbox index (into CurrentWorkingEvents) whose knockback the preview draws.</summary>
        public int PreviewHitboxIndex { get; set; }
        /// <summary>Last computed arc: (world pos, phase 'H'=hitstun 'F'=flight 'A'=apex 'G'=landing).</summary>
        public IReadOnlyList<(Vector3 pos, char phase)> Trajectory => _trajectory;
        /// <summary>Cache guard: recompute only when a preview input changes.</summary>
        private string _trajDirty = "";

        // ── Loaded data ──
        public CharacterDefinition Def { get; private set; } = null!;
        public CharacterDefinition DisplayDef { get; private set; } = null!; // Def + hurtbox override
        public BakedAnimationData? Baked { get; private set; }
        public string[] BakedBoneNames => Baked?.BoneNames ?? Array.Empty<string>();
        public bool AuthoritativePreview { get; private set; }
        public string PreviewStatus { get; private set; } = "Legacy";
        private CharacterAnimationCatalog _previewAnimationCatalog;
        private GameObject _previewRig;
        public PlayerRenderer Renderer { get; private set; } = null!;
        public HurtboxBoneDef[] WorkingDefs { get; private set; } = Array.Empty<HurtboxBoneDef>();
        /// <summary>Per-(slot, airborne, stage) hitbox event edits (key = "slot:airborne:stage").</summary>
        public Dictionary<string, HitboxEvent[]> WorkingEvents { get; private set; } = new();

        /// <summary>Per-(slot, airborne) hitstop multiplier edits keyed by content ability name.</summary>
        public Dictionary<string, float> WorkingHitstopOverrides { get; private set; } = new();


        private readonly List<SpellResolver.EntityData> _hurtboxes = new();
        private readonly List<(int index, HitboxEvent evt, Vector3 start, Vector3 end)> _hitboxes = new();
        private readonly List<(Vector3 pos, char phase)> _trajectory = new();
        private PlayerRenderer _dummyRenderer = null!;
        private WeaponAttach _weaponAttach;
        private WeaponAttach _dummyWeaponAttach;
        private float _playAccum;
        [SerializeField] private UnityEngine.Camera _camera = null!;
        private Vector2 _orbitAngles = new(25f, 0f);
        private float _orbitDistance = 4.5f;
        private Vector3 _orbitPivot;
        // Undo/redo stores complete source DTOs; WorkingEvents remains a render projection.
        private const int MaxUndoDepth = 50;
        private CharacterPackageSource? _sourceDocument;
        private readonly Stack<CharacterPackageSource> _undo = new();
        private readonly Stack<CharacterPackageSource> _redo = new();
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        private void Awake()
        {
            Instance = this;
            EnsureCamera();
        }

        private void OnDestroy()
        {
            DestroyRenderer();
            DestroyPreviewCatalog();
            if (Instance == this) Instance = null;
        }

        // ── Lifecycle ──

        private void Update()
        {
            if (!Playing) return;
            if (!TryGetStage(out var stage)) return;
            _playAccum += Time.deltaTime * PlaySpeed;
            while (_playAccum >= 1f / TickRate)
            {
                _playAccum -= 1f / TickRate;
                Tick = (ushort)((Tick + 1) % Math.Max(1, (int)stage.DurationTicks));
            }
            RefreshPose();
        }

        private void LateUpdate()
        {
            if (_camera != null)
            {
                // Lab camera: right-drag orbits, middle-drag pans the pivot, scroll zooms.
                // Project uses the new Input System — no legacy Input.* calls.
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    if (mouse.rightButton.isPressed)
                    {
                        Vector2 delta = mouse.delta.ReadValue();
                        _orbitAngles.x = Mathf.Clamp(_orbitAngles.x - delta.y * 0.1f, 5f, 85f);
                        _orbitAngles.y += delta.x * 0.1f;
                    }
                    if (mouse.middleButton.isPressed)
                    {
                        Vector2 delta = mouse.delta.ReadValue();
                        float scale = _orbitDistance * 0.0015f;
                        _orbitPivot += (-_camera.transform.right * delta.x + _camera.transform.up * delta.y) * scale;
                    }
                    _orbitDistance = Mathf.Clamp(_orbitDistance - mouse.scroll.ReadValue().y * 0.05f, 1f, 20f);
                }
                Quaternion rot = Quaternion.Euler(_orbitAngles.x, _orbitAngles.y, 0f);
                _camera.transform.position = _orbitPivot - rot * Vector3.forward * _orbitDistance;
                _camera.transform.rotation = rot;
            }
        }

        /// <summary>Reset the lab camera to its default orbit around the character.</summary>
        public void ResetCameraView()
        {
            _orbitAngles = new Vector2(25f, 0f);
            _orbitDistance = 4.5f;
            _orbitPivot = transform.position;
            if (_camera != null)
            {
                Quaternion rot = Quaternion.Euler(_orbitAngles.x, _orbitAngles.y, 0f);
                _camera.transform.position = _orbitPivot - rot * Vector3.forward * _orbitDistance;
                _camera.transform.rotation = rot;
            }
        }

        // ── Loading ──

        /// <summary>
        /// Reuse the scene's main camera for the orbit view, or create one when none
        /// exists (fresh/empty scene). Called by the lab window when the rig is built.
        /// </summary>
        public void EnsureCamera()
        {
            if (_camera != null) return; // serialized ref survives edit→play remap
            _camera = UnityEngine.Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("LabCamera");
                camGo.transform.SetParent(transform, false);
                _camera = camGo.AddComponent<UnityEngine.Camera>();
                camGo.AddComponent<AudioListener>();
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
                _camera.fieldOfView = 60f;
                _camera.nearClipPlane = 0.05f;
            }
            ResetCameraView();
        }

        public bool LoadCharacter(CharacterClass character)
        {
            if (character == CharacterClass.None || character == Character) return character == Character;
            var resolution = SlopArena.Client.LocalContentResolver.CreateDefault().ResolveLegacy(character);

            if (!resolution.Success || resolution.LegacyEntry == null)
            {
                Debug.LogError($"[AbilityLab] Failed to resolve legacy content for {character}: {FormatDiagnostics(resolution.Diagnostics)}");
                return false;
            }
            if (!Application.isPlaying)
            {
                Debug.Log("[AbilityLab] Legacy compatibility preview is available only in Play Mode.");
                return false;
            }

            ApplyLegacyPreview(resolution.LegacyEntry);
            return true;
        }

        public void LoadCharacter(ContentHandle handle)
        {
            if (!handle.IsValid) return;
            if (!SlopArena.Client.ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
            {
                Debug.LogError($"[AbilityLab] Failed to build local content catalog: {failure}");
                return;
            }
            var entry = catalog.Resolve(handle);
            if (entry == null)
            {
                Debug.LogError($"[AbilityLab] Unknown content handle {handle.Value}.");
                return;
            }
            if (entry.CookedCharacterPackage != null)
            {
                CharacterAnimationCatalog animationCatalog = null;
                GameObject rig = null;
                string error = "";
                bool resolved = entry.BakedAnimation != null &&
                    CookedCharacterClientAssetResolver.TryResolve(entry, out animationCatalog, out rig, out error);
                if (!resolved)
                {
                    if (entry.BakedAnimation == null)
                        error = "Cooked pose payload is missing.";
                    Debug.LogError($"[AbilityLab] Cooked client assets failed for {entry.Identity.PackageId}: {error}");
                    return;
                }
                ApplyCookedPackagePreview(
                    entry.CookedCharacterPackage, entry.BakedAnimation, animationCatalog, rig,
                    CharacterClass.None, authoritative: true);
                return;
            }
            if (!Application.isPlaying)
            {
                Debug.Log("[AbilityLab] Legacy compatibility preview is available only in Play Mode.");
                return;
            }

            ApplyLegacyPreview(entry);
        }

        private void ApplyLegacyPreview(MatchContentEntry entry)
        {
            var loadedDef = entry.Definition;
            var loadedBaked = LoadBaked(loadedDef);
            var loadedWorkingDefs = LoadWorkingDefs(loadedDef, loadedBaked);

            Character = entry.LegacySelector ?? CharacterClass.None;
            SelectedPackageId = "";
            SelectedSlotId = "";
            Def = loadedDef;
            Baked = loadedBaked;
            WorkingDefs = loadedWorkingDefs;
            DisplayDef = HurtboxOverride.Apply(Def, WorkingDefs);
            WorkingEvents = new Dictionary<string, HitboxEvent[]>();
            WorkingHitstopOverrides = new Dictionary<string, float>();
            AuthoritativePreview = false;
            PreviewStatus = $"Compatibility Preview · {Character} · Legacy authority · Read-only";
            ShowHurtboxes = true;
            ShowHitboxes = true;
            ShowBakedBones = false;
            ShowDummy = false;
            _sourceDocument = null;
            DestroyPreviewCatalog();
            _previewRig = null;

            Airborne = false;
            SlotIndex = SlotIndices[0];
            StageIndex = 0;
            Tick = 0;
            Playing = false;
            _undo.Clear();
            _redo.Clear();
            RefreshPose();
        }

        public void ApplyPackagePreview(AbilityLabPackagePreviewResult result)
        {
            if (result == null || !result.IsAvailable || result.Package == null ||
                result.BakedPoses == null || result.AnimationCatalog == null ||
                result.Rig == null || result.Identity == null)
            {
                ApplyPreviewUnavailable(result?.Diagnostics ?? Array.Empty<CharacterDiagnostic>());
                return;
            }

            ApplyPackageData(
                result.Package,
                result.BakedPoses,
                result.AnimationCatalog,
                result.Rig,
                result.Identity.PackageId);
        }

        public void ApplyCookedPackagePreview(
            CookedCharacterPackage package,
            BakedAnimationData baked,
            CharacterAnimationCatalog animationCatalog,
            GameObject rig,
            CharacterClass legacySelector = CharacterClass.None,
            bool authoritative = true)
        {
            if (package == null || baked == null || animationCatalog == null || rig == null)
            {
                ApplyPreviewUnavailable(new[]
                {
                    new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "preview.binding.failed", "package", "Cooked package preview data is incomplete."),
                });
                return;
            }

            if (authoritative)
            {
                ApplyPackageData(package, baked, animationCatalog, rig, package.Metadata.PackageId);
                return;
            }

            DestroyPreviewCatalog();
            _previewAnimationCatalog = animationCatalog;
            _previewRig = rig;
            var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(package, legacySelector);
            Character = legacySelector;
            SelectedPackageId = "";
            SelectedSlotId = "";
            Def = definition;
            Baked = baked;
            WorkingDefs = definition.HurtboxBoneDefs != null ? (HurtboxBoneDef[])definition.HurtboxBoneDefs.Clone() : Array.Empty<HurtboxBoneDef>();
            DisplayDef = definition;
            AuthoritativePreview = false;
            PreviewStatus = "Non-authoritative draft";
            ShowHurtboxes = false;
            ShowHitboxes = true;
            ShowBakedBones = false;
            ShowDummy = false;
            SpawnRenderer();
            Airborne = false; SlotIndex = SlotIndices[0]; StageIndex = 0; Tick = 0; Playing = false;
            WorkingEvents = new Dictionary<string, HitboxEvent[]>();
            WorkingHitstopOverrides = new Dictionary<string, float>();
            _undo.Clear(); _redo.Clear();
            RefreshPose();
        }

        private void ApplyPackageData(
            CookedCharacterPackage package,
            BakedAnimationData baked,
            CharacterAnimationCatalog animationCatalog,
            GameObject rig,
            string packageId)
        {
            DestroyPreviewCatalog();
            _previewAnimationCatalog = animationCatalog;
            _previewRig = rig;
            var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(package, CharacterClass.None);
            Character = CharacterClass.None;
            SelectedPackageId = packageId;
            Def = definition;
            Baked = baked;
            WorkingDefs = definition.HurtboxBoneDefs != null ? (HurtboxBoneDef[])definition.HurtboxBoneDefs.Clone() : Array.Empty<HurtboxBoneDef>();
            DisplayDef = definition;
            AuthoritativePreview = true;
            PreviewStatus = "Authoritative";
            ShowHurtboxes = false;
            ShowHitboxes = true;
            ShowBakedBones = false;
            ShowDummy = false;
            SpawnRenderer();
            WorkingEvents = new Dictionary<string, HitboxEvent[]>();
            WorkingHitstopOverrides = new Dictionary<string, float>();
            _undo.Clear(); _redo.Clear();
            SetSlot(CanonicalSlotProjection.All[0]);
        }

        public void ApplyPreviewUnavailable(IReadOnlyList<CharacterDiagnostic> diagnostics)
        {
            DestroyRenderer();
            DestroyPreviewCatalog();
            _previewRig = null;
            Character = CharacterClass.None;
            SelectedPackageId = "";
            SelectedSlotId = "";
            Def = null;
            DisplayDef = null;
            Baked = null;
            WorkingDefs = Array.Empty<HurtboxBoneDef>();
            WorkingEvents = new Dictionary<string, HitboxEvent[]>();
            WorkingHitstopOverrides = new Dictionary<string, float>();
            AuthoritativePreview = false;
            PreviewStatus = "Preview unavailable";
            StageIndex = 0;
            Tick = 0;
            Playing = false;
            RefreshPose();
        }

        private void DestroyPreviewCatalog()
        {
            if (_previewAnimationCatalog == null) return;
#if UNITY_EDITOR
            if (!EditorUtility.IsPersistent(_previewAnimationCatalog))
                DestroyImmediate(_previewAnimationCatalog);
#else
            Destroy(_previewAnimationCatalog);
#endif
            _previewAnimationCatalog = null;
        }

        private void DestroyRenderer()
        {
            if (Renderer != null)
            {
                _weaponAttach?.Init(null, null);
                DestroyPreviewObject(Renderer.gameObject);
                Renderer = null;
            }
            if (_dummyRenderer != null)
            {
                _dummyWeaponAttach?.Init(null, null);
                DestroyPreviewObject(_dummyRenderer.gameObject);
                _dummyRenderer = null;
            }
        }

        private static void DestroyPreviewObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        public void MarkPreviewNonAuthoritative()
        {
            AuthoritativePreview = false;
            PreviewStatus = "Non-authoritative draft";
        }

        private static BakedAnimationData? LoadBaked(CharacterDefinition def)
        {
            if (def.Class == CharacterClass.FightGuy || string.IsNullOrEmpty(def.BakedDataPath)) return null;
            string? path = BakedContentPaths.ResolveBaked(def.BakedDataPath);
            if (path == null) return null;
            try { return BakedAnimationData.LoadFromBin(File.ReadAllBytes(path)); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AbilityLab] Failed to load baked data from {path}: {ex.Message}");
                return null;
            }
        }

        private static HurtboxBoneDef[] LoadWorkingDefs(CharacterDefinition def, BakedAnimationData? baked)
        {
            // Override file wins; else the shipped C# defs (cloned so edits never
            // touch the registry); else empty (capsule-only character — no bone edits).
            var overridePath = HurtboxOverride.OverridePathFor(def);
            if (overridePath != null && baked != null)
            {
                string? sysPath = BakedContentPaths.ResolveBaked(overridePath);
                if (sysPath != null)
                {
                    try
                    {
                        if (HurtboxOverride.TryParse(File.ReadAllText(sysPath), out _, out var parsed)
                            && parsed != null && HurtboxOverride.ValidateOrder(parsed, baked))
                        {
                            Debug.Log($"[AbilityLab] Loaded hurtbox override: {sysPath}");
                            return parsed;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AbilityLab] Failed to read hurtbox override {sysPath}: {ex.Message}");
                    }
                }
            }
            return def.HurtboxBoneDefs != null ? (HurtboxBoneDef[])def.HurtboxBoneDefs.Clone() : Array.Empty<HurtboxBoneDef>();
        }


        private void ClearPreviewSelection()
        {
#if UNITY_EDITOR
            if (Selection.activeGameObject == gameObject ||
                (Selection.activeTransform != null && Selection.activeTransform.IsChildOf(transform)))
                Selection.activeObject = gameObject;
#endif
        }

        private void SpawnRenderer()
        {
            ClearPreviewSelection();
            if (Renderer != null)
            {
                _weaponAttach?.Init(null, null);
                DestroyPreviewObject(Renderer.gameObject);
            }
            var go = new GameObject("LabCharacter");
            go.transform.SetParent(transform, false);
            Renderer = go.AddComponent<PlayerRenderer>();
            ConfigureRenderer(Renderer, DisplayDef, "LabCharacter");
            Renderer.transform.position = BasePosition();
            _weaponAttach = AttachWeapon(Renderer, DisplayDef);

            if (_dummyRenderer != null)
            {
                _dummyWeaponAttach?.Init(null, null);
                DestroyPreviewObject(_dummyRenderer.gameObject);
            }
            var dgo = new GameObject("LabDummy");
            dgo.transform.SetParent(transform, false);
            _dummyRenderer = dgo.AddComponent<PlayerRenderer>();
            ConfigureRenderer(_dummyRenderer, DisplayDef, "LabDummy");
            PositionDummy();
            _dummyRenderer.gameObject.SetActive(ShowDummy);
            _dummyWeaponAttach = AttachWeapon(_dummyRenderer, DisplayDef);
        }

        /// <summary>
        /// Attach the selected package's configured weapon prop to the preview model.
        /// Package mode reads the generated catalog binding; legacy compatibility mode
        /// retains the CharacterClass resource fallback.
        /// </summary>
        private WeaponAttach AttachWeapon(PlayerRenderer renderer, CharacterDefinition def)
        {
            var attach = renderer.GetComponent<WeaponAttach>();
            if (attach == null) attach = renderer.gameObject.AddComponent<WeaponAttach>();

            WeaponAttachConfig config = _previewAnimationCatalog != null
                ? _previewAnimationCatalog.WeaponConfig
                : def != null && def.Class != CharacterClass.None
                    ? Resources.Load<WeaponAttachConfig>($"WeaponConfigs/{def.Class}")
                    : null;
            attach.Init(renderer, config);
            return attach;
        }

        private void ConfigureRenderer(PlayerRenderer renderer, CharacterDefinition def, string name)
        {
            renderer.name = name;
            renderer.ModelYOffset = def.ModelYOffset;
            renderer.CapsuleRadius = def.CapsuleRadius;
            renderer.CapsuleHeight = def.CapsuleHeight;
            renderer.HurtboxBoneDefs = def.HurtboxBoneDefs;
            renderer.SetBakedData(Baked);
            renderer.SetAnimationCatalog(_previewAnimationCatalog);
            renderer.SetCharacterDefinition(def);
            renderer.LoadModel(def, _previewRig);
        }

        private Vector3 BasePosition() => transform.position + new Vector3(0f, Def.CapsuleHeight * 0.5f, 0f);
        private Vector3 DummyPosition()
            => transform.position + new Vector3(
                Mathf.Sin(FacingYaw) * DummyDistance,
                Def.CapsuleHeight * 0.5f,
                Mathf.Cos(FacingYaw) * DummyDistance);

        private void PositionDummy()
        {
            if (_dummyRenderer == null) return;
            _dummyRenderer.transform.position = DummyPosition();
        }

        // ── Ability accessors ──

        public AbilitySpec? CurrentSpec() => Def?.GetSlotAbility(SlotIndex, Airborne);


        /// <summary>
        /// Stage lookup without nullable-struct pitfalls (AttackStage? member access
        /// does not narrow after == null in Roslyn). Returns false when there is no
        /// selectable stage.
        /// </summary>
        public bool TryGetStage(out AttackStage stage)
        {
            stage = default;
            var spec = CurrentSpec();
            if (spec?.Stages == null || StageIndex < 0 || StageIndex >= spec.Stages.Length) return false;
            stage = spec.Stages[StageIndex];
            return true;
        }

        private static string AnimNameFor(AbilitySpec spec, int stageIndex)
            => spec.AnimationNames != null && stageIndex >= 0 && stageIndex < spec.AnimationNames.Length
                ? spec.AnimationNames[stageIndex] : "idle";

        // ── State setters (window/UI entry points — keep pose + selection coherent) ──

        public void SetAirborne(bool airborne)
        {
            if (Airborne == airborne) return;
            Airborne = airborne;
            UpdateSelectedSlotId();
            StageIndex = 0;
            Tick = 0;
            SelectedHitboxEventIndex = -1;
            RefreshPose();
        }

        public void SetSlot(SlotAddress address)
        {
            if (!CanonicalSlotProjection.TryGet(address.Id, out var canonical) || canonical != address)
                return;

            int labelIndex = Array.IndexOf(SlotNames, canonical.InputLabel);
            if (labelIndex < 0) return;
            Airborne = canonical.IsAirborne;
            SlotIndex = SlotIndices[labelIndex];
            SelectedSlotId = canonical.Id;
            StageIndex = 0;
            Tick = 0;
            SelectedHitboxEventIndex = -1;
            Playing = false;
            RefreshPose();
        }


        public void SetSlot(int slot)
        {
            if (SlotIndex == slot) return;
            SlotIndex = slot;
            UpdateSelectedSlotId();
            StageIndex = 0;
            Tick = 0;
            SelectedHitboxEventIndex = -1;
            RefreshPose();
        }

        private void UpdateSelectedSlotId()
        {
            if (!IsPackagePreview) return;
            int labelIndex = Array.IndexOf(SlotIndices, SlotIndex);
            if (labelIndex >= 0 && CanonicalSlotProjection.TryGet(Airborne, SlotNames[labelIndex], out var address))
                SelectedSlotId = address.Id;
        }

        public void SetStage(int stage)
        {
            StageIndex = stage;
            Tick = 0;
            SelectedHitboxEventIndex = -1;
            RefreshPose();
        }

        public void SelectHitbox(int index)
        {
            SelectedHitboxEventIndex = index >= 0 && index < CurrentWorkingEvents().Length ? index : -1;
            QueueEditorRefresh();
        }

        public void SetTick(ushort tick)
        {
            if (Tick == tick) return;
            Tick = tick;
            RefreshPose();
        }

        /// <summary>Override key for the current selection ("slot:airborne:stage").</summary>
        public string CurrentKey => $"{SlotIndex}:{(Airborne ? 1 : 0)}:{StageIndex}";
        /// <summary>Content ability name used for the current ability's authored parameters.</summary>
        public string CurrentAbilityProperty => ContentAbilityName(SlotIndex, Airborne);

        private static string ContentAbilityName(int slotIndex, bool airborne) => (slotIndex, airborne) switch
        {
            (0, false) => "lmb", (0, true) => "airLmb",
            (1, false) => "rmb", (1, true) => "airRmb",
            (2, false) => "slot1", (2, true) => "airSlot1",
            (3, false) => "e", (3, true) => "airE",
            (4, false) => "r", (4, true) => "airR",
            (5, false) => "f", (5, true) => "airF",
            (6, false) => "slot2", (6, true) => "airSlot2",
            (7, false) => "slot3", (7, true) => "airSlot3",
            (8, false) => "slot4", (8, true) => "airSlot4",
            (9, false) => "slot5", (9, true) => "airSlot5",
            (10, false) => "a", (10, true) => "airA",
            _ => throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "No ability content name for slot"),
        };

        private static bool TryParseStageKey(string key, out int slotIndex, out bool airborne, out int stageIndex)
        {
            slotIndex = -1;
            airborne = false;
            stageIndex = -1;
            string[] parts = key.Split(':');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out slotIndex)
                || (parts[1] != "0" && parts[1] != "1")
                || !int.TryParse(parts[2], out stageIndex)
                || slotIndex < 0 || slotIndex > 10 || stageIndex < 0)
            {
                slotIndex = -1;
                stageIndex = -1;
                return false;
            }
            airborne = parts[1] == "1";
            return true;
        }

        private static bool TryGetContentAbility(
            CharacterDefinition definition, string name, out AbilitySpec? ability)
        {
            ability = name switch
            {
                "lmb" => definition.LMB, "rmb" => definition.RMB,
                "airLmb" => definition.AirLMB, "airRmb" => definition.AirRMB,
                "slot1" => definition.Slot1, "airSlot1" => definition.AirSlot1,
                "e" => definition.E, "airE" => definition.AirE,
                "r" => definition.R, "airR" => definition.AirR,
                "f" => definition.F, "airF" => definition.AirF,
                "slot2" => definition.Slot2, "airSlot2" => definition.AirSlot2,
                "slot3" => definition.Slot3, "airSlot3" => definition.AirSlot3,
                "slot4" => definition.Slot4, "airSlot4" => definition.AirSlot4,
                "slot5" => definition.Slot5, "airSlot5" => definition.AirSlot5,
                "a" => definition.A, "airA" => definition.AirA,
                _ => null,
            };
            return ability != null;
        }

        /// <summary>
        /// Working hitstop override, authored ability parameter, or the simulation default.
        /// </summary>
        public float CurrentHitstopMultiplier
        {
            get
            {
                if (WorkingHitstopOverrides.TryGetValue(CurrentAbilityProperty, out float working))
                    return working;
                var spec = CurrentSpec();
                return spec?.Params != null && spec.Params.TryGetValue("hitstop_multiplier", out float authored)
                    ? authored : 1f;
            }
        }

        public void SetHitstopMultiplier(float multiplier)
        {
            if (!IsPackagePreview) return;
            float value = Mathf.Max(0f, multiplier);
            if (Mathf.Approximately(value, CurrentHitstopMultiplier)) return;
            PushSourceUndo();
            WorkingHitstopOverrides[CurrentAbilityProperty] = value;
            _trajDirty = "";
            RefreshPose();
        }


        /// <summary>
        /// The hitbox events that preview + timeline use: the working override for the
        /// current (slot, airborne, stage) when present, else the authored stage events.
        /// </summary>
        public HitboxEvent[] CurrentWorkingEvents()
        {
            if (WorkingEvents.TryGetValue(CurrentKey, out var events)) return events;
            return TryGetStage(out var stage) && stage.HitboxEvents != null ? stage.HitboxEvents : Array.Empty<HitboxEvent>();
        }

        // ── Pose resolution (the Shared functions the server uses) ──

        /// <summary>
        /// Mirror of the server's tick→baked-frame projection (SpawnHitbox /
        /// ResolveBoneAnimFrame): bakedFrame = min(tick * fc / durationTicks, fc-1),
        /// animation falls back to "idle" when missing from the bake.
        /// </summary>
        public bool ResolvePose(string animName, ushort durationTicks, out string resolvedAnim, out int bakedFrame)
        {
            resolvedAnim = animName;
            bakedFrame = 0;
            if (Baked == null) return false;
            int fc = Baked.FrameCountFor(animName);
            if (fc < 0) { resolvedAnim = "idle"; fc = Baked.FrameCountFor("idle"); }
            if (fc < 0) return false;
            bakedFrame = durationTicks > 0 ? Mathf.Min(Tick * fc / durationTicks, fc - 1) : Mathf.Min(Tick, fc - 1);
            return true;
        }

        public IReadOnlyList<SpellResolver.EntityData> ResolveHurtboxes()
        {
            _hurtboxes.Clear();
            if (Baked == null) return _hurtboxes; // no pose data at all

            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage)) return _hurtboxes;

            string animName = AnimNameFor(spec, StageIndex);
            if (!ResolvePose(animName, stage.DurationTicks, out string resolvedAnim, out int bakedFrame)) return _hurtboxes;

            var state = new CharacterState
            {
                PX = transform.position.x,
                PY = BasePosition().y,
                PZ = transform.position.z,
                FacingYaw = FacingYaw,
            };
            _hurtboxes.AddRange(ServerSimulation.BuildEntitiesFromState(state, DisplayDef, Baked, resolvedAnim, bakedFrame, 0));
            return _hurtboxes;
        }

        public IReadOnlyList<(int index, HitboxEvent evt, Vector3 start, Vector3 end)> ResolveHitboxes()
        {
            _hitboxes.Clear();
            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage)) return _hitboxes;

            var state = new CharacterState
            {
                PX = transform.position.x,
                PY = BasePosition().y,
                PZ = transform.position.z,
                FacingYaw = FacingYaw,
                AttackElapsedTicks = Tick,
            };
            var events = CurrentWorkingEvents();
            for (int i = 0; i < events.Length; i++)
            {
                var evt = events[i];
                if (Tick < evt.TriggerTick || Tick >= evt.TriggerTick + evt.DurationTicks) continue;
                HitboxGeometry.ResolvePositions(state, evt, Baked, DisplayDef,
                    spec.AnimationNames, (byte)StageIndex, (byte)SlotIndex, Airborne,
                    out float wx, out float wy, out float wz,
                    out float wex, out float wey, out float wez);
                _hitboxes.Add((i, evt, new Vector3(wx, wy, wz), new Vector3(wex, wey, wez)));
            }
            return _hitboxes;
        }

        public List<SpellResolver.EntityData> ResolveDummyHurtboxes()
        {
            var list = new List<SpellResolver.EntityData>();
            if (Baked == null || !ShowDummy) return list;
            int fc = Baked.FrameCountFor("idle");
            if (fc < 0) return list;
            var state = new CharacterState
            {
                PX = DummyPosition().x,
                PY = DummyPosition().y,
                PZ = DummyPosition().z,
                FacingYaw = FacingYaw + Mathf.PI,
            };
            list.AddRange(ServerSimulation.BuildEntitiesFromState(state, DisplayDef, Baked, "idle", 0, 0));
            return list;
        }

        /// <summary>
        /// Knockback arc for the previewed hitbox: launches the dummy victim (at the current
        /// TrajectoryPercent) with the hitbox's authored knockback through the REAL sim
        /// (Simulation.ApplyKnockback + ServerSimulation tick loop — the same flight law the
        /// move-data tool uses), and samples the path to landing. Recompute on input change;
        /// draw via <see cref="Trajectory"/> in OnRenderObject.
        /// </summary>
        public IReadOnlyList<(Vector3 pos, char phase)> ResolveTrajectory()
        {
            if (Def == null || Baked == null) { _trajectory.Clear(); return _trajectory; }
            var events = CurrentWorkingEvents();
            if (events.Length == 0 || PreviewHitboxIndex < 0 || PreviewHitboxIndex >= events.Length) { _trajectory.Clear(); return _trajectory; }
            var hit = events[PreviewHitboxIndex];
            if (hit.Knockback.Profile != KnockbackProfile.Custom) { _trajectory.Clear(); return _trajectory; } // custom-only (like the tool)

            // Cache: recompute only when a preview input (hitbox values, %, facing, dummy pos) changes.
            string key = $"{PreviewHitboxIndex}|{TrajectoryPercent:0.0}|{FacingYaw:0.000}|{DummyPosition():0.00}|{hit.Radius:0.00}|" +
                $"{hit.Damage:0.0}|{hit.StunTicks}|{hit.Knockback.Angle}|{hit.Knockback.BaseKnockback:0.0}|{hit.Knockback.KnockbackGrowth:0.0}";
            if (_trajectory.Count > 0 && key == _trajDirty) return _trajectory;
            _trajDirty = key;
            _trajectory.Clear();

            // Launch the dummy away from the attacker, along the attack's facing.
            float groundY = DummyPosition().y - Def.CapsuleHeight * 0.5f; // arena floor under the dummy's feet
            var state = new CharacterState
            {
                PX = DummyPosition().x,
                PY = DummyPosition().y,
                PZ = DummyPosition().z,
                IsGrounded = true,
                State = ActionState.Idle,
                FacingYaw = FacingYaw,
                DamagePercent = (ushort)(TrajectoryPercent + (int)hit.Damage), // post-hit, matches tool parity
            };
            float dirX = Mathf.Sin(FacingYaw), dirZ = Mathf.Cos(FacingYaw);
            SlopArena.Shared.Simulation.ApplyKnockback(ref state, dirX, dirZ, (sbyte)hit.Knockback.Angle,
                hit.Knockback.BaseKnockback, hit.Knockback.KnockbackGrowth,
                hit.Damage, hit.StunTicks, Def.Weight);

            var sim = new ServerSimulation(LabArena(groundY));
            sim.RegisterEntity(1, Def, state);
            var inputs = new Dictionary<ulong, InputState> { [1] = default };

            _trajectory.Add((new Vector3(state.PX, state.PY, state.PZ), 'H'));
            float maxPy = state.PY;
            bool apexMarked = false;
            for (int t = 0; t < 2400; t++)
            {
                sim.Tick(inputs);
                var s = sim.GetState(1);
                bool atApex = !apexMarked && s.PY <= maxPy && t > 0 && !s.IsGrounded && s.HitstunTicks == 0;
                if (s.PY > maxPy) maxPy = s.PY;
                else if (atApex) apexMarked = true;
                char phase = s.IsGrounded ? 'G'
                    : s.HitstunTicks > 0 ? 'H'
                    : atApex ? 'A' : 'F';
                _trajectory.Add((new Vector3(s.PX, s.PY, s.PZ), phase));
                if (s.IsGrounded) break;
            }
            return _trajectory;
        }

        /// <summary>A minimal flat arena for trajectory stepping (floor at the given world Y).</summary>
        private static ArenaDefinition LabArena(float floorY)
        {
            const int w = 100, h = 100;
            var data = new float[w * h];
            for (int i = 0; i < data.Length; i++) data[i] = floorY;
            return new ArenaDefinition
            {
                Name = "lab",
                DisplayName = "Ability Lab",
                KillHeight = floorY - 20f,
                SpawnPoints = new[] { new SpawnPoint { X = 0, Y = floorY, Z = 0, Yaw = 0 } },
                Heightmap = new ArenaHeightmap
                {
                    Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f,
                },
            };
        }

        // ── Scrub / edit ──

        /// <summary>
        /// Pose the mesh at the current tick using the game's playback mapping:
        /// clip progress = tick / DurationTicks (equivalent to the server's
        /// frameCount / DurationTicks speed). The dummy (when shown) holds idle frame 0.
        /// </summary>
        public void RefreshPose()
        {
            if (Renderer == null || Def == null)
            {
                QueueEditorRefresh();
                return;
            }
            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage))
            {
                QueueEditorRefresh();
                return;
            }
            float normalized = stage.DurationTicks > 0 ? (float)Tick / stage.DurationTicks : 0f;
            Renderer.PlayScrubbed(AnimNameFor(spec, StageIndex), normalized);
            _weaponAttach?.SetPreviewState((byte)(SlotIndex + 1), Tick);
            if (_dummyRenderer != null)
            {
                _dummyRenderer.gameObject.SetActive(ShowDummy);
                if (ShowDummy)
                {
                    _dummyRenderer.PlayScrubbed("idle", 0f);
                    PositionDummy();
                }
            }
            QueueEditorRefresh();
        }

        private void QueueEditorRefresh()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && IsPackagePreview)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
#endif
        }

        // ── Hitbox event editing (spec #119: add / remove / move / scale) ──
        public void SetSourceDocument(CharacterPackageSource source, bool clearHistory = false)
        {
            _sourceDocument = source ?? throw new ArgumentNullException(nameof(source));
            SelectedHitboxEventIndex = -1;
            if (clearHistory) { _undo.Clear(); _redo.Clear(); }
        }

        private void PushSourceUndo()
        {
            if (_sourceDocument == null) return;
            _undo.Push(_sourceDocument);
            if (_undo.Count > MaxUndoDepth) _undo.Pop();
            _redo.Clear();
        }

        public void UndoEvents()
        {
            if (_undo.Count == 0) return;
            if (_sourceDocument != null) _redo.Push(_sourceDocument);
            _sourceDocument = _undo.Pop();
            RefreshPose();
        }
        public void SetWorkingEvent(int index, HitboxEvent evt)
        {
            if (!IsPackagePreview) return;
            var events = (HitboxEvent[])CurrentWorkingEvents().Clone();
            if (index < 0 || index >= events.Length) return;
            events[index] = evt;
            WorkingEvents[CurrentKey] = events;
            RefreshPose();
        }
        public void AddWorkingEvent()
        {
            if (!IsPackagePreview) return;
            var events = (HitboxEvent[])CurrentWorkingEvents().Clone();
            var template = events.Length > 0 ? events[events.Length - 1] : default;
            var created = new HitboxEvent
            {
                TriggerTick = 1,
                DurationTicks = 10,
                Shape = HitboxShape.Sphere,
                Radius = template.Radius > 0f ? template.Radius : 0.4f,
                OffY = template.OffY,
                OffZ = template.OffZ > 0f ? template.OffZ : 1.0f,
                Damage = template.Damage,
                StunTicks = template.StunTicks,
                Interruptible = true,
                Knockback = template.Knockback,
            };
            var list = new List<HitboxEvent>(events) { created };
            PushSourceUndo();
            WorkingEvents[CurrentKey] = list.ToArray();
            RefreshPose();
        }

        public void RemoveWorkingEvent(int index)
        {
            if (!IsPackagePreview) return;
            var events = (HitboxEvent[])CurrentWorkingEvents().Clone();
            if (index < 0 || index >= events.Length) return;
            var list = new List<HitboxEvent>(events);
            list.RemoveAt(index);
            PushSourceUndo();
            WorkingEvents[CurrentKey] = list.ToArray();
            RefreshPose();
        }

        /// <summary>Discard unsaved edits — the preview reverts to the last-built data.</summary>
        public void RevertEdits()
        {
            WorkingEvents = new Dictionary<string, HitboxEvent[]>();
            WorkingHitstopOverrides = new Dictionary<string, float>();
            _undo.Clear();
            _redo.Clear();
            RefreshPose();
        }

        // ── Rendering (OnRenderObject → visible in Game view AND Scene view) ──

        private static Material _lineMat;
        private static Material LineMat
        {
            get
            {
                if (_lineMat == null)
                {
                    var shader = Shader.Find("Hidden/Internal-Colored");
                    _lineMat = shader != null ? new Material(shader) { hideFlags = HideFlags.HideAndDontSave } : null;
                }
                return _lineMat;
            }
        }

        private void OnRenderObject()
        {
            if ((!Application.isPlaying && !IsPackagePreview) || Def == null) return;
            var mat = LineMat;
            if (mat == null) return;
            mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            if (ShowHurtboxes)
            {
                var hurtboxes = ResolveHurtboxes();
                for (int i = 0; i < hurtboxes.Count; i++)
                {
                    var hb = hurtboxes[i];
                    GL.Color(new Color(0f, 1f, 0.35f));
                    WireSphere(new Vector3(hb.PosX, hb.PosY, hb.PosZ), hb.Radius);
                }
            }
            if (ShowHitboxes)
            {
                foreach (var (index, evt, start, end) in ResolveHitboxes())
                {
                    GL.Color(index == SelectedHitboxEventIndex
                        ? new Color(1f, 1f, 0.1f)
                        : new Color(1f, 0.45f, 0f));
                    if (evt.Shape == HitboxShape.Capsule) WireCapsule(start, end, evt.Radius);
                    else WireSphere(start, evt.Radius);
                }
            }
            if (ShowDummy)
            {
                GL.Color(new Color(1f, 0.35f, 0.35f));
                foreach (var hb in ResolveDummyHurtboxes())
                    WireSphere(new Vector3(hb.PosX, hb.PosY, hb.PosZ), hb.Radius);
            }
            if (ShowTrajectory)
            {
                var arc = ResolveTrajectory();
                if (arc.Count > 1)
                {
                    // Hitstun portion in cyan, post-hitstun flight in blue; apex marker white.
                    for (int i = 0; i < arc.Count - 1; i++)
                    {
                        char phase = arc[i].phase;
                        GL.Color(phase == 'H' ? new Color(0.2f, 0.9f, 0.9f)
                            : phase == 'A' ? new Color(1f, 1f, 1f)
                            : new Color(0.3f, 0.55f, 1f));
                        Line(arc[i].pos, arc[i + 1].pos);
                    }
                    // Landing point: red marker.
                    GL.Color(new Color(1f, 0.3f, 0.2f));
                    WireSphere(arc[^1].pos, 0.12f);
                }
            }
            if (ShowBakedBones)
                DrawBakedBonePoints();
            GL.End();
            GL.PopMatrix();
        }

        private static void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        private static void WireSphere(Vector3 c, float r)
        {
            const int segs = 16;
            for (int ring = 0; ring < 3; ring++)
            {
                for (int i = 0; i < segs; i++)
                {
                    float t0 = i * 2f * Mathf.PI / segs, t1 = (i + 1) * 2f * Mathf.PI / segs;
                    Vector3 a = c, b = c;
                    switch (ring)
                    {
                        case 0: a = c + new Vector3(Mathf.Cos(t0) * r, 0, Mathf.Sin(t0) * r); b = c + new Vector3(Mathf.Cos(t1) * r, 0, Mathf.Sin(t1) * r); break;
                        case 1: a = c + new Vector3(Mathf.Cos(t0) * r, Mathf.Sin(t0) * r, 0); b = c + new Vector3(Mathf.Cos(t1) * r, Mathf.Sin(t1) * r, 0); break;
                        default: a = c + new Vector3(0, Mathf.Cos(t0) * r, Mathf.Sin(t0) * r); b = c + new Vector3(0, Mathf.Cos(t1) * r, Mathf.Sin(t1) * r); break;
                    }
                    Line(a, b);
                }
            }
        }
        private void DrawBakedBonePoints()
        {
            if (Baked == null) return;

            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage)) return;

            string animName = AnimNameFor(spec, StageIndex);
            if (!ResolvePose(animName, stage.DurationTicks, out string resolvedAnim, out int bakedFrame)) return;

            float cos = Mathf.Cos(FacingYaw);
            float sin = Mathf.Sin(FacingYaw);
            float scale = DisplayDef?.HurtboxBoneScale ?? 1f;
            var state = new CharacterState { PX = transform.position.x, PY = BasePosition().y, PZ = transform.position.z };

            for (int i = 0; i < Baked.BoneNames.Length; i++)
            {
                string bone = Baked.BoneNames[i];
                if (!Baked.GetBonePosition(resolvedAnim, bakedFrame, i, out float x, out float y, out float z)) continue;

                bool weaponPoint = bone.StartsWith("_", StringComparison.Ordinal);
                float pointScale = weaponPoint ? 1f : scale;
                float wx = x * pointScale;
                float wy = y * pointScale;
                float wz = z * pointScale;
                var world = new Vector3(
                    state.PX + wx * cos + wz * sin,
                    DisplayDef.BoneYToWorldY(state.PY, wy),
                    state.PZ - wx * sin + wz * cos);

                GL.Color(bone == "_weapon_tip"
                    ? new Color(1f, 0.1f, 1f)
                    : bone == "_weapon_hilt"
                        ? new Color(1f, 1f, 0.1f)
                        : new Color(0.1f, 0.8f, 1f));
                WireSphere(world, weaponPoint ? 0.09f : 0.045f);
            }
        }

        private static void WireCapsule(Vector3 a, Vector3 b, float radius)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 0.0001f) { WireSphere(a, radius); return; }
            Vector3 dir = axis / len;
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.99f) up = Vector3.right;
            Vector3 right = Vector3.Cross(dir, up).normalized;
            Vector3 fwd = Vector3.Cross(right, dir).normalized;
            Vector3 a2 = a + dir * radius, b2 = b - dir * radius;
            const int segs = 12;
            for (int i = 0; i < segs; i++)
            {
                float t0 = i * 2f * Mathf.PI / segs, t1 = (i + 1) * 2f * Mathf.PI / segs;
                Vector3 p0 = a2 + (right * Mathf.Cos(t0) + fwd * Mathf.Sin(t0)) * radius;
                Vector3 p1 = a2 + (right * Mathf.Cos(t1) + fwd * Mathf.Sin(t1)) * radius;
                Line(p0, p1);
                Vector3 q0 = b2 + (right * Mathf.Cos(t0) + fwd * Mathf.Sin(t0)) * radius;
                Vector3 q1 = b2 + (right * Mathf.Cos(t1) + fwd * Mathf.Sin(t1)) * radius;
                Line(q0, q1);
                Line(p0, q0);
            }

            // The collision capsule includes hemispherical ends centered at the
            // resolved endpoints. The cylinder rings above are inset by one
            // radius, so draw the endpoint spheres too; without them the
            // wireframe appears shorter than the actual capsule.
            WireSphere(a, radius);
            WireSphere(b, radius);
        }
        private static string FormatDiagnostics(IReadOnlyList<CharacterDiagnostic> diagnostics)
            => string.Join("; ", diagnostics.Select(d => $"{d.Code} ({d.Path}): {d.Message}"));
    }
}
