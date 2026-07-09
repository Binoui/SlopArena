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
    public class WeaponAttach : MonoBehaviour
    {
        private PlayerRenderer _owner;
        private SkinnedMeshRenderer _skin;

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

                var prefab = Resources.Load<GameObject>(_entries[i].PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[WeaponAttach] Prefab not found at Resources/{_entries[i].PrefabResourcePath}");
                    continue;
                }

                _instances[i] = Instantiate(prefab);
                _instances[i].SetActive(false);
            }
        }

        private void Update()
        {
            if (_owner == null || _entries == null) return;

            byte slot = _owner.CurrentAttackSlot;
            bool isAttacking = _owner.CurrentActionState == ActionState.Attacking;

            for (int i = 0; i < _entries.Length; i++)
            {
                var go = _instances[i];
                if (go == null) continue;

                byte entrySlot = _entries[i].AttackSlot;
                bool visible = entrySlot == 0
                    ? true                              // always-on weapon
                    : isAttacking && slot == entrySlot;

                if (go.activeSelf != visible)
                    go.SetActive(visible);

                if (visible && _bones[i] != null)
                {
                    go.transform.position = _bones[i].position;
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
                    if (go != null) Destroy(go);
            }
            _owner = null;
            _skin = null;
            _bones = null;
            _instances = null;
            _entries = null;
        }

        private Transform FindBone(string boneName)
        {
            if (_skin == null) return null;
            foreach (var b in _skin.bones)
                if (b != null && b.name == boneName)
                    return b;
            Debug.LogWarning($"[WeaponAttach] Bone '{boneName}' not found on {_owner.name}");
            return null;
        }
    }
}
