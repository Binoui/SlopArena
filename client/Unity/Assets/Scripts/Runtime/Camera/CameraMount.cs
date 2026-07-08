using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.Camera
{
    public enum CameraMode
    {
        Normal,       // Cursor locked, camera orbits freely
        Frozen,       // Cursor locked, camera yaw/pitch held constant
        FreeCursor,   // Cursor unlocked, camera yaw/pitch held constant
        Aiming,       // Cursor locked, orbital camera frozen, AimCameraMount drives aim camera
    }

    [RequireComponent(typeof(CinemachineCamera))]
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    public class CameraMount : MonoBehaviour
    {
        private CinemachineCamera _cmCam;
        private CinemachineOrbitalFollow _orbital;

        private CameraMode _mode = CameraMode.Normal;
        private float _frozenYaw;
        private float _frozenPitch = 17.5f;

        private void Awake()
        {
            _cmCam = GetComponent<CinemachineCamera>();
            _orbital = GetComponent<CinemachineOrbitalFollow>();
            // Allow camera to orbit below target (negative VerticalAxis = looking UP)
            if (_orbital != null)
                _orbital.VerticalAxis.Range = new Vector2(-30f, 50f);
        }
        
        /// <summary>
        /// The real Unity Camera that this mount drives.
        /// </summary>
        public UnityEngine.Camera RenderCamera => GetComponentInChildren<UnityEngine.Camera>();
        private void Start()
        {
            SetMode(CameraMode.Normal);
        }

        private void Update()
        {
            if (_orbital == null) return;

            // Normal — mouse controls yaw+pitch freely, scroll still works for zoom
            if (_mode == CameraMode.Normal)
            {
                float dy = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(dy) > 0.001f)
                    _orbital.RadialAxis.Value -= dy * 0.05f;
                // Pitch and yaw handled by Cinemachine's built-in orbital input
            }
            else if (_mode == CameraMode.Frozen)
            {
                // Lock both yaw and pitch — camera stays put, crosshair moves on screen
                SetCameraYawDeg(_frozenYaw);
                SetCameraPitchDeg(_frozenPitch);
            }
            else if (_mode == CameraMode.FreeCursor)
            {
                // Re-apply cached angles (cursor controls ground marker, not camera)
                SetCameraYawDeg(_frozenYaw);
                SetCameraPitchDeg(_frozenPitch);
            }
            // CameraMode.Aiming: do nothing — AimCameraMount owns all mouse input
        }
        public void SetMode(CameraMode mode)
        {
            _mode = mode;
            switch (mode)
            {
                case CameraMode.Normal:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    break;
                case CameraMode.Frozen:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    break;
                case CameraMode.FreeCursor:
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    break;
                case CameraMode.Aiming:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    break;
            }
        }

        public void FreezeAtCurrentAngles()
        {
            _frozenYaw = GetCameraYawDeg();
            _frozenPitch = GetCameraPitchDeg();
        }

        /// <summary>
        /// Accumulate mouse delta into the frozen camera orbit angles.
        /// Only meaningful in Frozen mode — updates _frozenYaw and _frozenPitch.
        /// deltaDeg = delta pixels * sensitivity (already scaled).
        /// </summary>
        public void OrbitFrozen(Vector2 deltaDeg)
        {
            if (_mode != CameraMode.Frozen) return;
            _frozenYaw += deltaDeg.x;
            _frozenPitch -= deltaDeg.y;
            _frozenPitch = Mathf.Clamp(_frozenPitch, -60f, 60f);
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
        public float GetOrbitRadius()
        {
            if (_orbital == null) return 2.5f;
            // Actual camera distance = base Radius multiplied by scroll-adjusted RadialAxis
            return _orbital.Radius * _orbital.RadialAxis.Value;
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
