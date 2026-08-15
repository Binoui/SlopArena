using System;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Shows a red ring under the persistent target-lock target (ADR-0018 / issue #127).
    /// Visible ONLY while the local player's sim state has LockOn set — the ring marks
    /// the locked enemy (TargetEntityId, resolved by the sim every tick). Reads state
    /// through a getState func (works for local sim and rollback bridge alike) and
    /// positions the ring at the matching renderer's feet each frame. Hidden when
    /// unlocked, no target resolved, or the target renderer is missing.
    /// </summary>
    public class TargetIndicator : MonoBehaviour
    {
        private PlayerRenderer[] _renderers;
        private Func<ulong, CharacterState> _getState;
        private ulong _localPlayerId;
        private Transform _ring;

        public void Init(Func<ulong, CharacterState> getState, PlayerRenderer[] renderers, ulong localPlayerId)
        {
            _getState = getState;
            _renderers = renderers;
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
            if (_getState == null || _renderers == null || _renderers.Length == 0)
            {
                if (_ring != null) _ring.gameObject.SetActive(false);
                return;
            }

            var local = _getState(_localPlayerId);
            if (!local.LockOn)
            {
                _ring.gameObject.SetActive(false);
                return;
            }

            ulong targetId = local.TargetEntityId;
            if (targetId == 0)
            {
                _ring.gameObject.SetActive(false);
                return;
            }

            // Find the locked target's renderer
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
