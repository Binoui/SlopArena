using Unity.Cinemachine;
using UnityEngine;

namespace SlopArena.Client.Camera
{
    /// <summary>
    /// Owns the dedicated aim camera for CameraForward3D abilities (Bazooka, Grapple).
    ///
    /// Attach to the AimCamera GameObject alongside CinemachineCamera only.
    /// Do NOT add CinemachineFollow — position is driven manually via ForceCameraPosition
    /// so the camera sits at orbital distance with a shoulder offset, without Cinemachine fighting it.
    ///
    /// Pivot follows the player and rotates with mouse input.
    /// Camera = pivot.position + pivot.rotation * (shoulderX, shoulderY, -followDistance)
    /// Camera rotation = pivot.rotation  →  screen center = aim ray = projectile direction.
    ///
    /// Usage:
    ///   Activate(playerTransform, facingYawRad, followDistance)  — snap behind character, raise priority
    ///   Tick(playerTransform)                                     — called every FixedUpdate while active
    ///   ApplyMouseDelta(delta, sensitivity)                       — rotate pivot from mouse input
    ///   Deactivate()                                              — lower priority, Cinemachine blends back
    /// </summary>
    public class AimCameraMount : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera _aimCinemachineCamera;
        [SerializeField] private Transform _pivot;

        [SerializeField] private float _pitchMin = -60f;
        [SerializeField] private float _pitchMax =  60f;

        [Header("Aim Defaults")]
        [Tooltip("Starting pitch when aim camera activates. Positive = above horizon. 10 puts crosshair roughly at enemy chest height at range.")]
        [SerializeField] private float _defaultPitchDeg = 10f;

        [Header("Shoulder Offset")]
        [Tooltip("Right offset in pivot-local space. Positive = right shoulder. Character ends up left of center.")]
        [SerializeField] private float _shoulderOffsetX = 0.5f;
        [Tooltip("Up offset in pivot-local space. Raises the aim camera slightly above eye level.")]
        [SerializeField] private float _shoulderOffsetY = 0.3f;

        private bool _active;
        private float _yawDeg;
        private float _pitchDeg;
        private float _followDistance = 2.5f;

        private void Awake()
        {
            // Start at -1 so the orbital camera (priority 0) always wins by default.
            // Activate raises to 20; Deactivate drops back to -1.
            if (_aimCinemachineCamera != null)
                _aimCinemachineCamera.Priority = -1;
        }

        /// <summary>
        /// Snap the aim camera behind the character and raise priority so Cinemachine blends in.
        /// facingYawRad: camera's current yaw in radians — aim camera inherits this so the blend is seamless.
        /// followDistance: orbital camera's current zoom radius — aim camera inherits this distance.
        /// </summary>
        public void Activate(Transform player, float facingYawRad, float followDistance = 2.5f)
        {
            if (_pivot == null || _aimCinemachineCamera == null) return;

            _followDistance = followDistance;
            _yawDeg         = facingYawRad * Mathf.Rad2Deg;
            _pitchDeg       = -_defaultPitchDeg; // negative: Unity Euler positive-X = tilt down

            // Seed pivot so ForceCameraPosition starts from the right spot — prevents the blend
            // from jumping if Cinemachine sampled the previous position before priority raised.
            _pivot.position = player.position;
            _pivot.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);

            ApplyCameraTransform();

            _aimCinemachineCamera.Priority = 20;
            _active = true;
        }

        /// <summary>
        /// Lower priority so Cinemachine blends back to the orbital camera.
        /// Uses -1 so orbital's 0 is always higher.
        /// </summary>
        public void Deactivate()
        {
            if (_aimCinemachineCamera != null)
                _aimCinemachineCamera.Priority = -1;
            _active = false;
        }

        /// <summary>
        /// Reposition the pivot to track the player each FixedUpdate tick, then push
        /// the computed camera position+rotation to Cinemachine via ForceCameraPosition.
        /// Call before ApplyMouseDelta.
        /// </summary>
        public void Tick(Transform player)
        {
            if (!_active || _pivot == null) return;
            _pivot.position = player.position;
            ApplyCameraTransform();
        }

        /// <summary>
        /// Accumulate mouse input into aim yaw and pitch.
        /// Call after Tick(), before reading GetAimYawRad/GetAimPitchRad.
        /// </summary>
        public void ApplyMouseDelta(Vector2 delta, float sensitivity)
        {
            if (!_active || _pivot == null) return;

            _yawDeg   += delta.x * sensitivity;
            _pitchDeg -= delta.y * sensitivity; // invert Y: mouse down = aim down
            _pitchDeg  = Mathf.Clamp(_pitchDeg, _pitchMin, _pitchMax);

            _pivot.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
            ApplyCameraTransform();
        }

        /// <summary>
        /// Compute world-space camera position (pivot + shoulder offset at follow distance)
        /// and push it to the CinemachineCamera via ForceCameraPosition.
        /// This bypasses CinemachineFollow so Cinemachine cannot fight the placement.
        /// </summary>
        private void ApplyCameraTransform()
        {
            if (_aimCinemachineCamera == null || _pivot == null) return;

            // Shoulder offset in pivot-local space: right, up, back
            Vector3 localOffset = new Vector3(_shoulderOffsetX, _shoulderOffsetY, -_followDistance);
            Vector3 worldPos    = _pivot.position + _pivot.rotation * localOffset;
            Quaternion worldRot = _pivot.rotation;

            _aimCinemachineCamera.ForceCameraPosition(worldPos, worldRot);
        }

        /// <summary>Aim yaw in radians — fed into AimContext.AimYawRad.</summary>
        public float GetAimYawRad() => _yawDeg * Mathf.Deg2Rad;

        /// <summary>
        /// Aim pitch in radians — fed into AimContext.AimPitchRad.
        /// Positive = above horizon (mouse up), negative = below horizon (mouse down).
        /// Negates _pitchDeg because Unity Euler uses opposite sign (positive = down).
        /// </summary>
        public float GetAimPitchRad() => -_pitchDeg * Mathf.Deg2Rad;
    }
}
