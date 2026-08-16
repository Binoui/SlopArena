using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Entities;

namespace SlopArena.Client.Tools
{
    /// <summary>
    /// Ability Lab rig (spec #119): stateless frame-by-frame preview of a character's
    /// hurtboxes + hitboxes for any ability slot. No simulation tick loop — poses come
    /// from the baked skeleton .bin through the same Shared resolvers the server uses
    /// (BuildEntitiesFromState for hurtboxes, HitboxGeometry for hitboxes), so what you
    /// see is exactly what collides.
    ///
    /// Hitbox editing: WorkingEvents holds per-(slot, airborne, stage) HitboxEvent[]
    /// replacements, loaded from a per-character JSON next to the baked skeleton. The
    /// game server and training sim load that file at entity registration, so edits
    /// made here are the hitboxes that spawn in a real match. Hurtboxes are display-only.
    ///
    /// ExecuteAlways: the orbit camera works in edit mode too (frame the view before
    /// pressing Play). Pose/scrub/boxes are play-mode only.
    /// </summary>
    [ExecuteAlways]
    public class AbilityLab : MonoBehaviour
    {
        public const float TickRate = 60f; // sim ticks per second (matches bake sample rate)

        /// <summary>Slot display names, index-aligned with GetSlotAbility (ADR-0016 layout).</summary>
        public static readonly string[] SlotNames =
            { "LMB", "RMB", "1", "E", "R", "F", "2", "3", "4", "5", "A" };

        public static AbilityLab Instance { get; private set; }

        // ── Selection state ──
        public CharacterClass Character { get; private set; } = CharacterClass.None;
        public int SlotIndex { get; private set; }
        public bool Airborne { get; private set; }
        public int StageIndex { get; private set; }
        public ushort Tick { get; private set; }
        public bool Playing { get; set; }
        public float PlaySpeed { get; set; } = 1f;
        public float FacingYaw { get; set; }
        public bool ShowHurtboxes { get; set; } = true;
        public bool ShowHitboxes { get; set; } = true;
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
        /// <summary>Every bone in the baked skeleton (mixamorig set) — the attachable bone options.</summary>
        public string[] BakedBoneNames => Baked?.BoneNames ?? Array.Empty<string>();
        public PlayerRenderer Renderer { get; private set; } = null!;
        public HurtboxBoneDef[] WorkingDefs { get; private set; } = Array.Empty<HurtboxBoneDef>();

        /// <summary>Per-(slot, airborne, stage) hitbox event edits (key = "slot:airborne:stage").</summary>
        public Dictionary<string, HitboxEvent[]> WorkingEvents { get; private set; } = new();

        /// <summary>Target .cs file for Save — the character's data source (src/Shared/Characters).</summary>
        public string? SourceFilePath { get; private set; }

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

        // ── Undo / redo (per-edit snapshots of WorkingEvents) ──
        private const int MaxUndoDepth = 50;
        private readonly Stack<Dictionary<string, HitboxEvent[]>> _undo = new();
        private readonly Stack<Dictionary<string, HitboxEvent[]>> _redo = new();
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        private void Awake()
        {
            Instance = this;
            EnsureCamera();
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }

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

        public void LoadCharacter(CharacterClass character)
        {
            if (character == CharacterClass.None || character == Character) return;
            Character = character;
            Def = CharacterRegistry.Get(character);
            Baked = LoadBaked(Def);
            WorkingDefs = LoadWorkingDefs(Def, Baked);
            DisplayDef = HurtboxOverride.Apply(Def, WorkingDefs);
            WorkingEvents = new Dictionary<string, HitboxEvent[]>();
            SourceFilePath = ResolveSourceFilePath(Character);

            SpawnRenderer();
            SlotIndex = 0;
            Airborne = false;
            StageIndex = 0;
            Tick = 0;
            Playing = false;
            _undo.Clear();
            _redo.Clear();
            RefreshPose();
        }

        private static BakedAnimationData? LoadBaked(CharacterDefinition def)
        {
            if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
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

        /// <summary>
        /// The character's C# data source: &lt;repo&gt;/src/Shared/Characters/&lt;Class&gt;Data.cs.
        /// Repo root = three levels above Unity's Assets folder.
        /// </summary>
        private static string? ResolveSourceFilePath(CharacterClass character)
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string file = Path.Combine(repoRoot, "src", "Shared", "Characters", character + "Data.cs");
            return File.Exists(file) ? file : null;
        }

        private void SpawnRenderer()
        {
            if (Renderer != null)
            {
                _weaponAttach?.Init(null, null); // free the previous character's weapon instances
                Destroy(Renderer.gameObject);
            }
            var go = new GameObject("LabCharacter");
            go.transform.SetParent(transform, false);
            Renderer = go.AddComponent<PlayerRenderer>();
            ConfigureRenderer(Renderer, DisplayDef, "LabCharacter");
            Renderer.transform.position = BasePosition();
            if (Application.isPlaying) _weaponAttach = AttachWeapon(Renderer, DisplayDef);

            if (_dummyRenderer != null)
            {
                _dummyWeaponAttach?.Init(null, null);
                Destroy(_dummyRenderer.gameObject);
            }
            var dgo = new GameObject("LabDummy");
            dgo.transform.SetParent(transform, false);
            _dummyRenderer = dgo.AddComponent<PlayerRenderer>();
            ConfigureRenderer(_dummyRenderer, DisplayDef, "LabDummy");
            PositionDummy();
            _dummyRenderer.gameObject.SetActive(ShowDummy);
            if (Application.isPlaying) _dummyWeaponAttach = AttachWeapon(_dummyRenderer, DisplayDef);
        }

        /// <summary>
        /// Attach the character's weapon prop (Resources/WeaponConfigs/&lt;Class&gt;.asset) to the
        /// preview model so blade reach is visible while tuning hitboxes. No-op without a config.
        /// </summary>
        private static WeaponAttach AttachWeapon(PlayerRenderer renderer, CharacterDefinition def)
        {
            var attach = renderer.GetComponent<WeaponAttach>();
            if (attach == null) attach = renderer.gameObject.AddComponent<WeaponAttach>();
            attach.Init(renderer, Resources.Load<WeaponAttachConfig>($"WeaponConfigs/{def.Class}"));
            return attach;
        }

        private static void ConfigureRenderer(PlayerRenderer renderer, CharacterDefinition def, string name)
        {
            renderer.name = name;
            renderer.ModelYOffset = def.ModelYOffset;
            renderer.CapsuleRadius = def.CapsuleRadius;
            renderer.CapsuleHeight = def.CapsuleHeight;
            renderer.HurtboxBoneDefs = def.HurtboxBoneDefs;
            renderer.SetBakedData(null); // baked pose comes from the tool's own resolve, not renderer playback
            renderer.SetCharacterDefinition(def);
            renderer.LoadModel(def);
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
            StageIndex = 0;
            Tick = 0;
            RefreshPose();
        }

        public void SetSlot(int slot)
        {
            if (SlotIndex == slot) return;
            SlotIndex = slot;
            StageIndex = 0;
            Tick = 0;
            RefreshPose();
        }

        public void SetStage(int stage)
        {
            if (StageIndex == stage) return;
            StageIndex = stage;
            Tick = 0;
            RefreshPose();
        }

        public void SetTick(ushort tick)
        {
            if (Tick == tick) return;
            Tick = tick;
            RefreshPose();
        }

        /// <summary>Override key for the current selection ("slot:airborne:stage").</summary>
        public string CurrentKey => CSharpCharacterWriter.Key(SlotIndex, Airborne, StageIndex);

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
            if (Renderer == null || Def == null) return;
            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage)) return;
            float normalized = stage.DurationTicks > 0 ? (float)Tick / stage.DurationTicks : 0f;
            Renderer.PlayScrubbed(AnimNameFor(spec, StageIndex), normalized);
            if (_dummyRenderer != null)
            {
                _dummyRenderer.gameObject.SetActive(ShowDummy);
                if (ShowDummy)
                {
                    _dummyRenderer.PlayScrubbed("idle", 0f);
                    PositionDummy();
                }
            }
        }

        // ── Hitbox event editing (spec #119: add / remove / move / scale) ──

        private void PushEventUndo()
        {
            var snapshot = new Dictionary<string, HitboxEvent[]>(WorkingEvents.Count);
            foreach (var kvp in WorkingEvents)
                snapshot[kvp.Key] = (HitboxEvent[])kvp.Value.Clone();
            _undo.Push(snapshot);
            if (_undo.Count > MaxUndoDepth) _undo.Pop();
            _redo.Clear();
        }

        private void StoreWorkingEvents(Dictionary<string, HitboxEvent[]> events)
        {
            WorkingEvents = events;
            RefreshPose();
        }

        public void UndoEvents()
        {
            if (_undo.Count == 0) return;
            var snapshot = new Dictionary<string, HitboxEvent[]>(WorkingEvents.Count);
            foreach (var kvp in WorkingEvents)
                snapshot[kvp.Key] = (HitboxEvent[])kvp.Value.Clone();
            _redo.Push(snapshot);
            StoreWorkingEvents(_undo.Pop());
        }

        public void RedoEvents()
        {
            if (_redo.Count == 0) return;
            var snapshot = new Dictionary<string, HitboxEvent[]>(WorkingEvents.Count);
            foreach (var kvp in WorkingEvents)
                snapshot[kvp.Key] = (HitboxEvent[])kvp.Value.Clone();
            _undo.Push(snapshot);
            StoreWorkingEvents(_redo.Pop());
        }

        public void SetWorkingEvent(int index, HitboxEvent evt)
        {
            var events = (HitboxEvent[])CurrentWorkingEvents().Clone();
            if (index < 0 || index >= events.Length) return;
            events[index] = evt;
            PushEventUndo();
            WorkingEvents[CurrentKey] = events;
            RefreshPose();
        }

        public void AddWorkingEvent()
        {
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
            PushEventUndo();
            WorkingEvents[CurrentKey] = list.ToArray();
            RefreshPose();
        }

        public void RemoveWorkingEvent(int index)
        {
            var events = (HitboxEvent[])CurrentWorkingEvents().Clone();
            if (index < 0 || index >= events.Length) return;
            var list = new List<HitboxEvent>(events);
            list.RemoveAt(index);
            PushEventUndo();
            WorkingEvents[CurrentKey] = list.ToArray();
            RefreshPose();
        }

        // ── Persistence: write edits back into the C# data source (the compiled
        //    source of truth — no JSON, no mirror; rebuild Shared to apply) ──

        public void SaveToSource()
        {
            if (SourceFilePath == null || !File.Exists(SourceFilePath))
            {
                Debug.LogWarning($"[AbilityLab] Character data file not found: {SourceFilePath ?? "<unknown>"}");
                return;
            }
            if (WorkingEvents.Count == 0)
            {
                Debug.LogWarning("[AbilityLab] No hitbox edits to save.");
                return;
            }
            string text;
            try
            {
                text = File.ReadAllText(SourceFilePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AbilityLab] Failed to read {SourceFilePath}: {ex.Message}");
                return;
            }
            foreach (var kvp in WorkingEvents)
            {
                if (!CSharpCharacterWriter.TryParseKey(kvp.Key, out int slot, out bool airborne, out int stage))
                {
                    Debug.LogError($"[AbilityLab] Cannot save: malformed edit key '{kvp.Key}'.");
                    return;
                }
                string property = CSharpCharacterWriter.PropertyName(slot, airborne);
                if (!CSharpCharacterWriter.TryReplaceHitboxEvents(text, property, stage, kvp.Value, out text))
                {
                    Debug.LogError($"[AbilityLab] Cannot save: no stage {stage} in {property} for {Character}. Aborted — file not written.");
                    return;
                }
            }
            try
            {
                File.WriteAllText(SourceFilePath, text);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AbilityLab] Failed to write {SourceFilePath}: {ex.Message}");
                return;
            }
            Debug.Log($"[AbilityLab] Saved {WorkingEvents.Count} edited stage(s) to {SourceFilePath}\n" +
                      "Apply: run `dotnet build src/Shared/` (DLL auto-copies to Unity), then restart the match.");
        }

        /// <summary>Discard unsaved edits — the preview reverts to the last-built data.</summary>
        public void RevertEdits()
        {
            WorkingEvents = new Dictionary<string, HitboxEvent[]>();
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
            if (!Application.isPlaying || Def == null) return;
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
                GL.Color(new Color(1f, 0.45f, 0f));
                foreach (var (_, evt, start, end) in ResolveHitboxes())
                {
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
        }
    }
}
