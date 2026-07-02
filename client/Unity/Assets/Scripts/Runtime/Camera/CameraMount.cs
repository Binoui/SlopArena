using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.Camera
{
    [RequireComponent(typeof(CinemachineCamera))]
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    public class CameraMount : MonoBehaviour
    {

        private CinemachineCamera _cmCam;
        private CinemachineOrbitalFollow _orbital;

        private void Awake()
        {
            _cmCam = GetComponent<CinemachineCamera>();
            _orbital = GetComponent<CinemachineOrbitalFollow>();
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            if (_orbital == null) return;
            float dy = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(dy) > 0.001f)
                _orbital.RadialAxis.Value -= dy * 0.05f;
        }

        public void SetTarget(Transform target)
        {
            _cmCam.Target = new CameraTarget
            {
                TrackingTarget = target,
                LookAtTarget = target
            };
        }

        /// <summary>
        /// Snap orbit to face the target from behind at a comfortable angle.
        /// Call after SetTarget to avoid the camera starting at a random orientation.
        /// </summary>
        public void ResetView(Transform target)
        {
            if (_orbital == null) return;
            _orbital.HorizontalAxis.Value = target.eulerAngles.y;
            _orbital.VerticalAxis.Value = 17.5f;
        }


        public float GetCameraYawDeg()
        {
            return _orbital != null ? _orbital.HorizontalAxis.Value : 0f;
        }

        public void SetCameraYawDeg(float yawDeg)
        {
            if (_orbital != null)
                _orbital.HorizontalAxis.Value = yawDeg;
        }

        public void SetCameraPitchDeg(float pitchDeg)
        {
            if (_orbital != null)
                _orbital.VerticalAxis.Value = pitchDeg;
        }

        public float GetCameraPitchDeg()
        {
            return _orbital != null ? _orbital.VerticalAxis.Value : 0f;
        }

        public float GetCameraYawRad()
        {
            return GetCameraYawDeg() * Mathf.Deg2Rad;
        }

        public Vector3 GetForwardDirection()
        {
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            return fwd.normalized;
        }

        public Vector3 GetRightDirection()
        {
            Vector3 right = transform.right;
            right.y = 0f;
            return right.normalized;
        }

        /// <summary>
        /// Smoothly rotate camera yaw toward a world-space target position.
        /// Clamps rotation speed so the camera doesn't snap.
        /// </summary>
        public void LerpTowardDirection(Vector3 fromPos, Vector3 targetPos, float lerpSpeedDegPerSec)
        {
            float dx = targetPos.x - fromPos.x;
            float dz = targetPos.z - fromPos.z;
            if (dx * dx + dz * dz < 0.01f) return;
            float targetYaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            float currentYaw = GetCameraYawDeg();
            float diff = Mathf.DeltaAngle(currentYaw, targetYaw);
            float maxStep = lerpSpeedDegPerSec * Time.deltaTime;
            float newYaw = currentYaw + Mathf.Clamp(diff, -maxStep, maxStep);
            SetCameraYawDeg(newYaw);
        }
    }
}
