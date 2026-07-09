using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Base for per-character weapon attachment components.
    /// Attach to the same GameObject as PlayerRenderer.
    /// Call Init() after LoadModel() completes.
    /// </summary>
    public abstract class WeaponAttach : MonoBehaviour
    {
        protected PlayerRenderer Owner { get; private set; }
        protected SkinnedMeshRenderer Skin { get; private set; }

        /// <summary>
        /// Called by the match after LoadModel(). Finds the SkinnedMeshRenderer
        /// in the freshly instantiated model child, then delegates to OnInit().
        /// Safe to call multiple times (re-init on model reload).
        /// </summary>
        public void Init(PlayerRenderer owner)
        {
            Owner = owner;
            Skin = owner.GetComponentInChildren<SkinnedMeshRenderer>();
            if (Skin == null)
            {
                Debug.LogWarning($"[WeaponAttach] No SkinnedMeshRenderer under {owner.name}");
                return;
            }
            // Destroy any previously instantiated prop hierarchy
            OnCleanup();
            OnInit();
        }

        /// <summary>Destroy existing prop GameObjects before re-init.</summary>
        protected virtual void OnCleanup() { }

        /// <summary>Find bones, instantiate prefabs, store references.</summary>
        protected abstract void OnInit();

        private void Update()
        {
            if (Skin == null || Owner == null) return;
            OnUpdate();
        }

        /// <summary>Update prop positions and visibility each frame.</summary>
        protected abstract void OnUpdate();

        /// <summary>
        /// Find a bone Transform by exact name from Skin.bones[].
        /// Returns null and logs a warning if not found.
        /// </summary>
        protected Transform FindBone(string boneName)
        {
            foreach (var b in Skin.bones)
                if (b != null && b.name == boneName)
                    return b;
            Debug.LogWarning($"[WeaponAttach] Bone '{boneName}' not found on {Owner.name}");
            return null;
        }
    }
}
