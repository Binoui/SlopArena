using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Generic weapon attachment component. Add one instance to every PlayerRenderer
    /// GameObject (player and NPC). No subclassing required.
    ///
    /// After LoadModel(), call Init(renderer, config) where config is loaded from
    /// Resources/WeaponConfigs/<CharacterClass>.asset. If config is null the component
    /// is a no-op — characters without weapons need no special handling.
    /// </summary>
    [ExecuteAlways]
    public class WeaponAttach : MonoBehaviour
    {
        private PlayerRenderer _owner;
        private SkinnedMeshRenderer _skin;

        private bool _previewStateActive;
        private byte _previewAttackSlot;
        private int _previewAttackElapsedTicks;

        // Parallel arrays indexed by config.Entries[]
        private Transform[] _bones;
        private GameObject[] _instances;
        private WeaponEntry[] _entries;

        /// <summary>
        /// Initialise (or re-initialise) with a config asset.
        /// Safe to call multiple times (e.g. character swap or model reload).
        /// Pass null config to make this component inert.
        /// </summary>
        public void Init(PlayerRenderer owner, WeaponAttachConfig config)
        {
            Cleanup();

            _owner = owner;

            if (config == null || config.Entries == null || config.Entries.Length == 0)
                return;

            _skin = owner.GetComponentInChildren<SkinnedMeshRenderer>();
            if (_skin == null)
            {
                Debug.LogWarning($"[WeaponAttach] No SkinnedMeshRenderer under {owner.name}");
                return;
            }

            _entries = config.Entries;
            int count = _entries.Length;
            _bones = new Transform[count];
            _instances = new GameObject[count];

            for (int i = 0; i < count; i++)
            {
                _bones[i] = FindBone(_entries[i].BoneName);

                if (_entries[i].Prefab == null)
                {
                    Debug.LogWarning($"[WeaponAttach] Prefab is null for entry {i} ({_entries[i].BoneName})");
                    continue;
                }

                _instances[i] = Instantiate(_entries[i].Prefab);
                _instances[i].SetActive(false);
            }
        }
        /// <summary>
        /// Supplies the Ability Lab's scrubbed attack state without mutating gameplay state.
        /// Runtime callers leave this disabled.
        /// </summary>
        public void SetPreviewState(byte attackSlot, int attackElapsedTicks)
        {
            _previewStateActive = true;
            _previewAttackSlot = attackSlot;
            _previewAttackElapsedTicks = attackElapsedTicks;
        }


        private void Update()
        {
            if (_owner == null || _entries == null) return;

            byte slot = _previewStateActive ? _previewAttackSlot : _owner.CurrentAttackSlot;
            bool isAttacking = _previewStateActive || _owner.CurrentActionState == ActionState.Attacking;
            bool isAiming = !_previewStateActive && _owner.CurrentActionState == ActionState.Aiming;
            int elapsedTicks = _previewStateActive
                ? _previewAttackElapsedTicks
                : _owner.CurrentAttackElapsedTicks;

            for (int i = 0; i < _entries.Length; i++)
            {
                var go = _instances[i];
                if (go == null) continue;

                byte entrySlot = _entries[i].AttackSlot;
                // HideAfterTicks hides the prop once the ATTACK has run its ticks (the
                // "leaves the hand" moment); during the aim hold the prop stays visible
                // for the whole hold regardless of elapsed ticks.
                bool withinHold = _previewStateActive
                    || _entries[i].HideAfterTicks <= 0
                    || !isAttacking
                    || elapsedTicks < _entries[i].HideAfterTicks;
                bool visible = (entrySlot == 0 ? true : (isAttacking || isAiming) && slot == entrySlot) && withinHold;

                if (go.activeSelf != visible)
                    go.SetActive(visible);

                if (visible && _bones[i] != null)
                {
                    go.transform.position = _bones[i].TransformPoint(_entries[i].PositionOffset);
                    go.transform.rotation = _bones[i].rotation
                        * Quaternion.Euler(_entries[i].RotationOffset);
                }
            }
        }

        private void Cleanup()
        {
            if (_instances != null)
            {
                foreach (var go in _instances)
                {
                    if (go == null) continue;
                    if (Application.isPlaying) Destroy(go);
                    else DestroyImmediate(go);
                }
            }
            _owner = null;
            _skin = null;
            _bones = null;
            _instances = null;
            _entries = null;
            _previewStateActive = false;
            _previewAttackSlot = 0;
            _previewAttackElapsedTicks = 0;
        }

        private Transform FindBone(string boneName)
        {
            if (_skin == null) return null;
            foreach (var b in _skin.bones)
                if (b != null && b.name == boneName)
                    return b;

            // Package configs may use the canonical Humanoid alias while a rig
            // exposes an imported bone name such as Bonk's hand_r.
            if (boneName == "mixamorig:RightHand")
            {
                var animator = _skin.GetComponentInParent<Animator>();
                var humanoidHand = animator?.GetBoneTransform(HumanBodyBones.RightHand);
                if (humanoidHand != null)
                    return humanoidHand;
            }

            Debug.LogWarning($"[WeaponAttach] Bone '{boneName}' not found on {_owner.name}");
            return null;
        }
    }
}
