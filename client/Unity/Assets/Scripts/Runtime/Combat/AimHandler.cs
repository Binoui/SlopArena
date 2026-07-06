using SlopArena.Shared;
using SlopArena.Client.Camera;
using SlopArena.Client.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Owns the full aim pipeline for the local player each FixedUpdate tick:
    ///   1. Resolves which AbilitySpec is currently active (just-pressed or held during attack)
    ///   2. Transitions CameraMount mode (Normal / Frozen / FreeCursor)
    ///   3. Updates AimIndicator
    ///   4. Returns AimContext for InputController.BuildInputState
    ///
    /// TrainingMatch (and future match types) call Init() once then Evaluate() each tick.
    /// Zero aim logic leaks back to the caller.
    /// </summary>
    public class AimHandler : MonoBehaviour
    {
        [SerializeField] private AimIndicator _aimIndicator;
        [SerializeField] private CameraMount _cameraMount;

        private CameraMode _activeMode = CameraMode.Normal;
        private byte _aimingSlot;
        private float _aimPitchOffsetDeg;

        /// <summary>True when a CameraForward3D ability is active — caller draws the crosshair.</summary>
        public bool ShowCrosshair { get; private set; }

        /// <summary>
        /// Wire camera into AimIndicator once the scene is ready.
        /// Call from OnMatchStart after the camera hierarchy exists.
        /// </summary>
        public void Init(CameraMount cameraMount, UnityEngine.Camera renderCamera, Transform characterTransform, float capsuleHeight)
        {
            _cameraMount = cameraMount;
            if (_aimIndicator != null)
            {
                _aimIndicator.SetCamera(renderCamera);
                _aimIndicator.SetCharacter(characterTransform, capsuleHeight);
            }
            _cameraMount?.SetMode(CameraMode.Normal);
            _activeMode = CameraMode.Normal;
        }

        /// <summary>
        /// Resolve aim state for this tick.
        /// Figures out the active aimed ability from player state + just-pressed slot,
        /// drives camera and indicator, returns an AimContext for BuildInputState.
        /// </summary>
        public AimContext Evaluate(
            CharacterState playerState,
            byte pendingSlotPress,
            CharacterDefinition charDef,
            InputController inputController)
        {
            // ── 1. Resolve active aim spec ──
            AbilitySpec? spec = null;
            _aimingSlot = 0;

            // A slot was just pressed and its key is still held down
            if (pendingSlotPress > 0)
            {
                byte slotIdx = (byte)(pendingSlotPress - 1);
                if (inputController.IsSlotKeyHeld(slotIdx))
                {
                    var candidate = charDef.GetSlotAbility(slotIdx, !playerState.IsGrounded);
                    if (candidate?.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D)
                    {
                        spec = candidate;
                        _aimingSlot = slotIdx;
                    }
                }
            }

            // Already attacking with an aimed ability and key is still held
            if (spec == null && playerState.State == ActionState.Attacking && playerState.AttackSlot > 0)
            {
                byte slotIdx = (byte)(playerState.AttackSlot - 1);
                if (inputController.IsSlotKeyHeld(slotIdx))
                {
                    var candidate = charDef.GetSlotAbility(slotIdx, !playerState.IsGrounded);
                    if (candidate?.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D)
                    {
                        spec = candidate;
                        _aimingSlot = slotIdx;
                    }
                }
            }

            AimMode aimMode = spec?.AimMode ?? AimMode.None;

            // ── 2. Drive camera mode (transitions only) ──
            CameraMode desired = aimMode switch
            {
                AimMode.GroundCursor    => CameraMode.FreeCursor,
                AimMode.CameraForward3D => CameraMode.Frozen,
                _                       => CameraMode.Normal,
            };

            if (desired != _activeMode)
            {
                // Capture camera angles before any mode switch that freezes orientation
                if (desired is CameraMode.Frozen or CameraMode.FreeCursor)
                    _cameraMount?.FreezeAtCurrentAngles();
                // Reset reticle pitch offset when entering camera-relative aim
                if (desired == CameraMode.Frozen)
                    _aimPitchOffsetDeg = 0f;
                _cameraMount?.SetMode(desired);
                _activeMode = desired;
            }

            // ── 3. Collect aim data ──
            AimContext ctx = AimContext.None;

            if (aimMode == AimMode.GroundCursor && _aimIndicator != null)
            {
                _aimIndicator.SetAiming(true);
                _aimIndicator.UpdateAim();
                var (yawRad, distCm) = _aimIndicator.GetAimInput();
                ctx = new AimContext
                {
                    IsAiming      = true,
                    AimYawRad     = yawRad,
                    AimDistanceCm = distCm,
                };
            }
            else
            {
                if (_aimIndicator != null) _aimIndicator.SetAiming(false);

                if (aimMode == AimMode.CameraForward3D && _cameraMount != null)
                {
                    // Accumulate mouse Y as pitch offset (camera stays frozen)
                    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                    if (Mathf.Abs(mouseDelta.y) > 0.001f)
                        _aimPitchOffsetDeg -= mouseDelta.y * 0.1f;

                    ctx = new AimContext
                    {
                        IsAiming    = true,
                        AimYawRad   = _cameraMount.GetCameraYawRad(),
                        AimPitchRad = -(_cameraMount.GetCameraPitchDeg() + _aimPitchOffsetDeg) * Mathf.Deg2Rad,
                    };
                }
            }

            ShowCrosshair = aimMode != AimMode.None;
            return ctx;
        }
    }
}
