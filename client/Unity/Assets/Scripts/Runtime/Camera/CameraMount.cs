using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.Camera
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraMount : MonoBehaviour
    {
        [Header("Orbit")]
        [SerializeField] private float _yawSpeed = 180f;
        [SerializeField] private float _pitchSpeed = 120f;

        [Header("Zoom")]
        [SerializeField] private float _zoomSpeed = 8f;

        private CinemachineCamera _cmCam;
        private CinemachineOrbitalFollow _orbital;
        private CinemachinePanTilt _panTilt;

        private void Awake()
        {
            _cmCam = GetComponent<CinemachineCamera>();
            _orbital = GetComponent<CinemachineOrbitalFollow>();
            _panTilt = GetComponent<CinemachinePanTilt>();
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                float mx = delta.x * _yawSpeed * Time.deltaTime;
                float my = delta.y * _pitchSpeed * Time.deltaTime;
                if (_panTilt != null)
                {
                    _panTilt.PanAxis.Value += mx;
                    _panTilt.TiltAxis.Value -= my;
                    _panTilt.TiltAxis.Value = Mathf.Clamp(_panTilt.TiltAxis.Value, -80f, 80f);
                }
            }

            float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            if (Mathf.Abs(scroll) > 0.01f && _orbital != null)
                _orbital.RadialAxis.Value = Mathf.Clamp(
                    _orbital.RadialAxis.Value - scroll * _zoomSpeed, 3f, 20f);
        }

        public void SetTarget(Transform target)
        {
            _cmCam.Follow = target;
            _cmCam.LookAt = target;
        }

        public float GetCameraYawDeg()
        {
            return _panTilt != null ? _panTilt.PanAxis.Value : 0f;
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
    }
}
