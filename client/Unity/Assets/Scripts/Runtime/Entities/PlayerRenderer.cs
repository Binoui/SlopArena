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
        private GameObject _modelInstance;

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
            _modelInstance = instance;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

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

        // ── Single-layer parameter-driven Animator ──
        //
        // All states (Movement blend, Jump, Fall, Land, Dash, Hitstun, abilities)
        // live in one state machine. Transitions driven by triggers + params.
        // On combat end, Idle trigger force-clears back to Movement.

        private void UpdateAnimationState(CharacterState state)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;

            // ── Movement params ──
            _animator.SetBool("IsGrounded", state.IsGrounded);
            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;
            _animator.SetFloat("Speed", hSpeed);

            // Jump trigger — fire once when entering JumpSquat
            if (state.State == ActionState.JumpSquat && _lastAnimState != ActionState.JumpSquat)
                _animator.SetTrigger("Jump");

            // ── Combat ──
            bool isCombat = state.State == ActionState.Attacking
                || state.State == ActionState.Dashing
                || state.State == ActionState.Hitstun;

            bool stateChanged = state.State != _lastAnimState
                || (state.State == ActionState.Attacking && (
                    state.AttackSlot != _lastAttackSlot || state.ComboStage != _lastComboStage));

            if (isCombat && stateChanged)
            {
                // Entering combat — reset triggers, then fire the right one
                _animator.ResetTrigger("Attack");
                _animator.ResetTrigger("Dash");
                _animator.ResetTrigger("Hitstun");
                _animator.ResetTrigger("Idle");

                if (state.State == ActionState.Attacking)
                {
                    _animator.SetInteger("AttackSlot", state.AttackSlot - 1);
                    _animator.SetInteger("ComboStage", state.ComboStage);
                    _animator.SetTrigger("Attack");
                }
                else if (state.State == ActionState.Dashing)
                {
                    _animator.SetTrigger("Dash");
                }
                else if (state.State == ActionState.Hitstun)
                {
                    _animator.SetTrigger("Hitstun");
                }
            }

            else if (!isCombat && 
                (_lastAnimState == ActionState.Attacking ||
                 _lastAnimState == ActionState.Dashing ||
                 _lastAnimState == ActionState.Hitstun))
            {
                // Left combat state — force-clear to Movement
                _animator.SetTrigger("Idle");
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
            _animator.Rebind();
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
