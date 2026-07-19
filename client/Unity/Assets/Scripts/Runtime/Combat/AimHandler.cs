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
    ///   2. Transitions CameraMount mode (Normal / FreeCursor / Aiming)
    ///   3. Activates/deactivates AimCameraMount for CameraForward3D abilities
    ///   4. Updates AimIndicator for GroundCursor abilities
    ///   5. Returns AimContext for InputController.BuildInputState
    ///
    /// TrainingMatch (and future match types) call Init() once then Evaluate() each tick.
    /// Zero aim logic leaks back to the caller.
    /// </summary>
    public class AimHandler : MonoBehaviour
    {
        [SerializeField] private AimIndicator _aimIndicator;
        [SerializeField] private CameraMount _cameraMount;
        [SerializeField] private AimCameraMount _aimCameraMount;
        [SerializeField] private float _aimSensitivity = 0.15f;

        private CameraMode _activeMode = CameraMode.Normal;
        private byte _aimingSlot;
        private Transform _characterTransform;
        /// <summary>Cached aim values — persist after key release so server gets right direction during fire delay.</summary>
        private float _lastAimYawRad;
        private float _lastAimPitchRad;
        private byte _lastAimingSlot;
        /// <summary>True when a CameraForward3D ability is active — caller draws the crosshair.</summary>
        public bool ShowCrosshair { get; private set; }

        /// <summary>
        /// Wire camera into AimIndicator once the scene is ready.
        /// Call from OnMatchStart after the camera hierarchy exists.
        /// </summary>
        public void Init(CameraMount cameraMount, UnityEngine.Camera renderCamera, Transform characterTransform, float capsuleHeight)
        {
            _cameraMount = cameraMount;
            _characterTransform = characterTransform;
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
                    if (candidate != null && (candidate.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D
                        || candidate.Behavior == AbilityBehavior.ChargeAttack))
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
                    if (candidate != null && (candidate.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D
                        || candidate.Behavior == AbilityBehavior.ChargeAttack))
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
                AimMode.CameraForward3D => CameraMode.Aiming,
                _                       => CameraMode.Normal,
            };

            if (desired != _activeMode)
            {
                // Leaving Aiming — deactivate aim camera so Cinemachine blends back to orbital
                if (_activeMode == CameraMode.Aiming)
                    _aimCameraMount?.Deactivate();

                // Entering GroundCursor — freeze orbital at current angles (cursor controls ground marker)
                if (desired == CameraMode.FreeCursor)
                    _cameraMount?.FreezeAtCurrentAngles();

                // Entering Aiming — activate aim camera, inherit current yaw + zoom distance
                if (desired == CameraMode.Aiming && _characterTransform != null)
                {
                    float yawRad      = _cameraMount?.GetCameraYawRad() ?? 0f;
                    float orbitRadius = _cameraMount?.GetOrbitRadius()  ?? 2.5f;
                    _aimCameraMount?.Activate(_characterTransform, yawRad, orbitRadius);
                    _lastAimYawRad   = yawRad;
                    _lastAimPitchRad = 0f;
                }

                _cameraMount?.SetMode(desired);
                _activeMode = desired;
            }
            // ── 3. Collect aim data ──
            AimContext ctx = AimContext.None;
            bool isCharging = spec != null && spec.Behavior == AbilityBehavior.ChargeAttack;

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

                if (aimMode == AimMode.CameraForward3D && _aimCameraMount != null)
                {
                    Vector2 delta = Mouse.current.delta.ReadValue();
                    _aimCameraMount.Tick(_characterTransform);
                    _aimCameraMount.ApplyMouseDelta(delta, _aimSensitivity);

                    _lastAimYawRad   = _aimCameraMount.GetAimYawRad();
                    _lastAimPitchRad = _aimCameraMount.GetAimPitchRad();
                    _lastAimingSlot  = _aimingSlot;

                    ctx = new AimContext
                    {
                        IsAiming    = true,
                        AimYawRad   = _lastAimYawRad,
                        AimPitchRad = _lastAimPitchRad,
                    };
                }
                else if (isCharging && _aimingSlot > 0 && inputController.IsSlotKeyHeld(_aimingSlot))
                {
                    // ChargeAttack: signal IsAiming=true while key held, no cursor/camera changes
                    ctx = new AimContext { IsAiming = true };
                }
                else if (_lastAimingSlot > 0 && playerState.State == ActionState.Attacking && playerState.AttackSlot == (byte)(_lastAimingSlot + 1))
                {
                    // Key released but server hasn't fired yet — send last known aim direction
                    ctx = new AimContext
                    {
                        IsAiming    = false,
                        AimYawRad   = _lastAimYawRad,
                        AimPitchRad = _lastAimPitchRad,
                    };
                }
            }

            ShowCrosshair = aimMode != AimMode.None;
            return ctx;
        }
    }
}
