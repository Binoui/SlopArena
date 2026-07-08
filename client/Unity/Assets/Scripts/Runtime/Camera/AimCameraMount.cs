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

        private float _yawDeg;
        private float _pitchDeg;
        private bool _active;

        /// <summary>
        /// Snap the aim camera behind the character and raise priority so Cinemachine blends in.
        /// facingYawRad: character's current facing direction in radians.
        /// </summary>
        public void Activate(Transform player, float facingYawRad)
        {
            if (_pivot == null || _aimCinemachineCamera == null) return;

            _yawDeg   = facingYawRad * Mathf.Rad2Deg;
            _pitchDeg = 0f;

            _pivot.position = player.position;
            _pivot.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);

            _aimCinemachineCamera.Priority = 20;
            _active = true;
        }

        /// <summary>
        /// Lower priority so Cinemachine blends back to the orbital camera.
        /// </summary>
        public void Deactivate()
        {
            if (_aimCinemachineCamera != null)
                _aimCinemachineCamera.Priority = 0;
            _active = false;
        }

        /// <summary>
        /// Reposition the pivot to track the player each FixedUpdate tick.
        /// The player moves during aiming — keep the camera following.
        /// </summary>
        public void Tick(Transform player)
        {
            if (!_active || _pivot == null) return;
            _pivot.position = player.position;
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

        /// <summary>Aim pitch in radians — fed into AimContext.AimPitchRad. Negative = below horizon.</summary>
        public float GetAimPitchRad() => _pitchDeg * Mathf.Deg2Rad;
    }
}
