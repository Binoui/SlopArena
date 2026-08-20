using System;
using SlopArena.Shared;
using UnityEngine;
using System.Collections.Generic;
using Animancer;

using SlopArena.Client.Animation;


namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Visual representation of a CharacterState.
    /// Applies server-authoritative position, rotation, and drives
    /// animation state on an Animator. Works for both player and NPC entities.
    /// </summary>
    public class PlayerRenderer : MonoBehaviour
    {
        [Header("Entity")]
        [SerializeField] private string _entityName = "Entity";
        [SerializeField] private ulong _entityId;

        [Header("Animation")]
        [SerializeField] private AnimancerComponent _animancer;

        [SerializeField] private CharacterAnimationConfig _charConfig;

        [Header("Thresholds")]
        [SerializeField] private float _runSpeedThreshold = 0.1f;

        [Header("Debug Visualization")]
        [SerializeField] private float _capsuleRadius = 0.6f;
        [SerializeField] private float _capsuleHeight = 1.3f;

        public float CapsuleRadius
        {
            get => _capsuleRadius;
            set => _capsuleRadius = value;
        }
        public float CapsuleHeight
        {
            get => _capsuleHeight;
            set => _capsuleHeight = value;
        }
        private HurtboxBoneDef[] _hurtboxBoneDefs = System.Array.Empty<HurtboxBoneDef>();

        // ── State color mapping for gizmo visualization ──

        private static readonly Dictionary<ActionState, Color> StateColors = new()
        {
            { ActionState.Idle,       new Color(1f, 1f, 1f, 0.8f) },       // white
            { ActionState.Dashing,    new Color(0f, 1f, 1f, 0.8f) },       // cyan
            { ActionState.Hitstun,    new Color(1f, 0.2f, 0.2f, 0.8f) },   // red
            { ActionState.Sliding,    new Color(0.5f, 0.5f, 0.5f, 0.8f) }, // gray
            { ActionState.Attacking,  new Color(1f, 0.6f, 0f, 0.8f) },     // orange
            { ActionState.AirDodging, new Color(1f, 0.8f, 0f, 0.8f) },     // yellow
            { ActionState.JumpSquat,  new Color(0.8f, 0.3f, 1f, 0.8f) },   // purple
            { ActionState.Aiming,     new Color(0.3f, 1f, 1f, 0.8f) },     // cyan-light
            { ActionState.Run,        new Color(0f, 1f, 1f, 0.8f) },       // cyan
            { ActionState.LedgeHang,  new Color(1f, 0.8f, 0f, 0.8f) },     // yellow
        };
        private static readonly AnimationCurve AttackAccentWidth = new(
            new Keyframe(0f, 0f),
            new Keyframe(0.28f, 1f),
            new Keyframe(0.72f, 0.72f),
            new Keyframe(1f, 0f));

        private sealed class AttackAccentTrail
        {
            public GameObject Root;
            public TrailRenderer Primary;
            public TrailRenderer Secondary;
        }


        [Header("Visual Offset")]
        [SerializeField] private float _modelYOffset;

        [Header("Bone Trail VFX")]
        [SerializeField] private GameObject _boneTrailPrefab;
        private Dictionary<string, ParticleSystem> _activeTrails = new();
        private Dictionary<string, AttackAccentTrail> _attackAccentTrails = new();

        /// <summary>
        /// Y offset to align the visual model's feet with the collision capsule bottom.
        /// Set from CharacterDefinition.ModelYOffset (≈ -0.52 for Manki).
        /// </summary>

        /// <summary>
        /// Character definition for ability animation lookups.
        /// Set from TrainingMatch at spawn.
        /// </summary>
        private CharacterDefinition? _charDef;
        private GameObject _modelInstance;

        public void SetCharacterDefinition(CharacterDefinition? def) => _charDef = def;
        private ArenaCollision.BlastLines _blastLines;
        private bool _hasBlastLines;

        public void SetBlastLines(ArenaDefinition arena)
        {
            _blastLines = ArenaCollision.ResolveBlastLines(in arena);
            _hasBlastLines = true;
        }

        private Vector3 KnockoutLimitPosition(CharacterState state)
        {
            Vector3 position = new(state.PX, state.PY + _modelYOffset, state.PZ);
            if (!_hasBlastLines)
                return position;

            if (position.x < _blastLines.KillMinX)
                position.x = _blastLines.KillMinX;
            else if (position.x > _blastLines.KillMaxX)
                position.x = _blastLines.KillMaxX;
            if (position.z < _blastLines.KillMinZ)
                position.z = _blastLines.KillMinZ;
            else if (position.z > _blastLines.KillMaxZ)
                position.z = _blastLines.KillMaxZ;
            if (position.y < _blastLines.KillHeight + _modelYOffset)
                position.y = _blastLines.KillHeight + _modelYOffset;
            else if (position.y > _blastLines.KillTop + _modelYOffset)
                position.y = _blastLines.KillTop + _modelYOffset;
            return position;
        }

        /// <summary>Character definition (capsule geometry, weight, baked bones) — set at spawn.</summary>
        public CharacterDefinition? CharacterDef => _charDef;

        /// <summary>Set animation config from MatchBase (drag-drop), skipping Resources.Load fallback.</summary>
        public void SetAnimationConfig(CharacterAnimationConfig config) => _charConfig = config;
        private StatusBillboard _billboard;

        /// <summary>
        /// Initialize the world-space status billboard (damage% + entity name).
        /// Safe to call multiple times — no-op after first init.
        /// </summary>
        public void InitBillboard(Func<ulong, CharacterState> getState, ulong entityId)
        {
            if (_billboard == null)
            {
                _billboard = gameObject.AddComponent<StatusBillboard>();
                _billboard.Init(this, getState, entityId);
            }
        }

        public float ModelYOffset
        {
            get => _modelYOffset;
            set => _modelYOffset = value;
        }

        /// <summary>
        /// Load the 3D model for this character from Resources.
        /// Destroys any existing model child and instantiates the new one.
        /// Must be called before ApplyServerState.
        /// </summary>
        /// <param name="def">Character definition with ModelResourcePath as fallback.</param>
        /// <param name="prefabOverride">If non-null, use this prefab instead of Resources.Load.</param>
        public void LoadModel(CharacterDefinition def, GameObject prefabOverride = null)
        {
            if (prefabOverride == null && string.IsNullOrEmpty(def.ModelResourcePath)) return;

            // Destroy existing model children
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }

            // Use Unity's null-aware operator (!=): a broken/missing reference is
            // "fake-null" (C#-non-null but Unity-null), and `??` would NOT fall
            // through to Resources.Load for it — silently skipping the model.
            var prefab = prefabOverride != null
                ? prefabOverride
                : Resources.Load<GameObject>(def.ModelResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[PlayerRenderer] Model not found — override null and Resources/{def.ModelResourcePath} missing");
                return;
            }

            var instance = Instantiate(prefab, transform);
            instance.name = def.Class.ToString();
            _modelInstance = instance;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            instance.transform.localScale = Vector3.one * def.VisualScale;

            _animancer = instance.GetComponent<AnimancerComponent>();
            if (_animancer == null)
                _animancer = instance.AddComponent<AnimancerComponent>();

            // Auto-load CharacterAnimationConfig by class convention if not wired in Inspector
            if (_charConfig == null)
            {
                string path = $"AnimationConfigs/{def.Class}_AnimConfig";
                _charConfig = Resources.Load<CharacterAnimationConfig>(path);
                if (_charConfig == null)
                {
                    // Placeholder characters borrow another class's prefab (e.g. Nilus and Kistu
                    // both ship ModelResourcePath = "Characters/FightGuy"). The model IS that
                    // class's model, so its clips are the right ones to borrow — without this
                    // the applier early-returns on a null config and the character T-poses.
                    string modelClass = def.ModelResourcePath.Substring(
                        def.ModelResourcePath.LastIndexOf('/') + 1);
                    string fallbackPath = $"AnimationConfigs/{modelClass}_AnimConfig";
                    if (modelClass.Length > 0 && fallbackPath != path)
                        _charConfig = Resources.Load<CharacterAnimationConfig>(fallbackPath);
                    if (_charConfig != null)
                        Debug.Log($"[PlayerRenderer] {def.Class} has no AnimConfig — borrowing {modelClass} clips from Resources/{fallbackPath} (placeholder art)");
                    else
                        Debug.LogError($"[PlayerRenderer] AnimConfig not found at Resources/{path} (model fallback Resources/{fallbackPath} missing too)");
                }
            }
        }

        /// <summary>Hurtbox bone definitions for debug visualization.</summary>
        public HurtboxBoneDef[] HurtboxBoneDefs
        {
            get => _hurtboxBoneDefs;
            set => _hurtboxBoneDefs = value;
        }

        /// <summary>Entity display name (settable in Inspector).</summary>
        public string EntityName
        {
            get => _entityName;
            set => _entityName = value;
        }

        /// <summary>Server-assigned entity ID.</summary>
        public ulong EntityId
        {
            get => _entityId;
            set => _entityId = value;
        }

        /// <summary>
        /// Last applied action state. Read by weapon attach components.
        /// </summary>
        public ActionState CurrentActionState => _lastAnimState;

        /// <summary>
        /// Last applied attack slot (1-6). 0 when not attacking. Read by weapon attach components.
        /// </summary>
        public byte CurrentAttackSlot => _lastAttackSlot;


        /// <summary>Expose the Animator for external access (e.g. VFX hooks).</summary>
        public Animator Animator => _animancer != null ? _animancer.Animator : null;

        // ── Tracking state for change detection ──

        private CharacterState _lastState;
        private bool _jumpArcActive;
        private ActionState _lastAnimState;
        private byte _lastAttackSlot;
        private byte _lastComboStage;
        private int _deathFlashTicks;
        private const int DeathFlashDuration = 6;
        private bool _wasGrounded = true;
        private bool _wasAttacking;
        private bool _attackAccentsEnabled;
        private bool _attackStartedGrounded;
        private bool _hasAppliedState;
        private byte _lastPresentedDeaths;
        private bool _hasPresentedDeaths;
        private MovementFeedbackEffect _dashTrail;
        private MovementFeedbackEffect _respawnInvincibilityEffect;
        private bool _respawnInvincibilityActive;
        private ulong _activeAccentWindowSignature;
        private ClipExtrapolator? _activeExtrapolator;
        private ExtrapolationMode _currentExtrapolationMode;
        private AnimancerState? _currentAnimState;
        // ── Hitstun extrapolation tracking ──
        private bool _inHitstunAnim;
        private float _hitstunAnimStartTime;
        private float _hitstunAnimLength;
        private string _lastHitstunAnimName = "";
        // ── Hitstop freeze tracking (ADR-0012) ──
        // True while the current clip is paused at Speed 0 for a hitstop freeze;
        // _hitstopPausedSpeed holds the pre-freeze speed for the resume frame.
        private bool _hitstopPaused;
        private float _hitstopPausedSpeed = 1f;
        // ── Frame-by-frame animation control ──

        private BakedAnimationData? _bakedData;

        /// <summary>
        /// Set baked skeleton data for frame-accurate animation.
        /// Must be called before first ApplyServerState.
        /// </summary>
        public void SetBakedData(BakedAnimationData? data) => _bakedData = data;

        /// <summary>
        /// Play a clip at a fixed normalized time with zero speed — Ability Lab frame
        /// scrubbing (spec #119). Normalized time matches the game's playback mapping
        /// (clip progress = tick / stage duration, speed = frameCount / DurationTicks),
        /// so the scrubbed pose is exactly the in-game pose at that tick. A missing
        /// clip is a no-op (the previous pose stays).
        /// </summary>
        public void PlayScrubbed(string clipName, float normalizedTime)
        {
            if (_animancer == null || _charConfig == null) return;
            var clip = _charConfig.GetClipByName(clipName);
            if (clip == null) return;
            var state = _animancer.Play(clip, 0f);
            state.Time = clip.length > 0f ? normalizedTime * clip.length : 0f;
            state.Speed = 0f;
        }

        // ── Lifecycle ──

        private void Awake()
        {
            if (_animancer == null)
                _animancer = GetComponent<AnimancerComponent>();
            MovementFeedbackEffect.Prewarm();
        }

        private void OnDisable()
        {
            if (_dashTrail != null)
            {
                _dashTrail.EndDashTrail();
                _dashTrail = null;
            }
            if (_respawnInvincibilityEffect != null)
            {
                _respawnInvincibilityEffect.EndInvincibility();
                _respawnInvincibilityEffect = null;
            }
            _respawnInvincibilityActive = false;
        }
        private void Start()
        {
            if (_boneTrailPrefab == null)
            {
                _boneTrailPrefab = Resources.Load<GameObject>("VFX/BoneTrail");
                if (_boneTrailPrefab == null)
                    Debug.LogWarning("[PlayerRenderer] BoneTrail prefab not found at Resources/VFX/BoneTrail");
            }
        }


        // ── Public API ──

        /// <summary>
        /// Apply a server-authoritative CharacterState to this entity.
        /// Updates position, rotation, and drives animation state.
        /// Call from Update() after reconciliation.
        /// </summary>
        public void ApplyServerState(CharacterState state)
        {
            Vector3 targetPos = new Vector3(state.PX, state.PY + _modelYOffset, state.PZ);

            bool stockTransition = _hasAppliedState
                && state.Deaths != _lastState.Deaths
                && (!_hasPresentedDeaths || state.Deaths != _lastPresentedDeaths);
            if (stockTransition)
            {
                PresentStockTransition(_lastState, state, targetPos);
                _lastPresentedDeaths = state.Deaths;
                _hasPresentedDeaths = true;
            }

            if (state.HitstunTicks > 0)
            {
                // Smooth knockback: lerp toward server position
                transform.position = Vector3.Lerp(transform.position, targetPos, 0.6f);
            }
            else
            {
                // Normal movement: snap directly (60Hz is fast enough)
                transform.position = targetPos;
            }
            UpdateDashFeedback(state, targetPos);
            if (!stockTransition)
                EmitMovementFeedback(state, targetPos);
            UpdateRespawnInvincibility(state, targetPos);

            transform.rotation = Quaternion.Euler(0f, state.FacingYaw * Mathf.Rad2Deg, 0f);
            // Death flash blink
            if (_deathFlashTicks > 0)
            {
                _deathFlashTicks--;
                if (_modelInstance != null)
                {
                    var mr = _modelInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (mr != null)
                        mr.enabled = (_deathFlashTicks % 2 == 0);
                }
            }
            else
            {
                if (_modelInstance != null)
                {
                    var mr = _modelInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (mr != null && !mr.enabled)
                        mr.enabled = true;
                }
            }

            UpdateAnimationState(state);

            _lastState = state;
            _hasAppliedState = true;
            if (!_hasPresentedDeaths)
            {
                _lastPresentedDeaths = state.Deaths;
                _hasPresentedDeaths = true;
            }
        }

        private void PresentStockTransition(
            CharacterState previous, CharacterState respawned, Vector3 respawnPosition)
        {
            Vector3 deathDirection = new(previous.KVX, previous.KVY, previous.KVZ);
            if (deathDirection.sqrMagnitude < 0.01f)
                deathDirection = new(previous.VX, previous.VY, previous.VZ);
            if (deathDirection.sqrMagnitude < 0.01f)
                deathDirection = Vector3.up;

            MovementFeedbackEffect.Spawn(
                KnockoutLimitPosition(previous), deathDirection, MovementFeedbackKind.Knockout);
            OnDeath();

            if (respawned.InvincibilityTicks == 0)
                return;

            MovementFeedbackEffect.SpawnRespawn(
                respawnPosition, Vector3.up, RespawnColor());
            _respawnInvincibilityEffect?.EndInvincibility();
            _respawnInvincibilityEffect = MovementFeedbackEffect.BeginInvincibility(
                respawnPosition, RespawnColor());
            _respawnInvincibilityActive = _respawnInvincibilityEffect != null;
        }

        private void UpdateRespawnInvincibility(CharacterState state, Vector3 position)
        {
            if (!_respawnInvincibilityActive)
                return;
            if (state.InvincibilityTicks == 0)
            {
                _respawnInvincibilityEffect?.EndInvincibility();
                _respawnInvincibilityEffect = null;
                _respawnInvincibilityActive = false;
                return;
            }
            _respawnInvincibilityEffect?.FollowInvincibility(position);
        }

        private Color RespawnColor() => _charDef?.Class switch
        {
            CharacterClass.Manki => new Color(1f, 0.36f, 0.08f, 0.95f),
            CharacterClass.FightGuy => new Color(0.28f, 0.72f, 1f, 0.95f),
            CharacterClass.Kistu => new Color(0.76f, 0.9f, 1f, 0.95f),
            CharacterClass.Nilus => new Color(0.72f, 0.36f, 1f, 0.95f),
            _ => Color.white,
        };

        private void UpdateDashFeedback(CharacterState state, Vector3 position)
        {
            if (state.State != ActionState.Dashing)
            {
                if (_dashTrail != null)
                {
                    _dashTrail.EndDashTrail();
                    _dashTrail = null;
                }
                return;
            }

            Vector3 dashDirection = new(state.DashDirX, 0f, state.DashDirZ);
            if (dashDirection.sqrMagnitude < 0.001f)
                dashDirection = new Vector3(state.VX, 0f, state.VZ);
            Vector3 trailDirection = -dashDirection;
            if (_dashTrail == null)
                _dashTrail = MovementFeedbackEffect.BeginDashTrail(position, trailDirection);
            else
                _dashTrail.FollowDashTrail(position, trailDirection);
        }

        private void EmitMovementFeedback(CharacterState state, Vector3 position)
        {
            if (!_hasAppliedState)
            {
                _hasAppliedState = true;
                return;
            }



            bool tookOff = _lastState.IsGrounded && !state.IsGrounded;
            bool doubleJumped = !_lastState.IsGrounded
                && !state.IsGrounded
                && state.JumpsLeft < _lastState.JumpsLeft;
            if (doubleJumped)
            {
                MovementFeedbackEffect.Spawn(position, Vector3.down, MovementFeedbackKind.DoubleJump);
            }
            else if (tookOff && state.HitstunTicks == 0)
            {
                MovementFeedbackEffect.Spawn(position, Vector3.down, MovementFeedbackKind.Jump);
            }

            if (!_lastState.IsGrounded && state.IsGrounded)
            {
                float landingSpeed = Mathf.Max(0f, -_lastState.VY);
                MovementFeedbackKind kind = landingSpeed >= 24f
                    ? MovementFeedbackKind.HeavyLanding
                    : MovementFeedbackKind.Landing;
                MovementFeedbackEffect.Spawn(position, Vector3.up, kind);
            }

            if (_charDef != null && !state.IsGrounded && state.VY < 0f)
            {
                float fastFallSpeed = _charDef.Movement.FastFallSpeed;
                bool isFastFall = Mathf.Abs(state.VY + fastFallSpeed) <= 0.25f;
                bool wasFastFall = Mathf.Abs(_lastState.VY + fastFallSpeed) <= 0.25f;
                if (isFastFall && !wasFastFall)
                    MovementFeedbackEffect.Spawn(position, Vector3.up, MovementFeedbackKind.FastFall);
            }

            Vector3 knockback = new(state.KVX, state.KVY, state.KVZ);
            Vector3 previousKnockback = new(_lastState.KVX, _lastState.KVY, _lastState.KVZ);
            const float launchSpeed = 12f;
            if (knockback.sqrMagnitude >= launchSpeed * launchSpeed
                && previousKnockback.sqrMagnitude < launchSpeed * launchSpeed)
            {
                MovementFeedbackEffect.Spawn(position, -knockback, MovementFeedbackKind.Launch);
            }
        }

        // ── Animancer-driven animation ──
        //
        /// <summary>
        /// Compute animation playback speed so that <c>frameCount</c> frames from
        /// baked data play in exactly <c>durationTicks</c> ticks (at 60 Hz).
        /// Returns 1f when baked data or the named animation is unavailable.
        /// </summary>
        private float GetAnimSpeedFromDuration(string animName, int durationTicks)
        {
            if (_bakedData == null || durationTicks <= 0) return 1f;
            int bakedIdx = _bakedData.FindAnimIndex(animName);
            if (bakedIdx < 0) return 1f;
            int frameCount = _bakedData.Animations[bakedIdx].FrameCount;
            return (float)frameCount / durationTicks;
        }

        /// <summary>
        /// Play the ability clip for a combo stage with baked-frame speed and
        /// extrapolation overrides. Returns true when a clip played.
        /// </summary>
        private bool PlayAbilityAnim(CharacterState state, AbilitySpec spec, int comboStage)
        {
            if (spec == null)
                return false;

            string animName = "spell_lmb_1";
            float animSpeed = 1f;
            if (spec.AnimationNames != null && comboStage < spec.AnimationNames.Length)
            {
                animName = spec.GetAnimationName(comboStage);
                if (_bakedData != null && spec.Stages != null && comboStage < spec.Stages.Length)
                {
                    int bakedIdx = _bakedData.FindAnimIndex(animName);
                    if (bakedIdx >= 0)
                    {
                        int frameCount = _bakedData.Animations[bakedIdx].FrameCount;
                        int durationTicks = spec.Stages[comboStage].DurationTicks;
                        if (durationTicks > 0)
                            animSpeed = (float)frameCount / durationTicks;
                    }
                }
                if (spec.AnimSpeed > 0f)
                    animSpeed = spec.AnimSpeed;
            }

            var clip = _charConfig.GetClipByName(animName);
            if (clip == null)
                return false;

            var animState = _animancer.Play(clip, 0.05f);
            animState.Speed = animSpeed;
            _currentAnimState = animState;
            _currentExtrapolationMode = ExtrapolationMode.None;
            // Extrapolation is driven entirely by baked skeleton frames. Characters
            // with no BakedDataPath (placeholders: Nilus, Kistu) have none, and
            // FromBakedData dereferences its argument — so gate on it here.
            if (_bakedData != null && _charDef?.ClipOverrides != null)
            {
                foreach (var cfg in _charDef.ClipOverrides)
                {
                    if (cfg.Name == animName && cfg.Extrapolation == ExtrapolationMode.Continuous)
                    {
                        _currentExtrapolationMode = ExtrapolationMode.Continuous;
                        _activeExtrapolator = ClipExtrapolator.FromBakedData(_bakedData, animName);
                        break;
                    }
                }
            }
            if (_bakedData != null && _currentExtrapolationMode == ExtrapolationMode.None
                && _charConfig?.AbilityClips != null)
            {
                foreach (var entry in _charConfig.AbilityClips)
                {
                    if (entry.Name == animName && entry.Extrapolation == ExtrapolationMode.Continuous)
                    {
                        _currentExtrapolationMode = ExtrapolationMode.Continuous;
                        _activeExtrapolator = ClipExtrapolator.FromBakedData(_bakedData, animName);
                        break;
                    }
                }
            }
            return true;
        }

        private void UpdateAnimationState(CharacterState state)
        {
            if (_animancer == null || _charConfig == null)
                return;
            if (state.State != ActionState.Attacking && _attackAccentsEnabled)
                DisableAttackAccents();
            // Clear extrapolation on any state change — new clip starts
            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            _currentAnimState = null;

            // ── Hitstop (ADR-0012): freeze the animation in place — the pose holds at the
            // impact frame for the freeze's duration. The server timeline pauses
            // symmetrically (TickTimers, ability ticks), so the same paused clip resumes
            // in sync when the freeze pops — no clip restart, no combo-extension artifact.
            if (state.HitstopTicks > 0)
            {
                if (!_hitstopPaused)
                {
                    // DIAG: hitstop lifecycle — correlate with in-game hit reactions.
                    Debug.Log($"[Hitstop] freeze START ent={_entityId} ticks={state.HitstopTicks} " +
                        $"dmg={state.DamagePercent} prevDmg={_lastState.DamagePercent} " +
                        $"clip={CurrentClipName()} level={state.HitstunLevel}");
                    _hitstopPaused = true;
                    if (_animancer.States.Current is { } frozen)
                    {
                        _hitstopPausedSpeed = frozen.Speed;
                        frozen.Speed = 0f;
                    }
                }
                else if (_animancer.States.Current is { } fs)
                {
                    fs.Speed = 0f;
                }
                return; // nothing may change during the freeze
            }
            if (_hitstopPaused)
            {
                _hitstopPaused = false;
                // DIAG: hitstop lifecycle — freeze popped, queued launch applied.
                Debug.Log($"[Hitstop] freeze END ent={_entityId} hs={state.HitstunTicks} " +
                    $"st={state.State} clip={CurrentClipName()}");
                if (_animancer.States.Current is { } resumed)
                    resumed.Speed = _hitstopPausedSpeed;
            }

            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;

            // ── Combat (Attacking / Dashing / Hitstun) ──
            bool isCombat = state.State == ActionState.Attacking
                || state.State == ActionState.Dashing
                || state.State == ActionState.Hitstun;

            // ── Hitstun animation (always, even during extrapolation guard) ──
            // Separate from stateChanged gate to handle re-hits during guard period.
            if (state.State == ActionState.Hitstun)
            {
                string hitName = state.HitstunLevel switch
                {
                    1 => "hit_medium",
                    2 => "hit_hard",
                    _ => "hit_small"
                };
                // Re-hit detection: restart the clip on first entry into hitstun OR
                // whenever HitstunTicks does not follow the natural 1-tick countdown.
                // ResolveHits OVERWRITES HitstunTicks with the new hit's raw StunTicks,
                // so a re-hit while the victim is mid-hitstun can send it DOWN (shorter-
                // stun follow-up); the old ">= last" check missed those and the clip
                // played only once per string.
                _jumpArcActive = false;
                bool naturalCountdown = _lastAnimState == ActionState.Hitstun
                    && _lastState.HitstunTicks > 0
                    && state.HitstunTicks == _lastState.HitstunTicks - 1;
                bool newHit = !naturalCountdown;
                if (newHit)
                {
                    var clip = _charConfig.GetClipByName(hitName);
                    if (clip != null)
                    {
                        float hsAnimSpeed = GetAnimSpeedFromDuration(hitName, state.HitstunTicks);
                        var hsState = _animancer.Play(clip, 0f);
                        hsState.Speed = hsAnimSpeed;
                        _lastHitstunAnimName = hitName;
                        _inHitstunAnim = true;
                        _hitstunAnimStartTime = Time.time;
                        _hitstunAnimLength = clip.length / hsAnimSpeed;
                        // DIAG: hitstun clip restart — which clip, why, and the
                        // tick delta vs the previous applied state.
                        Debug.Log($"[HitstunAnim] RESTART ent={_entityId} clip={hitName} " +
                            $"ticks={state.HitstunTicks} prevTicks={_lastState.HitstunTicks} " +
                            $"level={state.HitstunLevel} firstEntry={_lastAnimState != ActionState.Hitstun} " +
                            $"speed={hsAnimSpeed:F2}");
                    }
                }
                _lastAnimState = ActionState.Hitstun;
                return;
            }

            // ── Hitstun extrapolation guard ──
            // Let the hitstun clip finish naturally before transitioning — but
            // release immediately if the server state has moved on (e.g. Dashing).
            if (_inHitstunAnim)
            {
                if (state.State == ActionState.Hitstun)
                {
                    float elapsed = Time.time - _hitstunAnimStartTime;
                    if (elapsed < _hitstunAnimLength)
                    {
                        _lastAnimState = ActionState.Hitstun; // Keep trigger for next re-hit
                        return;
                    }
                }
                _inHitstunAnim = false;
            }

            // ── Non-combat: ground/air state machine ──
            if (!isCombat)
            {
                // Fixed-stance aim holds (AimedProjectile/Projectile behaviors) play the
                // aim-loop once on entering the hold; mobile aimers (Kistu) fall through
                // to locomotion below.
                bool fixedAimHold = state.State == ActionState.Aiming && state.AttackSlot > 0 && _charDef != null
                    && _charDef.GetSlotAbility(state.AttackSlot - 1, !state.IsGrounded)?.Behavior is AbilityBehavior.AimedProjectile or AbilityBehavior.Projectile;
                if (fixedAimHold)
                {
                    if (state.State != _lastAnimState)
                        PlayAbilityAnim(state, _charDef.GetSlotAbility(state.AttackSlot - 1, !state.IsGrounded), 0);
                }
                else if (state.State == ActionState.JumpSquat)
                {
                    var clip = _charConfig.GetClipByName("jump_up");
                    if (clip != null)
                    {
                        _animancer.Play(clip, 0.05f);
                        _jumpArcActive = true;
                    }
                }
                else if (state.State == ActionState.LedgeHang)
                {
                    // Placeholder pose: no hang anim asset yet (ADR-0020 §4) — play fall.
                    var clip = _charConfig.GetClipByName("fall");
                    if (clip != null && _animancer.States.Current?.Clip != clip)
                        _animancer.Play(clip, 0.1f);
                }
                else if (!state.IsGrounded)
                {
                    bool isAscending = state.VY > 0f;
                    bool wasAscending = _lastState.VY > 0f;

                    // Detect double jump: JumpsLeft decreased this tick
                    bool doubleJumped = state.JumpsLeft < _lastState.JumpsLeft;

                    if (isAscending)
                    {
                        // New ascent: initial jump, or double jump from descent
                        if (!wasAscending)
                        {
                            // Only play jump_up if it's a fresh jump (not recovery from attack hitstun)
                            if (_jumpArcActive || doubleJumped)
                            {
                                var clip = _charConfig.GetClipByName("jump_up");
                                if (clip != null)
                                {
                                    _animancer.Play(clip, 0.05f);
                                    _jumpArcActive = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Descending: slow crossfade from jump_up / attack pose → fall
                        if (wasAscending && _jumpArcActive)
                        {
                            // Fresh descent from a jump — slow transition to fall
                            var clip = _charConfig.GetClipByName("fall");
                            if (clip != null)
                                _animancer.Play(clip, 2f); // Very slow crossfade — blends jump_up into fall
                            _jumpArcActive = false;
                        }
                        else if (!_jumpArcActive)
                        {
                            // Already descending (after attack, after jump_down expired, etc.)
                            // Ensure fall is eventually playing
                            var fallClip = _charConfig.GetClipByName("fall");
                            if (fallClip != null && _animancer.States.Current?.Clip != fallClip)
                                _animancer.Play(fallClip, 2f);
                        }
                    }
                }
                else
                {
                    _jumpArcActive = false;
                    var clip = hSpeed > _runSpeedThreshold
                        ? _charConfig.GetClipByName("run")
                        : _charConfig.GetClipByName("idle");
                    if (clip != null)
                        _animancer.Play(clip, 0.1f);
                }
            }

            // ── Combat state changes (excluding hitstun — handled above) ──
            bool stateChanged = state.State != _lastAnimState
                || (state.State == ActionState.Attacking && (
                    state.AttackSlot != _lastAttackSlot || state.ComboStage != _lastComboStage));

            if (isCombat && stateChanged)
            {
                _jumpArcActive = false;
                if (state.State == ActionState.Attacking)
                {
                    if (_charDef != null)
                    {
                        byte slot = (byte)(state.AttackSlot - 1);
                        var spec = _charDef.GetSlotAbility(slot, !state.IsGrounded);
                        PlayAbilityAnim(state, spec, state.ComboStage);
                    }
                }
                else if (state.State == ActionState.Dashing)
                {
                    var clip = _charConfig.GetClipByName("dash");
                    if (clip != null && _charDef != null)
                    {
                        float dashAnimSpeed = GetAnimSpeedFromDuration("dash", _charDef.Movement.DashDurationTicks);
                        var dashState = _animancer.Play(clip, 0f);
                        dashState.Speed = dashAnimSpeed;
                    }
                }
            }

            byte prevComboStage = _lastComboStage;
            _lastComboStage = state.ComboStage;
            _wasGrounded = state.IsGrounded;

            // ── Attack accent lifecycle ──
            UpdateAttackAccents(state, prevComboStage);
            _lastAttackSlot = state.AttackSlot;
            _wasAttacking = state.State == ActionState.Attacking;
            _lastAnimState = state.State;
        }

        private void LateUpdate()
        {
            if (_activeExtrapolator != null && _currentAnimState != null
                && _currentExtrapolationMode == ExtrapolationMode.Continuous
                && _currentAnimState.IsPlaying)
            {
                float currentTime = (float)_currentAnimState.Time;
                float clipLength = (float)_currentAnimState.Length;
                if (currentTime > clipLength)
                {
                    float extraTime = currentTime - clipLength;
                    Transform rootBone = FindBone("mixamorig:Hips");
                    if (rootBone != null)
                        _activeExtrapolator.Apply(rootBone, extraTime);
                }
            }
        }

        private void UpdateAttackAccents(CharacterState state, byte prevComboStage)
        {
            bool isAttacking = state.State == ActionState.Attacking;
            bool slotChanged = isAttacking && _lastAttackSlot != state.AttackSlot;
            bool stageChanged = isAttacking && prevComboStage != state.ComboStage;
            bool attackStarted = isAttacking
                && (_lastState.State != ActionState.Attacking
                    || slotChanged
                    || state.AttackElapsedTicks < _lastState.AttackElapsedTicks);
            if (attackStarted)
                _attackStartedGrounded = state.IsGrounded;

            bool isFightGuyNormal = isAttacking
                && _charDef?.Class == CharacterClass.FightGuy
                && IsNormalAttackSlot(state.AttackSlot);
            if (!isFightGuyNormal)
            {
                if (_attackAccentsEnabled)
                    DisableAttackAccents();
                UpdateLegacyBoneTrails(state, slotChanged, stageChanged);
                return;
            }

            byte slot = (byte)(state.AttackSlot - 1);
            var spec = _charDef.GetSlotAbility(slot, airborne: !_attackStartedGrounded);
            if (spec?.Stages == null || spec.Stages.Length == 0)
            {
                DisableAttackAccents();
                return;
            }

            int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
            var stage = spec.Stages[stageIdx];
            var trails = stage.BoneTrails ?? spec.BoneTrails;
            ulong activeWindowSignature = GetActiveWindowSignature(
                state.AttackElapsedTicks, stage.HitboxEvents);
            if (activeWindowSignature == 0 || trails == null || trails.Length == 0)
            {
                if (_attackAccentsEnabled)
                    DisableAttackAccents();
                if (slotChanged || stageChanged)
                    DisableAllTrails();
                return;
            }

            if (_attackAccentsEnabled
                && _activeAccentWindowSignature == activeWindowSignature
                && !slotChanged
                && !stageChanged
                && !attackStarted)
                return;
            if (_attackAccentsEnabled)
                DisableAttackAccents();
            DisableAllTrails();

            float activeDamage = GetMaxActiveDamage(
                state.AttackElapsedTicks, stage.HitboxEvents);
            bool heavy = activeDamage >= 12f;
            bool secondaryAssigned = false;
            foreach (var trailDef in trails)
            {
                if (!IsTrailActive(
                    trailDef.BoneName, state.AttackElapsedTicks, stage.HitboxEvents))
                    continue;

                var accent = GetOrCreateAttackAccent(trailDef.BoneName);
                if (accent == null) continue;

                bool emitSecondary = heavy && !secondaryAssigned;
                ConfigureAttackAccent(accent, trailDef, activeDamage, emitSecondary);
                secondaryAssigned |= emitSecondary;
                _attackAccentsEnabled = true;
            }
            _activeAccentWindowSignature = activeWindowSignature;
        }

        private void UpdateLegacyBoneTrails(CharacterState state, bool slotChanged, bool stageChanged)
        {
            bool isAttacking = state.State == ActionState.Attacking;
            if ((isAttacking && !_wasAttacking || slotChanged || stageChanged) && _charDef != null)
            {
                if (slotChanged || stageChanged)
                    DisableAllTrails();

                byte slot = (byte)(state.AttackSlot - 1);
                var spec = _charDef.GetSlotAbility(slot, !state.IsGrounded);
                if (spec?.Stages == null || spec.Stages.Length == 0) return;

                int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
                var stage = spec.Stages[stageIdx];
                var trails = stage.BoneTrails ?? spec.BoneTrails;
                if (trails == null) return;

                foreach (var trailDef in trails)
                {
                    var ps = GetOrCreateBoneTrail(trailDef.BoneName);
                    if (ps == null) continue;

                    var main = ps.main;
                    main.startColor = new Color(trailDef.R, trailDef.G, trailDef.B, trailDef.A);
                    main.startSize = trailDef.Width;
                    var emission = ps.emission;
                    emission.enabled = true;
                }
            }
            else if (!isAttacking && _wasAttacking)
            {
                DisableAllTrails();
            }
        }
        private static bool IsNormalAttackSlot(byte attackSlot)
            => attackSlot == AbilitySlots.Slot1
                || attackSlot == AbilitySlots.Slot2
                || attackSlot == AbilitySlots.Slot3
                || attackSlot == AbilitySlots.Slot4;


        private static ulong GetActiveWindowSignature(
            ushort elapsedTicks, HitboxEvent[] events)
        {
            if (events == null) return 0;
            ulong signature = 0;
            for (int i = 0; i < events.Length; i++)
            {
                if (!IsHitboxActive(elapsedTicks, events[i])) continue;
                signature |= 1UL << (i & 63);
            }
            return signature;
        }

        private static float GetMaxActiveDamage(
            ushort elapsedTicks, HitboxEvent[] events)
        {
            float maxDamage = 0f;
            if (events == null) return maxDamage;
            foreach (var hitbox in events)
            {
                if (IsHitboxActive(elapsedTicks, hitbox))
                    maxDamage = Math.Max(maxDamage, hitbox.Damage);
            }
            return maxDamage;
        }

        private static bool IsTrailActive(
            string boneName, ushort elapsedTicks, HitboxEvent[] events)
        {
            if (events == null) return false;
            foreach (var hitbox in events)
            {
                if (!IsHitboxActive(elapsedTicks, hitbox)) continue;
                if (string.IsNullOrEmpty(hitbox.BoneName)
                    || hitbox.BoneName == boneName
                    || hitbox.EndBoneName == boneName)
                    return true;
            }
            return false;
        }

        private static bool IsHitboxActive(ushort elapsedTicks, HitboxEvent hitbox)
        {
            int endExclusive = hitbox.TriggerTick + hitbox.DurationTicks;
            return elapsedTicks >= hitbox.TriggerTick && elapsedTicks < endExclusive;
        }


        private static void ConfigureAttackAccent(
            AttackAccentTrail accent,
            BoneTrailDef def,
            float damage,
            bool emitSecondary)
        {
            bool heavy = damage >= 12f;
            bool medium = !heavy && damage >= 7f;
            float widthScale = heavy ? 1.8f : medium ? 1.25f : 0.75f;
            float lifetime = heavy ? 0.16f : medium ? 0.12f : 0.08f;
            var color = new Color(def.R, def.G, def.B, def.A);

            accent.Primary.widthMultiplier = def.Width * widthScale;
            accent.Primary.time = lifetime;
            accent.Primary.startColor = new Color(color.r, color.g, color.b, color.a * 0.08f);
            accent.Primary.endColor = new Color(color.r, color.g, color.b, color.a * 0.9f);
            accent.Primary.Clear();
            accent.Primary.emitting = true;

            accent.Secondary.emitting = false;
            accent.Secondary.Clear();
            if (!emitSecondary) return;

            accent.Secondary.transform.localPosition =
                new Vector3(def.Width * 0.45f, 0f, 0f);
            accent.Secondary.widthMultiplier = def.Width * widthScale * 0.38f;
            accent.Secondary.time = lifetime * 0.7f;
            accent.Secondary.startColor =
                new Color(color.r, color.g, color.b, color.a * 0.03f);
            accent.Secondary.endColor =
                new Color(color.r, color.g, color.b, color.a * 0.45f);
            accent.Secondary.emitting = true;
        }

        private AttackAccentTrail GetOrCreateAttackAccent(string boneName)
        {
            if (_attackAccentTrails.TryGetValue(boneName, out var existing))
                return existing;
            var bone = FindBone(boneName);
            if (_boneTrailPrefab == null || bone == null) return null;
            var sourceRenderer = _boneTrailPrefab.GetComponent<ParticleSystemRenderer>();
            if (sourceRenderer == null || sourceRenderer.sharedMaterial == null) return null;

            var root = new GameObject($"AttackAccent_{boneName}");
            root.transform.SetParent(bone, false);
            var primary = CreateAttackAccentTrail(
                root, sourceRenderer.sharedMaterial);

            var secondaryObject = new GameObject("HeavyFragment");
            secondaryObject.transform.SetParent(root.transform, false);
            var secondary = CreateAttackAccentTrail(
                secondaryObject, sourceRenderer.sharedMaterial);

            var accent = new AttackAccentTrail
            {
                Root = root,
                Primary = primary,
                Secondary = secondary,
            };
            _attackAccentTrails[boneName] = accent;
            return accent;
        }

        private static TrailRenderer CreateAttackAccentTrail(
            GameObject owner, Material material)
        {
            var trail = owner.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.widthCurve = AttackAccentWidth;
            trail.minVertexDistance = 0.025f;
            trail.numCornerVertices = 3;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.generateLightingData = false;
            trail.autodestruct = false;
            trail.emitting = false;
            return trail;
        }

        private ParticleSystem GetOrCreateBoneTrail(string boneName)
        {
            if (_activeTrails.TryGetValue(boneName, out var existing))
                return existing;
            var bone = FindBone(boneName);
            if (_boneTrailPrefab == null) return null;
            if (bone == null) return null;
            var trail = UnityEngine.Object.Instantiate(_boneTrailPrefab, bone).GetComponent<ParticleSystem>();
            trail.transform.localPosition = Vector3.zero;
            trail.transform.localRotation = Quaternion.identity;

            trail.Play();
            var emission = trail.emission;
            emission.enabled = false;

            _activeTrails[boneName] = trail;
            return trail;
        }

        private void DisableAllTrails()
        {
            foreach (var kvp in _activeTrails)
            {
                var emission = kvp.Value.emission;
                emission.enabled = false;
            }
        }

        private void DisableAttackAccents()
        {
            foreach (var kvp in _attackAccentTrails)
            {
                kvp.Value.Primary.emitting = false;
                kvp.Value.Primary.Clear();
                kvp.Value.Secondary.emitting = false;
                kvp.Value.Secondary.Clear();
            }
            _attackAccentsEnabled = false;
            _activeAccentWindowSignature = 0;
        }


        /// <summary>
        /// Reset animation state to defaults. Useful on respawn.
        /// </summary>
        public void ResetAnimationState()
        {
            _animancer.Stop();
            _wasGrounded = true;
            _lastAnimState = default;
            _lastAttackSlot = 0;
            _lastComboStage = 0;
            _jumpArcActive = false;

            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            _currentAnimState = null;
            _hitstopPaused = false;
            _wasAttacking = false;
            _attackStartedGrounded = false;
            DisableAttackAccents();
            DisableAllTrails();
            foreach (var kvp in _activeTrails)
            {
                var go = kvp.Value.gameObject;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(go);
                else
                    UnityEngine.Object.DestroyImmediate(go);
            }
            foreach (var kvp in _attackAccentTrails)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(kvp.Value.Root);
                else
                    UnityEngine.Object.DestroyImmediate(kvp.Value.Root);
            }
            _attackAccentTrails.Clear();
            _activeTrails.Clear();
        }

        /// <summary>
        /// Called when the entity dies (void death detected via Deaths counter change).
        /// Triggers a brief visual flash + animation reset.
        /// </summary>
        public void OnDeath()
        {
            _deathFlashTicks = DeathFlashDuration;
            ResetAnimationState();
        }

        // ── Gizmos ──

        private void OnDrawGizmos()
        {
            // ── Lookup color for current state ──
            Color stateColor = StateColors.GetValueOrDefault(_lastState.State, Color.white);

            // ── Entity name label ──
            Gizmos.color = Color.white;
            Vector3 labelPos = transform.position + Vector3.up * 2.5f;
#if UNITY_EDITOR
            // UnityEditor.Handles is editor-only; the guard keeps the player
            // build compiling (release blocker found 2026-08-02).
            UnityEditor.Handles.Label(labelPos, $"{_lastState.State}");
#endif

            // ── State color indicator sphere (above label) ──
            Gizmos.color = stateColor;
            //Gizmos.DrawSphere(transform.position + Vector3.up * 3.2f, 0.25f);

            // ── Collision capsule ──
            Gizmos.color = stateColor;
            Vector3 capCenter = transform.position - Vector3.up * _modelYOffset;
            float halfH = Mathf.Max(_capsuleHeight * 0.5f - _capsuleRadius, 0f);
            Vector3 top = capCenter + Vector3.up * halfH;
            Vector3 bot = capCenter - Vector3.up * halfH;
        }

        /// <summary>DIAG: name of the currently playing clip, or "none".</summary>
        private string CurrentClipName()
            => _animancer?.States.Current?.Clip != null
                ? _animancer.States.Current.Clip.name
                : "none";

        private Transform FindBone(string name)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
