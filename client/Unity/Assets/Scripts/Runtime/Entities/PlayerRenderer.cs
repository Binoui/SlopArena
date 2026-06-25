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
            // ── Transform ──
            transform.position = new Vector3(state.PX, state.PY, state.PZ);
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

            _animator.SetBool("IsGrounded", isGrounded);
            _animator.SetBool("IsWarping", state.WarpSpeed > 0f);

            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;
            _animator.SetFloat("Speed", hSpeed);
            _animator.SetBool("IsMoving", hSpeed > _runSpeedThreshold);

            if (!isGrounded && _wasGrounded)
                _animator.SetTrigger("Jump");

            _wasGrounded = isGrounded;

            if (_lastState.State != state.State)
            {
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
            // Draw entity name above the renderer
            Gizmos.color = Color.white;
            Vector3 labelPos = transform.position + Vector3.up * 2.5f;
            UnityEditor.Handles.Label(labelPos, _entityName);
        }
    }
}
