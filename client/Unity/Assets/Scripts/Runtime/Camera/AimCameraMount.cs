using Unity.Cinemachine;
using UnityEngine;

namespace SlopArena.Client.Camera
{
    /// <summary>
    /// Owns the dedicated aim camera for CameraForward3D abilities (Bazooka, Grapple).
    ///
    /// Attach to the AimCamera GameObject alongside CinemachineCamera + CinemachineFollow.
    ///
    /// Usage:
    ///   Activate(playerTransform, facingYawRad)  — snap behind character, raise priority
    ///   Tick(playerTransform)                    — called every FixedUpdate while active
    ///   ApplyMouseDelta(delta, sensitivity)       — rotate pivot from mouse input
    ///   Deactivate()                             — lower priority, Cinemachine blends back
    /// </summary>
    public class AimCameraMount : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera _aimCinemachineCamera;
        [SerializeField] private Transform _pivot;

        [SerializeField] private float _pitchMin = -60f;
        [SerializeField] private float _pitchMax =  60f;
        [SerializeField] private bool _inheritOrbitDistance = true;
        private bool _active;
        private float _yawDeg;
        private float _pitchDeg;
        private CinemachineFollow _follow;

        private void Awake()
        {
            // Start at -1 so the orbital camera (priority 0) always wins by default.
            // Activate raises to 20; Deactivate drops back to -1.
            if (_aimCinemachineCamera != null)
                _aimCinemachineCamera.Priority = -1;
            _follow = GetComponent<CinemachineFollow>();
        }
        /// <summary>
        /// Snap the aim camera behind the character and raise priority so Cinemachine blends in.
        /// facingYawRad: camera's current yaw direction in radians.
        /// followDistance: orbital camera's current zoom radius — aim camera inherits this distance.
        /// </summary>
        public void Activate(Transform player, float facingYawRad, float followDistance = 2.5f)
        {
            if (_pivot == null || _aimCinemachineCamera == null) return;

            _yawDeg   = facingYawRad * Mathf.Rad2Deg;
            _pitchDeg = 0f;

            _pivot.position = player.position;
            _pivot.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);

            // Inherit orbital zoom distance so aim camera feels like the same zoom level
            if (_inheritOrbitDistance && _follow != null)
            {
                Vector3 offset = _follow.FollowOffset;
                offset.z = -followDistance;
                _follow.FollowOffset = offset;
            }

            _aimCinemachineCamera.Priority = 20;
            _active = true;
        }

        /// <summary>
        /// Lower priority so Cinemachine blends back to the orbital camera.
        /// Uses -1 so orbital's 0 is always higher — avoids priority tie where Brain may stay on aim cam.
        /// </summary>
        public void Deactivate()
        {
            if (_aimCinemachineCamera != null)
                _aimCinemachineCamera.Priority = -1;
            _active = false;
        }

        /// <summary>
        /// Reposition the pivot to track the player each FixedUpdate tick.
        /// The player moves during aiming — keep the camera following.
        /// Also faces the AimCamera in the aim direction (no HardLookAt on scene).
        /// </summary>
        public void Tick(Transform player)
        {
            if (!_active || _pivot == null) return;
            _pivot.position = player.position;

            // Camera faces the aim direction so crosshair at screen center = projectile direction
            if (_aimCinemachineCamera != null)
                _aimCinemachineCamera.transform.rotation = _pivot.rotation;
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

            _pitchDeg = Mathf.Clamp(_pitchDeg, _pitchMin, _pitchMax);
            _pivot.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
        }

        /// <summary>Aim yaw in radians — fed into AimContext.AimYawRad.</summary>
        public float GetAimYawRad()   => _yawDeg * Mathf.Deg2Rad;

        /// <summary>
        /// Aim pitch in radians — fed into AimContext.AimPitchRad.
        /// Positive = above horizon (mouse up), negative = below horizon (mouse down).
        /// Negates _pitchDeg because Unity Euler uses opposite sign (positive = down).
        /// </summary>
        public float GetAimPitchRad() => -_pitchDeg * Mathf.Deg2Rad;
    }
}
