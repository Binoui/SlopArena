using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Visual representation of a CharacterState.
    /// Applies server-authoritative position, rotation, and drives
    /// animation state on an Animator. Works for both player and NPC entities.
    /// </summary>
    [RequireComponent(typeof(Animator))]
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
        public float ModelYOffset
        {
            get => _modelYOffset;
            set => _modelYOffset = value;
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

        private void UpdateAnimationState(CharacterState state)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;

            bool isGrounded = state.IsGrounded;

            // DEBUG: trace state and ground detection
            Debug.Log($"[Anim] {_entityName}({_entityId}) Y={state.PY:F3} VY={state.VY:F3} grounded={isGrounded} state={state.State} warp={state.WarpSpeed:F2}");

            _animator.SetBool("IsGrounded", isGrounded);
            _animator.SetBool("IsWarping", state.WarpSpeed > 0f);

            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;
            _animator.SetFloat("Speed", hSpeed);
            _animator.SetBool("IsMoving", hSpeed > _runSpeedThreshold);

            if (!isGrounded && _wasGrounded)
            {
                _animator.SetTrigger("Jump");
                Debug.Log($"[Anim] {_entityName}({_entityId}) → Jump trigger (was grounded, now airborne)");
            }

            _wasGrounded = isGrounded;

            if (_lastState.State != state.State)
            {
                Debug.Log($"[Anim] {_entityName}({_entityId}) ActionState changed: {_lastState.State} → {state.State}");
                switch (state.State)
                {
                    case ActionState.Attacking:
                        _animator.SetTrigger("Attack");
                        _animator.SetInteger("ComboStage", state.ComboStage);
                        break;
                    case ActionState.Dashing:
                        _animator.SetTrigger("Dash"); break;
                    case ActionState.Hitstun:
                        _animator.SetTrigger("Hitstun"); break;
                    case ActionState.AirDodging:
                        _animator.SetTrigger("AirDodge"); break;
                    case ActionState.Sliding:
                        _animator.SetTrigger("Slide"); break;
                    case ActionState.Idle:
                        _animator.SetTrigger("Idle"); break;
                }
            }
        }

        /// <summary>
        /// Reset animation state to defaults. Useful on respawn.
        /// </summary>
        public void ResetAnimationState()
        {
            _animator.Rebind();
            _wasGrounded = true;
            _lastState = default;
        }

        // ── Gizmos ──

        private void OnDrawGizmosSelected()
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
