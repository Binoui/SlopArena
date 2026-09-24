using UnityEngine;
using Unity.Cinemachine;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
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
        private CinemachineInputAxisController _inputAxisController;

        private CameraMode _mode = CameraMode.Normal;
        private float _frozenYaw;
        private float _frozenPitch = 15f;
        private bool _chatInputSuppressed;
        private float _chatSuppressedYaw;
        private float _chatSuppressedPitch;
        private float _chatSuppressedRadial;
        private float _lastStableYaw;
        private float _lastStablePitch;
        private float _lastStableRadial;
        private bool _hasStableAngles;

        private void Awake()
        {
            _cmCam = GetComponent<CinemachineCamera>();
            _orbital = GetComponent<CinemachineOrbitalFollow>();
            _inputAxisController = GetComponent<CinemachineInputAxisController>();
            if (_inputAxisController != null)
                _inputAxisController.enabled = false;
            if (!TryGetComponent<CameraObstruction>(out _))
                gameObject.AddComponent<CameraObstruction>();
            // Clamp pitch so camera stays above the stage floor level
            if (_orbital != null)
                _orbital.VerticalAxis.Range = new Vector2(0f, 45f);
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

            bool suppressCameraInput = ChatInputGate.SuppressGameplay ||
                SettingsOverlay.Active is { IsOpen: true } || SettingsOverlay.ClosedThisFrame ||
                HumanInputActions.IsCapturing || MatchPauseMenu.Active is { IsPaused: true };
            if (suppressCameraInput)
            {
                if (!_chatInputSuppressed)
                {
                    _chatSuppressedYaw = _hasStableAngles ? _lastStableYaw : GetCameraYawDeg();
                    _chatSuppressedPitch = _hasStableAngles ? _lastStablePitch : GetCameraPitchDeg();
                    _chatSuppressedRadial = _hasStableAngles ? _lastStableRadial : _orbital.RadialAxis.Value;
                    _chatInputSuppressed = true;
                }
                SetCameraYawDeg(_chatSuppressedYaw);
                SetCameraPitchDeg(_chatSuppressedPitch);
                _orbital.RadialAxis.Value = _chatSuppressedRadial;
                return;
            }

            if (_chatInputSuppressed)
            {
                if (HumanInputActions.StickLook.ReadValue<Vector2>().sqrMagnitude > 0.01f ||
                    HumanInputActions.MouseLook.ReadValue<Vector2>().sqrMagnitude > 0.01f ||
                    Mathf.Abs(HumanInputActions.Zoom.ReadValue<float>()) > 0.01f)
                {
                    SetCameraYawDeg(_chatSuppressedYaw);
                    SetCameraPitchDeg(_chatSuppressedPitch);
                    _orbital.RadialAxis.Value = _chatSuppressedRadial;
                    return;
                }
                _chatInputSuppressed = false;
            }

            // Mouse input is a per-frame delta; stick input is a rate.
            if (_mode == CameraMode.Normal)
            {
                Vector2 delta = HumanInputActions.MouseLook.ReadValue<Vector2>()
                    + HumanInputActions.StickLook.ReadValue<Vector2>() * (90f * Time.deltaTime);
                var settings = ClientSettingsService.Instance;
                delta *= settings.CameraInputGain;
                _orbital.HorizontalAxis.Value += delta.x * (settings.InvertCameraHorizontal ? -1f : 1f);
                _orbital.VerticalAxis.Value -= delta.y * (settings.InvertCameraVertical ? -1f : 1f);

                float zoom = HumanInputActions.Zoom.ReadValue<float>();
                if (Mathf.Abs(zoom) > 0.001f)
                    _orbital.RadialAxis.Value -= zoom * 0.05f;
            }
            else if (_mode == CameraMode.Frozen)
            {
                SetCameraYawDeg(_frozenYaw);
                SetCameraPitchDeg(_frozenPitch);
            }
            else if (_mode == CameraMode.FreeCursor)
            {
                SetCameraYawDeg(_frozenYaw);
                SetCameraPitchDeg(_frozenPitch);
            }

        }

        private void LateUpdate()
        {
            if (_orbital == null) return;

            if (_chatInputSuppressed)
            {
                // Cinemachine may process its axes after Update; restore the last stable
                // pose again so the opening frame cannot move the camera.
                SetCameraYawDeg(_chatSuppressedYaw);
                SetCameraPitchDeg(_chatSuppressedPitch);
                _orbital.RadialAxis.Value = _chatSuppressedRadial;
                return;
            }

            _lastStableYaw = GetCameraYawDeg();
            _lastStablePitch = GetCameraPitchDeg();
            _lastStableRadial = _orbital.RadialAxis.Value;
            _hasStableAngles = true;
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
                    // Freeze orbital at current angles so it's ready to blend back to
                    // the right position when aiming ends.
                    FreezeAtCurrentAngles();
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
            var settings = ClientSettingsService.Instance;
            deltaDeg *= settings.CameraInputGain;
            _frozenYaw += deltaDeg.x * (settings.InvertCameraHorizontal ? -1f : 1f);
            _frozenPitch -= deltaDeg.y * (settings.InvertCameraVertical ? -1f : 1f);
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

        private Transform _lockFocus;

        /// <summary>
        /// While target-locked (ADR-0018 / issue #127): player-centered lock camera
        /// (souls-like framing). The orbit stays on the PLAYER — mouse orbit, pitch
        /// and scroll zoom are untouched; only the look aim changes. The camera
        /// LOOKS at a point ~25% of the way from the player toward the target, so
        /// the player sits near screen centre with the target beside them — both
        /// fighters visible from any orbit angle. The look point lerps smoothly
        /// (frame-rate independent). Call every FixedUpdate while locked.
        /// </summary>
        public void SetLockFocus(Transform player, Vector3 targetPos)
        {
            if (_cmCam == null || player == null) return;
            if (_lockFocus == null)
            {
                var go = new GameObject("LockFocus");
                // Root-level on purpose: the CinemachineCamera lives on this same
                // object and the orbital rig MOVES it every frame. Parenting the
                // focus under it would couple the focus's world position to the
                // camera position (and vice versa) — a feedback loop that
                // oscillates (visible camera shake while locked).
                _lockFocus = go.transform;
                _lockFocus.position = player.position;
            }
            Vector3 lookPoint = Vector3.Lerp(player.position, targetPos, 0.25f);
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            _lockFocus.position = Vector3.Lerp(_lockFocus.position, lookPoint, k);
            SetTarget(player, _lockFocus);
        }

        /// <summary>
        /// Restore the camera to follow and look at the player (unlocked).
        /// Safe to call every tick.
        /// </summary>
        public void ClearLockFocus(Transform player)
        {
            if (player != null) SetTarget(player);
        }

        /// <summary>
        /// Point the camera at a different look target while keeping the orbit on
        /// the player — used by the target lock to frame both fighters.
        /// </summary>
        private void SetTarget(Transform tracking, Transform lookAt)
        {
            _cmCam.Target = new CameraTarget
            {
                TrackingTarget = tracking,
                LookAtTarget = lookAt
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
            _orbital.VerticalAxis.Value = 15f;
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
