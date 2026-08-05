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
        /// <summary>
        /// GroundVector aim: screen-space offset of a hidden cursor anchored at the character's
        /// screen position. The indicator direction = angle from the character to the cursor, so
        /// both horizontal AND vertical mouse movement rotate it naturally (1:1 with the mouse).
        /// </summary>
        private Vector2 _aimScreenOffset;
        /// <summary>Dead zone radius (px) — below this the aim keeps its last direction.</summary>
        private const float AimScreenDeadZone = 20f;
        /// <summary>Max cursor offset (px) — clamps runaway spin while keeping the angle.</summary>
        private const float AimScreenMaxOffset = 600f;
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
                    if (candidate != null && (candidate.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D or AimMode.GroundVector
                        || candidate.Behavior == AbilityBehavior.ChargeAttack))
                    {
                        spec = candidate;
                        _aimingSlot = slotIdx;
                    }
                }
            }
            // Already attacking/aiming with an aimed ability and key is still held
            if (spec == null && playerState.State is (ActionState.Attacking or ActionState.Aiming) && playerState.AttackSlot > 0)
            {
                byte slotIdx = (byte)(playerState.AttackSlot - 1);
                if (inputController.IsSlotKeyHeld(slotIdx))
                {
                    var candidate = charDef.GetSlotAbility(slotIdx, !playerState.IsGrounded);
                    if (candidate != null && (candidate.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D or AimMode.GroundVector
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
                AimMode.GroundVector    => CameraMode.Frozen,
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

                // Entering Frozen (GroundVector) — freeze the camera; mouse delta rotates the
                // aim direction instead. Inherit the current view yaw as the initial aim.
                if (desired == CameraMode.Frozen)
                {
                    _cameraMount?.FreezeAtCurrentAngles();
                    _lastAimYawRad = _cameraMount?.GetCameraYawRad() ?? 0f;
                    _lastAimPitchRad = 0f;
                    // Hidden cursor starts above the character's screen position → aim = camera forward.
                    _aimScreenOffset = Vector2.up * AimScreenDeadZone;
                }

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
                _aimIndicator.SetVectorMode(false, 0f, 0f);
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
            else if (aimMode == AimMode.GroundVector && _aimIndicator != null)
            {
                // Screen-space aim: the direction follows the mouse like a hidden cursor
                // anchored at the character's screen position. Horizontal AND vertical mouse
                // movement rotate the indicator naturally (1:1 on screen), instead of a raw
                // yaw delta which only used horizontal input.
                _aimScreenOffset += Mouse.current.delta.ReadValue();
                _aimScreenOffset = Vector2.ClampMagnitude(_aimScreenOffset, AimScreenMaxOffset);
                if (_aimScreenOffset.sqrMagnitude > AimScreenDeadZone * AimScreenDeadZone)
                {
                    Vector2 screenDir = _aimScreenOffset.normalized;
                    Vector3 camFwd = _cameraMount?.GetForwardDirection() ?? Vector3.forward;
                    Vector3 camRight = _cameraMount?.GetRightDirection() ?? Vector3.right;
                    Vector3 worldDir = (camFwd * screenDir.y + camRight * screenDir.x).normalized;
                    _lastAimYawRad = Mathf.Atan2(worldDir.x, worldDir.z);
                }
                _lastAimingSlot = _aimingSlot;

                // Dash distance + indicator width come from the spec (matches the server sim).
                float dashDistance = 5f;
                float dashWidth = 1.1f;
                if (spec != null)
                {
                    if (spec.Params != null && spec.Params.TryGetValue("dash_distance", out var dd))
                        dashDistance = dd;
                    if (spec.Stages is { Length: > 0 } && spec.Stages[0].HitboxEvents is { Length: > 0 })
                        dashWidth = spec.Stages[0].HitboxEvents[0].Radius * 2f;
                }

                _aimIndicator.SetVectorMode(true, dashDistance, dashWidth);
                _aimIndicator.SetAiming(true);
                _aimIndicator.SetVectorAim(_lastAimYawRad);
                var (yawRad2, distCm2) = _aimIndicator.GetAimInput();
                ctx = new AimContext
                {
                    IsAiming      = true,
                    AimYawRad     = yawRad2,
                    AimDistanceCm = distCm2,
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
                else if (_lastAimingSlot > 0 && playerState.State is (ActionState.Attacking or ActionState.Aiming)
                    && playerState.AttackSlot == (byte)(_lastAimingSlot + 1))
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

            ShowCrosshair = aimMode is AimMode.GroundCursor or AimMode.CameraForward3D;
            return ctx;
        }
    }
}
