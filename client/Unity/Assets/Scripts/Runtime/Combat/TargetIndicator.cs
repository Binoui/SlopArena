using SlopArena.Shared;
using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Shows a red ring under the currently targeted entity (soft-lock).
    /// Reads TargetEntityId from the simulation each frame and positions
    /// the ring at the corresponding renderer's feet.
    /// </summary>
    public class TargetIndicator : MonoBehaviour
    {
        private PlayerRenderer[] _renderers;
        private ServerSimulation _sim;
        private ulong _localPlayerId;
        private Transform _ring;

        public void Init(PlayerRenderer[] renderers, ServerSimulation sim, ulong localPlayerId)
        {
            _renderers = renderers;
            _sim = sim;
            _localPlayerId = localPlayerId;
        }

        private void Awake()
        {
            var ringGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringGO.name = "TargetIndicatorRing";
            ringGO.transform.SetParent(transform, false);
            ringGO.transform.localScale = new Vector3(1.2f, 0.04f, 1.2f);
            ringGO.transform.localPosition = Vector3.zero;
            Destroy(ringGO.GetComponent<CapsuleCollider>());
            var mr = ringGO.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = new Color(1f, 0.2f, 0.15f, 0.7f);
            mr.sharedMaterial = mat;
            mr.receiveShadows = false;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring = ringGO.transform;
            _ring.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_sim == null || _renderers == null || _renderers.Length == 0)
            {
                if (_ring != null) _ring.gameObject.SetActive(false);
                return;
            }

            ulong targetId = _sim.GetState(_localPlayerId).TargetEntityId;
            if (targetId == 0)
            {
                _ring.gameObject.SetActive(false);
                return;
            }

            // Find the target's renderer
            bool found = false;
            foreach (var renderer in _renderers)
            {
                if (renderer == null) continue;
                if (renderer.EntityId == targetId)
                {
                    _ring.gameObject.SetActive(true);
                    _ring.transform.position = renderer.transform.position + Vector3.up * 0.05f;
                    found = true;
                    break;
                }
            }

            if (!found)
                _ring.gameObject.SetActive(false);
        }
    }
}
