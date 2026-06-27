using SlopArena.Shared;
using UnityEngine;

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
        [SerializeField] private Animator _animator;

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

        public void SetCharacterDefinition(CharacterDefinition? def) => _charDef = def;
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
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            if (def.VisualScale != 1f)
                instance.transform.localScale = Vector3.one * def.VisualScale;

            _animator = instance.GetComponent<Animator>();
            if (_animator == null)
                Debug.LogError($"[PlayerRenderer] Instantiated model has no Animator component");
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
        public Animator Animator => _animator;

        // ── Tracking state for change detection ──

        private CharacterState _lastState;
        private bool _wasGrounded = true;

        // ── Frame-by-frame animation control ──

        private BakedAnimationData? _bakedData;
        private int _animFrame;           // current frame index within the active clip
        private string _lastAnimClip = ""; // clip name from last tick (to detect transitions)
        private ushort _startLockTicks;

        /// <summary>
        /// Set baked skeleton data for frame-accurate animation.
        /// Must be called before first ApplyServerState.
        /// </summary>
        public void SetBakedData(BakedAnimationData? data) => _bakedData = data;

        // ── Lifecycle ──

        private void Awake()
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();
        }

        // ── Public API ──

        /// <summary>
        /// Apply a server-authoritative CharacterState to this entity.
        /// Updates position, rotation, and drives animation state.
        /// Call from Update() after reconciliation.
        /// </summary>
        public void ApplyServerState(CharacterState state)
        {
            transform.position = new Vector3(state.PX, state.PY + _modelYOffset, state.PZ);
            transform.rotation = Quaternion.Euler(0f, state.FacingYaw * Mathf.Rad2Deg, 0f);

            UpdateAnimationState(state);

            _lastState = state;
        }

        // ── Animation State Machine ──

        /// <summary>
        /// Resolve the target animation clip name from current character state.
        /// Mirrors the logic in ServerSimulation.Tick entity-list building.
        /// </summary>
        private string ResolveAnimClip(CharacterState state)
        {
            if (state.State == ActionState.Dashing) return "Dash";
            if (state.State == ActionState.Hitstun) return "Hitstun";
            if (state.State == ActionState.Attacking && state.AttackSlot > 0 && _charDef != null)
            {
                bool airborne = !state.IsGrounded;
                var spec = _charDef.GetSlotAbility(state.AttackSlot - 1, airborne);
                int stageIdx = Mathf.Min(state.ComboStage, (byte)(spec.Stages.Length - 1));
                if (stageIdx >= 0 && stageIdx < spec.AnimationNames.Length)
                    return spec.AnimationNames[stageIdx];
                return "Melee";
            }
            if (state.State == ActionState.JumpSquat) return "Jump";
            if (!state.IsGrounded) return state.VY > 0f ? "Jump" : "Fall";
            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;
            if (hSpeed > _runSpeedThreshold) return "Movement";
            return "Movement";
        }

        private void UpdateAnimationState(CharacterState state)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;

            bool isGrounded = state.IsGrounded;

            // DEBUG: trace state and ground detection
            //Debug.Log($"[Anim] {_entityName}({_entityId}) Y={state.PY:F3} VY={state.VY:F3} grounded={isGrounded} state={state.State} warp={state.WarpSpeed:F2}");

            _animator.SetBool("IsGrounded", isGrounded);
            _animator.SetBool("IsWarping", state.WarpSpeed > 0f);

            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;
            _animator.SetFloat("Speed", hSpeed);
            _animator.SetBool("IsMoving", hSpeed > _runSpeedThreshold);

            // Resolve target animation clip
            string targetClip = ResolveAnimClip(state);

            // Frame-accurate driving for discrete animation states
            // Movement (idle/run blend tree) uses Play to enter the state, then Speed float drives the blend
            if (targetClip == "Movement")
            {
                if (_lastAnimClip != targetClip)
                {
                    _animator.Play("Movement", 0, 0f);
                    _lastAnimClip = targetClip;
                }
            }
            else
            {
                // Tick-driven and baked-frame fallback for discrete animation states
                if (_lastAnimClip != targetClip)
                {
                    _animFrame = 0;
                    _lastAnimClip = targetClip;
                    _startLockTicks = state.AnimLockTicks;
                }

                // Compute normalized time:
                // - If AnimLockTicks is set (attack, hitstun, dash): use lock ratio for tick-exact timing
                // - Otherwise: fall back to baked frame count
                float normalizedTime;
                if (_startLockTicks > 0)
                {
                    // _startLockTicks captured the initial value; state.AnimLockTicks counts down.
                    // First frame: (start - (start-1)) / start ≈ 0.  Last frame: (start - 0) / start = 1.
                    float elapsed = _startLockTicks - state.AnimLockTicks;
                    normalizedTime = Mathf.Clamp01(elapsed / _startLockTicks);
                }
                else
                {
                    _animFrame++;
                    int totalFrames = 30;
                    if (_bakedData != null)
                    {
                        int idx = _bakedData.FindAnimIndex(targetClip);
                        if (idx >= 0)
                            totalFrames = _bakedData.Animations[idx].FrameCount;
                    }
                    normalizedTime = totalFrames > 0 ? (_animFrame % totalFrames) / (float)totalFrames : 0f;
                }

                _animator.Play(targetClip, 0, normalizedTime);
                _animator.Update(0f);
            }

            _wasGrounded = isGrounded;
        }

        /// <summary>
        /// Reset animation state to defaults. Useful on respawn.
        /// </summary>
        public void ResetAnimationState()
        {
            _animator.Rebind();
            _wasGrounded = true;
            _animFrame = 0;
            _lastAnimClip = "";
            _startLockTicks = 0;
            _lastState = default;
        }

        // ── Gizmos ──

        private void OnDrawGizmos()
        {
            // ── Entity name label ──
            Gizmos.color = Color.white;
            Vector3 labelPos = transform.position + Vector3.up * 2.5f;
            UnityEditor.Handles.Label(labelPos, _entityName);

            // ── Collision capsule ──
            Gizmos.color = new Color(0f, 1f, 0f, 0.6f);
            Vector3 capCenter = transform.position;
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
