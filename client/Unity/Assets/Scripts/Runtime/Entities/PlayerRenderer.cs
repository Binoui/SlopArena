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
        public void InitBillboard(ServerSimulation sim, ulong entityId)
        {
            if (_billboard == null)
            {
                _billboard = gameObject.AddComponent<StatusBillboard>();
                _billboard.Init(this, sim, entityId);
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
        // All states play AnimationClips directly via AnimancerComponent.Play().
        // No AnimatorController, no triggers, no blend tree.
        // Movement uses simple idle/run crossfade (no linear mixer needed for
        // a platform fighter where speed is a threshold, not a smooth gradient).

        private void UpdateAnimationState(CharacterState state)
        {
            if (_animancer == null || _charConfig == null)
                return;

            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;

            // ── Combat (Attacking / Dashing / Hitstun) ──
            bool isCombat = state.State == ActionState.Attacking
                || state.State == ActionState.Dashing
                || state.State == ActionState.Hitstun;

            // ── Non-combat: ground/air state machine ──
            if (!isCombat)
            {
                if (state.State == ActionState.JumpSquat && _lastAnimState != ActionState.JumpSquat)
                {
                    // JumpSquat entry — play jump
                    var clip = _charConfig.GetClipByName("jump");
                    if (clip != null)
                        _animancer.Play(clip, 0.05f);
                }
                else if (!state.IsGrounded)
                {
                    // Airborne (fall / post-jump) — play fall
                    var clip = _charConfig.GetClipByName("fall");
                    if (clip != null)
                        _animancer.Play(clip, 0.1f);
                }
                else
                {
                    // Grounded — idle/run crossfade
                    var clip = hSpeed > _runSpeedThreshold
                        ? _charConfig.GetClipByName("run")
                        : _charConfig.GetClipByName("idle");
                    if (clip != null)
                        _animancer.Play(clip, 0.1f);
                }
            }

            // Double jump: upward impulse while airborne (overrides fall)
            if (!state.IsGrounded && state.VY > 2f && _lastState.VY <= 0f && state.State != ActionState.JumpSquat)
            {
                var clip = _charConfig.GetClipByName("jump");
                if (clip != null)
                    _animancer.Play(clip, 0.05f);
            }

            // ── Combat state changes ──
            bool stateChanged = state.State != _lastAnimState
                || (state.State == ActionState.Attacking && (
                    state.AttackSlot != _lastAttackSlot || state.ComboStage != _lastComboStage));

            if (isCombat && stateChanged)
            {
                if (state.State == ActionState.Attacking)
                {
                    // Look up clip from character definition
                    string animName = "melee";
                    float animSpeed = 1f;
                    if (_charDef != null)
                    {
                        byte slot = (byte)(state.AttackSlot - 1);
                        var spec = _charDef.GetSlotAbility(slot, !state.IsGrounded);
                        if (spec?.Stages != null && state.ComboStage < spec.Stages.Length)
                        {
                            animName = spec.GetAnimationName(state.ComboStage);
                            // Runtime speed modulation: match animation duration to server stage duration
                            if (_bakedData != null)
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
                        }
                    }

                    var clip = _charConfig.GetClipByName(animName);
                    if (clip != null)
                    {
                        var animState = _animancer.Play(clip, 0.05f);
                        animState.Speed = animSpeed;
                    }
                }
                else if (state.State == ActionState.Dashing)
                {
                    var clip = _charConfig.GetClipByName("dash");
                    if (clip != null)
                        _animancer.Play(clip, 0f);
                }
                else if (state.State == ActionState.Hitstun)
                {
                    string hitName = state.HitstunLevel switch
                    {
                        1 => "hit_medium",
                        2 => "hit_hard",
                        _ => "hit_small"
                    };
                    var clip = _charConfig.GetClipByName(hitName);
                    if (clip != null)
                        _animancer.Play(clip, 0f);
                }
            }

            _lastAnimState = state.State;
            _lastAttackSlot = state.AttackSlot;
            _lastComboStage = state.ComboStage;
            _wasGrounded = state.IsGrounded;
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
            Gizmos.DrawSphere(transform.position + Vector3.up * 3.2f, 0.25f);

            // ── Collision capsule ──
            Gizmos.color = stateColor;
            Vector3 capCenter = transform.position - Vector3.up * _modelYOffset;
            float halfH = Mathf.Max(_capsuleHeight * 0.5f - _capsuleRadius, 0f);
            Vector3 top = capCenter + Vector3.up * halfH;
            Vector3 bot = capCenter - Vector3.up * halfH;

            UnityEditor.Handles.DrawWireDisc(top, Vector3.up, _capsuleRadius);
            UnityEditor.Handles.DrawWireDisc(bot, Vector3.up, _capsuleRadius);
            UnityEditor.Handles.DrawLine(top + Vector3.right * _capsuleRadius, bot + Vector3.right * _capsuleRadius);
            UnityEditor.Handles.DrawLine(top - Vector3.right * _capsuleRadius, bot - Vector3.right * _capsuleRadius);
            UnityEditor.Handles.DrawLine(top + Vector3.forward * _capsuleRadius, bot + Vector3.forward * _capsuleRadius);
            UnityEditor.Handles.DrawLine(top - Vector3.forward * _capsuleRadius, bot - Vector3.forward * _capsuleRadius);

            // ── Hurtbox spheres at bone positions ──
            if (_hurtboxBoneDefs != null)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
                foreach (var def in _hurtboxBoneDefs)
                {
                    Transform bone = FindBone(def.BoneName);
                    if (bone != null)
                    {
                        Vector3 localOffset = transform.InverseTransformDirection(new Vector3(def.OffX, def.OffY, def.OffZ));
                        Gizmos.DrawWireSphere(bone.position + localOffset, def.Radius);
                    }
                }
            }
        }

        private Transform FindBone(string name)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
