using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// World-space billboard above a character showing entity name + damage%.
    /// Uses legacy TextMesh for zero-allocation world-space text rendering.
    /// Faces the main camera each LateUpdate.
    /// </summary>
    public class StatusBillboard : MonoBehaviour
    {
        [SerializeField] private PlayerRenderer _owner;
        private TextMesh _textMesh;
        private ServerSimulation _sim;
        private ulong _entityId;
        private Transform _cameraTransform;

        public void Init(PlayerRenderer owner, ServerSimulation sim, ulong entityId)
        {
            _owner = owner;
            _sim = sim;
            _entityId = entityId;

            // Create TextMesh child
            var go = new GameObject("StatusBillboard");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 2f, 0); // above head
            go.transform.localRotation = Quaternion.identity;

            _textMesh = go.AddComponent<TextMesh>();
            _textMesh.anchor = TextAnchor.MiddleCenter;
            _textMesh.alignment = TextAlignment.Center;
            _textMesh.fontSize = 64;     // font atlas resolution (pt)
            _textMesh.characterSize = 0.06f; // world-space scale per character
            _textMesh.color = Color.white;

            // Camera resolved lazily in LateUpdate — Camera.main may not be ready
            // during OnMatchStart() due to Unity component initialization order.
            _cameraTransform = null;
        }

        private void LateUpdate()
        {
            if (_sim == null || _entityId == 0) return;

            // Lazy camera resolve — Camera.main may not be available at Init time
            if (_cameraTransform == null)
            {
                var cam = UnityEngine.Camera.main;
                if (cam == null) return; // still unavailable, try next frame
                _cameraTransform = cam.transform;
            }

            var state = _sim.GetState(_entityId);
            _textMesh.text = $"{state.DamagePercent}%";
            // Face the camera perfectly — use camera's forward direction
            // so text is always in the camera's view plane and readable.
            _textMesh.transform.rotation = Quaternion.LookRotation(
                _cameraTransform.forward, _cameraTransform.up);
        }
    }
}
