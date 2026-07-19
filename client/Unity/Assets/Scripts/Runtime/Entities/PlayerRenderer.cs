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
        };

        [Header("Visual Offset")]
        [SerializeField] private float _modelYOffset;

        [Header("Bone Trail VFX")]
        [SerializeField] private GameObject _boneTrailPrefab;
        private Dictionary<string, ParticleSystem> _activeTrails = new();

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
        public void LoadModel(CharacterDefinition def)
        {
            if (string.IsNullOrEmpty(def.ModelResourcePath)) return;

            // Destroy existing model children
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }

            var prefab = Resources.Load<GameObject>(def.ModelResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[PlayerRenderer] Model not found at Resources/{def.ModelResourcePath}");
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
                    Debug.LogError($"[PlayerRenderer] AnimConfig not found at Resources/{path}");
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
        private ActionState _lastAnimState;
        private byte _lastAttackSlot;
        private byte _lastComboStage;
        private int _deathFlashTicks;
        private const int DeathFlashDuration = 6;
        private bool _wasGrounded = true;
        private bool _wasAttacking;
        private ClipExtrapolator? _activeExtrapolator;
        private ExtrapolationMode _currentExtrapolationMode;
        private AnimancerState? _currentAnimState;
        // ── Hitstun extrapolation tracking ──
        private bool _inHitstunAnim;
        private float _hitstunAnimStartTime;
        private float _hitstunAnimLength;
        private string _lastHitstunAnimName = "";
        // ── Frame-by-frame animation control ──

        private BakedAnimationData? _bakedData;

        /// <summary>
        /// Set baked skeleton data for frame-accurate animation.
        /// Must be called before first ApplyServerState.
        /// </summary>
        public void SetBakedData(BakedAnimationData? data) => _bakedData = data;

        // ── Lifecycle ──

        private void Awake()
        {
            if (_animancer == null)
                _animancer = GetComponent<AnimancerComponent>();
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
        }

        // ── Animancer-driven animation ──
        //
        private void UpdateAnimationState(CharacterState state)
        {
            if (_animancer == null || _charConfig == null)
                return;
            // Clear extrapolation on any state change — new clip starts
            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            _currentAnimState = null;

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
                // Re-hit detection: restart animation when HitstunTicks resets upward
                // (new hit) or on first entry into hitstun.
                bool newHit = _lastAnimState != ActionState.Hitstun
                    || state.HitstunTicks >= _lastState.HitstunTicks;
                if (newHit)
                {
                    var clip = _charConfig.GetClipByName(hitName);
                    if (clip != null)
                    {
                        _animancer.Play(clip, 0f);
                        _lastHitstunAnimName = hitName;
                        _inHitstunAnim = true;
                        _hitstunAnimStartTime = Time.time;
                        _hitstunAnimLength = clip.length;
                    }
                }
                _lastAnimState = ActionState.Hitstun;
                return;
            }

            // ── Hitstun extrapolation guard ──
            // Let the hitstun clip finish naturally before transitioning to idle/run.
            // Animancer non-looping clips hold last frame after finishing.
            if (_inHitstunAnim)
            {
                float elapsed = Time.time - _hitstunAnimStartTime;
                if (elapsed < _hitstunAnimLength)
                {
                    _lastAnimState = ActionState.Hitstun; // Keep trigger for next re-hit
                    return;
                }
                _inHitstunAnim = false;
            }

            // ── Non-combat: ground/air state machine ──
            if (!isCombat)
            {
                if (state.State == ActionState.JumpSquat)
                {
                    var clip = _charConfig.GetClipByName("jump");
                    if (clip != null)
                        _animancer.Play(clip, 0.05f);
                }
                else if (!state.IsGrounded)
                {
                    bool isAscending = state.VY > 0f;
                    bool wasAscending = _lastState.VY > 0f;

                    if (isAscending)
                    {
                        // Crossfade to jump on ascent start (initial jump or double jump)
                        if (!wasAscending)
                        {
                            var clip = _charConfig.GetClipByName("jump");
                            if (clip != null)
                                _animancer.Play(clip, 0.05f);
                        }
                    }
                    else
                    {
                        // Crossfade to fall on descent start
                        if (wasAscending)
                        {
                            var clip = _charConfig.GetClipByName("fall");
                            if (clip != null)
                                _animancer.Play(clip, 0.1f);
                        }
                    }
                }
                else
                {
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
                if (state.State == ActionState.Attacking)
                {
                    string animName = "spell_lmb_1";
                    float animSpeed = 1f;
                    if (_charDef != null)
                    {
                        byte slot = (byte)(state.AttackSlot - 1);
                        var spec = _charDef.GetSlotAbility(slot, !state.IsGrounded);
                        if (spec?.AnimationNames != null && state.ComboStage < spec.AnimationNames.Length)
                        {
                            animName = spec.GetAnimationName(state.ComboStage);
                            if (_bakedData != null && spec.Stages != null && state.ComboStage < spec.Stages.Length)
                            {
                                int bakedIdx = _bakedData.FindAnimIndex(animName);
                                if (bakedIdx >= 0)
                                {
                                    int frameCount = _bakedData.Animations[bakedIdx].FrameCount;
                                    int durationTicks = spec.Stages[state.ComboStage].DurationTicks;
                                    if (durationTicks > 0)
                                        animSpeed = (float)frameCount / durationTicks;
                                }
                            }
                            if (spec.AnimSpeed > 0f)
                                animSpeed = spec.AnimSpeed;
                        }
                    }

                    var clip = _charConfig.GetClipByName(animName);
                    if (clip != null)
                    {
                        var animState = _animancer.Play(clip, 0.05f);
                        animState.Speed = animSpeed;
                        _currentAnimState = animState;
                        _currentExtrapolationMode = ExtrapolationMode.None;
                        if (_charDef?.ClipOverrides != null)
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
                        if (_currentExtrapolationMode == ExtrapolationMode.None && _charConfig?.AbilityClips != null)
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
                    }
                }
                else if (state.State == ActionState.Dashing)
                {
                    var clip = _charConfig.GetClipByName("dash");
                    if (clip != null)
                        _animancer.Play(clip, 0f);
                }
            }

            _lastAnimState = state.State;
            _lastComboStage = state.ComboStage;
            _wasGrounded = state.IsGrounded;

            // ── Bone trail lifecycle ──
            bool isAttacking = state.State == ActionState.Attacking;
            bool slotChanged = isAttacking && _lastAttackSlot != state.AttackSlot;
            if ((isAttacking && !_wasAttacking || slotChanged) && _charDef != null)
            {
                // Transitioning into attack (or slot changed) — re-evaluate trails
                if (slotChanged)
                    DisableAllTrails();
                byte slot = (byte)(state.AttackSlot - 1);
                var spec = _charDef.GetSlotAbility(slot, !state.IsGrounded);
                if (spec?.BoneTrails != null)
                {
                    foreach (var def in spec.BoneTrails)
                    {
                        var ps = GetOrCreateBoneTrail(def.BoneName, def);
                        if (ps != null)
                        {
                            var main = ps.main;
                            main.startColor = new Color(def.R, def.G, def.B, def.A);
                            main.startSize = def.Width;
                            var emission = ps.emission;
                            emission.enabled = true;
                        }
                    }
                }
            }
            else if (!isAttacking && _wasAttacking)
            {
                // Transitioning away from attack — disable all trails
                DisableAllTrails();
            }
            _lastAttackSlot = state.AttackSlot;
            _wasAttacking = isAttacking;
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

        private ParticleSystem GetOrCreateBoneTrail(string boneName, BoneTrailDef def)
        {
            if (_activeTrails.TryGetValue(boneName, out var existing))
                return existing;
            var bone = FindBone(boneName);
            if (_boneTrailPrefab == null) return null;
            if (bone == null) return null;
            var trail = UnityEngine.Object.Instantiate(_boneTrailPrefab, bone).GetComponent<ParticleSystem>();
            trail.transform.localPosition = Vector3.zero;
            trail.transform.localRotation = Quaternion.identity;
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


        /// <summary>
        /// Reset animation state to defaults. Useful on respawn.
        /// </summary>
        public void ResetAnimationState()
        {
            _animancer.Stop();
            _wasGrounded = true;
            _lastState = default;
            _lastAnimState = default;
            _lastAttackSlot = 0;
            _lastComboStage = 0;
            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            _currentAnimState = null;
            _wasAttacking = false;
            DisableAllTrails();
            foreach (var kvp in _activeTrails)
            {
                var go = kvp.Value.gameObject;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(go);
                else
                    UnityEngine.Object.DestroyImmediate(go);
            }
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
            UnityEditor.Handles.Label(labelPos, $"{_entityName} [{_lastState.State}]");

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

        private Transform FindBone(string name)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
