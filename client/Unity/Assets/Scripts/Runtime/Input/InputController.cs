#nullable enable
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Camera;
using SlopArena.Client.UI;
using System;

namespace SlopArena.Client.Input
{
    /// <summary>
    /// Centralized human input adapter. Polls the shared Input System action map once
    /// per frame, buffers discrete edges, and builds the existing InputState contract.
    /// AI input remains injected separately through InjectAI().
    /// </summary>
    public class InputController : MonoBehaviour
    {
        private void Awake()
        {
            var settings = ClientSettingsService.Instance;
            HumanInputActions.Initialize(settings.BindingOverrides);
        }

        // ── Frame state (set by Poll) ──
        /// <summary>Pending jump: set by Poll, consumed by BuildInputState.</summary>
        private bool _pendingJump;
        /// <summary>Pending dash: set by Poll, consumed by BuildInputState.</summary>
        private bool _pendingDash;
        /// <summary>Pending burst: set by Poll, consumed by BuildInputState (ADR-0014).</summary>
        private bool _pendingBurst;
        /// <summary>Pending dedicated Down press edge: set by Poll/InjectAI and consumed once by BuildInputState.</summary>
        private bool _pendingDownPressed;
        /// <summary>
        /// Prevent a key held through chat/pause/focus suppression from creating a new Down edge
        /// when gameplay input resumes. Cleared only after the bound key is released.
        /// </summary>
        private bool _downReleaseRequired;
        /// <summary>Pending LMB facing snap (ADR-0017, issue #126): set by Poll on the LMB
        /// press edge, consumed by BuildInputState. One tick of FaceToCamera.</summary>
        private bool _pendingFaceToCamera;
        /// <summary>Pending RMB target-lock toggle (ADR-0018, issue #127): set by Poll on
        /// the RMB press edge, consumed by BuildInputState. One tick of ToggleLock.</summary>
        private bool _pendingToggleLock;
        /// <summary>True if the action assigned to the canonical slot index is held.</summary>
        public bool IsSlotKeyHeld(byte slotIdx)
        {
            if (_aiControlled)
                return _aiInput.ActiveSlot == slotIdx + 1;
            if (HumanInputBlocked) return false;
            string action = SlotAction(slotIdx);
            return action != null && HumanInputActions.Get(action).IsPressed();
        }

        private static string SlotAction(byte slotIdx) => slotIdx switch
        {
            2 => "Slot1", 3 => "SlotE", 4 => "SlotR", 5 => "SlotF",
            6 => "Slot2", 7 => "Slot3", 8 => "Slot4", 10 => "SlotA",
            _ => null,
        };

        private static bool HumanInputSuppressed =>
            ChatInputGate.SuppressGameplay || SettingsOverlay.Active is { IsOpen: true } ||
            SettingsOverlay.ClosedThisFrame || HumanInputActions.IsCapturing ||
            MatchPauseMenu.Active is { IsPaused: true };

        private bool _humanReleaseRequired;
        private bool HumanInputBlocked => HumanInputSuppressed || _humanReleaseRequired;

        public void RequireReleaseBeforeHumanInput()
        {
            _humanReleaseRequired = true;
            ClearPendingFrameState();
        }

        private bool AnyHumanControlHeld()
        {
            foreach (var name in HumanInputActions.RebindableActions)
                if (HumanInputActions.Get(name).IsPressed()) return true;
            return HumanInputActions.Get("MoveStick").ReadValue<Vector2>().sqrMagnitude > 0.01f
                || HumanInputActions.StickLook.ReadValue<Vector2>().sqrMagnitude > 0.01f
                || HumanInputActions.MouseLook.ReadValue<Vector2>().sqrMagnitude > 0.01f
                || Mathf.Abs(HumanInputActions.Zoom.ReadValue<float>()) > 0.01f;
        }

        // ── AI injection ──
        private bool _aiControlled;
        private InputState _aiInput;
        /// <summary>
        /// True while the bound fast-fall key is held. Kept separate from InputState.Down so
        /// release suppression can be enforced across pause/focus transitions.
        /// </summary>
        private bool IsDownHeld() => HumanInputActions.Get("Down").IsPressed();


        private void OnApplicationFocus(bool hasFocus)
        {
            ClearPendingFrameState();
            // InputSystem can reset device state while unfocused. Re-arm on both
            // loss and regain so background polling cannot release this guard.
            _downReleaseRequired = true;
        }

        private void OnApplicationPause(bool paused)
        {
            ClearPendingFrameState();
            _downReleaseRequired = true;
        }

        private void OnDisable()
        {
            ClearPendingFrameState();
            _downReleaseRequired = true;
        }

        // ════════════════════════════════════════════════════════════════
        //  AI injection
        // ════════════════════════════════════════════════════════════════

        // ── Slot press (set by Poll, consumed via ConsumePendingSlotPress) ──
        private byte _pendingSlotPress;

        // ════════════════════════════════════════════════════════════════
        /// <summary>
        /// Inject synthetic input from AI (for NPCs).
        /// Must be called every frame before Poll() if AI-controlled.
        /// </summary>
        public void InjectAI(InputState input)
        {
            _aiControlled = true;
            _aiInput = input;
            // Injection supplies a new edge; BuildInputState consumes it once. Do not
            // derive Down from MoveY, since backward-moving NPCs must not fast-fall.
            _pendingDownPressed = input.DownPressed;
        }

        /// <summary>
        /// Clear AI control (switch back to human input).
        /// </summary>
        public void ClearAI()
        {
            _aiControlled = false;
            _pendingDownPressed = false;
        }

        public bool IsAIControlled() => _aiControlled;

        // ════════════════════════════════════════════════════════════════
        //  Polling
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read current input and store into frame state.
        /// Call once per frame before BuildInputState() or any property access.
        /// Uses AI input if InjectAI() was called, otherwise reads from InputSystem.
        ///
        /// ActiveSlot retains existing wire values: 1→3, E→4, R→5, F→6, 2→7,
        /// 3→8, 4→9, A→11. LMB/RMB are utility actions, not attack Slots.
        /// Consume via <see cref="ConsumePendingSlotPress"/> after BuildInputState.
        /// </summary>
        public void Poll()
        {
            if (_aiControlled)
            {
                // AI-driven: use injected input. DownPressed was latched by InjectAI and is
                // consumed by BuildInputState exactly once for this supplied input.
                _pendingJump = _aiInput.Jump;
                _pendingDash = _aiInput.Dash;
                return;
            }

            if (HumanInputSuppressed)
            {
                ClearPendingFrameState();
                return;
            }
            if (_humanReleaseRequired)
            {
                if (AnyHumanControlHeld()) return;
                _humanReleaseRequired = false;
            }

            var jump = HumanInputActions.Get("Jump");
            var dash = HumanInputActions.Get("Dash");
            var burst = HumanInputActions.Get("Burst");
            var down = HumanInputActions.Get("Down");
            if (jump.WasPressedThisFrame()) _pendingJump = true;
            if (dash.WasPressedThisFrame()) _pendingDash = true;
            if (burst.WasPressedThisFrame()) _pendingBurst = true;
            if (_downReleaseRequired)
            {
                if (!down.IsPressed()) _downReleaseRequired = false;
            }
            else if (down.WasPressedThisFrame()) _pendingDownPressed = true;

            if (HumanInputActions.Get("FaceToCamera").WasPressedThisFrame())
                _pendingFaceToCamera = true;
            else if (HumanInputActions.Get("ToggleLock").WasPressedThisFrame())
                _pendingToggleLock = true;
            else
            {
                foreach (var (action, slot) in SlotActions)
                    if (HumanInputActions.Get(action).WasPressedThisFrame())
                    {
                        _pendingSlotPress = slot;
                        break;
                    }
            }
        }

        private static readonly (string action, byte slot)[] SlotActions =
        {
            ("Slot1", AbilitySlots.Slot1), ("SlotE", AbilitySlots.E), ("SlotR", AbilitySlots.R),
            ("SlotF", AbilitySlots.F), ("Slot2", AbilitySlots.Slot2), ("Slot3", AbilitySlots.Slot3),
            ("Slot4", AbilitySlots.Slot4), ("SlotA", AbilitySlots.A),
        };

        /// <summary>
        /// Discard buffered jump/dash/slot presses without consuming them. Called
        /// when pausing so stale presses don't fire on the first frame after
        /// resume (issue #77).
        /// </summary>
        public void ClearPendingFrameState()
        {
            _pendingJump = false;
            _pendingDash = false;
            _pendingBurst = false;
            _pendingDownPressed = false;
            _pendingFaceToCamera = false;
            _pendingToggleLock = false;
            _pendingSlotPress = 0;
            if (IsDownHeld())
                _downReleaseRequired = true;
        }

        public byte ConsumePendingSlotPress()
        {
            if (!_aiControlled && HumanInputBlocked)
            {
                _pendingSlotPress = 0;
                return 0;
            }

            byte slot = _pendingSlotPress;
            if (slot > 0) {
                Debug.Log($"[Input] ConsumePendingSlotPress: {slot}");
                _pendingSlotPress = 0;
            }
            return slot;
        }

        // ════════════════════════════════════════════════════════════════
        //  Movement
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get raw movement input for current frame.
        /// Returns AI input if AI-controlled, otherwise reads the bound movement keys.
        /// </summary>
        public Vector2 GetMovement()
        {
            if (_aiControlled)
                return new Vector2(_aiInput.MoveX, _aiInput.MoveY);

            if (HumanInputBlocked) return Vector2.zero;
            float x = (HumanInputActions.Get("MoveRight").IsPressed() ? 1f : 0f)
                    - (HumanInputActions.Get("MoveLeft").IsPressed() ? 1f : 0f);
            float y = (HumanInputActions.Get("MoveUp").IsPressed() ? 1f : 0f)
                    - (HumanInputActions.Get("MoveDown").IsPressed() ? 1f : 0f);
            Vector2 stick = HumanInputActions.Get("MoveStick").ReadValue<Vector2>();
            return Vector2.ClampMagnitude(new Vector2(x, y) + stick, 1f);
        }

        // ════════════════════════════════════════════════════════════════
        //  BuildInputState
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a full InputState for one frame, including camera-relative direction math,
        /// 8-direction snap, and FSM movement gate.
        ///
        /// Parameters owned by the caller (PlayerController):
        ///   bodyYaw = transform.eulerAngles.y (in degrees)
        ///   pendingSlotPress = from ConsumePendingSlotPress()
        ///   abilityAimYaw / abilityAimDistance = set by active ability Tick
        ///
        /// Returns (InputState, world-space moveDirection, camera-relative snappedInputDirection).
        /// </summary>
        public (InputState input, Vector3 moveDirection, Vector2 snappedInputDirection) BuildInputState(
            Camera.CameraMount? camera,
            float bodyYawDeg,
            bool isNPC,
            byte pendingSlotPress,
            Camera.AimContext aimCtx,
            Func<bool>? canMove,
            byte targetEntityId = 0)
        {
            var input = new InputState();

            if (isNPC && _aiControlled)
            {
                var move = GetMovement();
                input.MoveX = move.x;
                input.MoveY = move.y;
                input.Up = move.y > 0.3f;
                // Down is an explicit injected hold, not an interpretation of backward
                // movement. The edge is consumed independently of movement gating.
                input.Down = _aiInput.Down;
                input.DownPressed = _pendingDownPressed;
                _pendingDownPressed = false;
                input.Left = move.x < -0.3f;
                input.Right = move.x > 0.3f;
                input.JumpHeld = _aiInput.JumpHeld;
                input.FaceToCamera = _aiInput.FaceToCamera;
                input.ToggleLock = _aiInput.ToggleLock;
                input.ActiveSlot = pendingSlotPress;
                if (_pendingJump)
                {
                    input.Jump = true;
                    _pendingJump = false;
                }
                if (_pendingDash)
                {
                    input.Dash = true;
                    _pendingDash = false;
                }

                Vector3 moveDir = new Vector3(move.x, 0f, move.y).normalized;
                Vector2 snappedDir = new Vector2(move.x, move.y);
                input.TargetEntityId = targetEntityId;
                input.AimPitch = 0;  // NPCs aim horizontally

                return (input, moveDir, snappedDir);
            }

            if (HumanInputBlocked)
            {
                ClearPendingFrameState();
                input.TargetEntityId = targetEntityId;
                return (input, Vector3.zero, Vector2.zero);
            }

            // ── Player path: camera-relative 8-direction input ──
            Vector3 camForward = Vector3.forward;
            Vector3 camRight = Vector3.right;
            if (camera != null)
            {
                camForward = camera.GetForwardDirection();
                camRight = camera.GetRightDirection();
            }

            // Build raw camera-relative direction from unified digital and stick input.
            Vector2 moveInput = GetMovement();
            Vector3 rawDir = camForward * moveInput.y + camRight * moveInput.x;

            Vector3 moveDirection = Vector3.zero;
            Vector2 snappedInputDirection = Vector2.zero;

            if (rawDir.sqrMagnitude > 0.001f)
            {
                // Convert to camera-relative 2D coordinates
                float rawForward = Vector3.Dot(rawDir, camForward);
                float rawRight = Vector3.Dot(rawDir, camRight);

                // Snap to 8 directions (45-degree increments)
                float angle = MathF.Atan2(rawRight, rawForward);
                const float snapStep = MathF.PI / 4f;
                float snappedAngle = MathF.Round(angle / snapStep) * snapStep;

                float fwd = MathF.Cos(snappedAngle);
                float rgt = MathF.Sin(snappedAngle);

                snappedInputDirection = new Vector2(rgt, fwd);
                moveDirection = (camForward * fwd) + (camRight * rgt);
                moveDirection = moveDirection.normalized;
            }

            input.MoveX = moveDirection.x;
            input.MoveY = moveDirection.z;
            input.Up = moveDirection.z > 0.3f;
            // Dedicated Down remains independent from backward movement.
            input.Down = !_downReleaseRequired && HumanInputActions.Get("Down").IsPressed();
            input.DownPressed = _pendingDownPressed;
            _pendingDownPressed = false;
            input.Left = moveDirection.x < -0.3f;
            input.Right = moveDirection.x > 0.3f;
            input.JumpHeld = HumanInputActions.Get("Jump").IsPressed();
            // Burst fires even when the FSM gates movement — it must work during
            // hitstop/hitstun (the gate zeroes Jump/Dash only).
            input.Burst = _pendingBurst;
            _pendingBurst = false;
            input.ActiveSlot = pendingSlotPress;
            input.IsAiming = aimCtx.IsAiming;
            // Utility edges (ADR-0017/0018): consumed here, one tick each. Set before the
            // FSM gate — a snap/toggle is not movement and must survive a canMove=false.
            input.FaceToCamera = _pendingFaceToCamera;
            _pendingFaceToCamera = false;
            input.ToggleLock = _pendingToggleLock;
            _pendingToggleLock = false;

            // Facing yaw from body rotation
            float deg = bodyYawDeg;
            input.FacingYaw = (short)Math.Clamp(deg * 100f, -32768f, 32767f);

            // Aim yaw: camera default, overridden by active ability
            float aimDeg = camera != null ? camera.GetCameraYawDeg() : deg;
            input.AimYaw = (short)Math.Clamp(aimDeg * 100f, -32768f, 32767f);
            if (aimCtx.AimYawRad.HasValue)
                input.AimYaw = (short)Math.Clamp(aimCtx.AimYawRad.Value * Mathf.Rad2Deg * 100f, -32768f, 32767f);

            // Aim pitch: camera default, overridden by active ability
            float aimPitchDeg = camera != null ? camera.GetCameraPitchDeg() : 0f;
            input.AimPitch = (short)Math.Clamp(aimPitchDeg * 100f, -9000f, 9000f);
            if (aimCtx.AimPitchRad.HasValue)
                input.AimPitch = (short)Math.Clamp(aimCtx.AimPitchRad.Value * Mathf.Rad2Deg * 100f, -9000f, 9000f);

            input.AimDistance = aimCtx.AimDistanceCm ?? 0;
            // FSM movement gate: zero out input if state disallows movement
            if (canMove != null && !canMove())
            {
                input.MoveX = 0f;
                input.MoveY = 0f;
                input.Jump = false;
                input.JumpHeld = false;
                input.Dash = false;
                moveDirection = Vector3.zero;
                snappedInputDirection = Vector2.zero;
            }
            else
            {
                // Gate allows movement — consume pending jump
                if (_pendingJump)
                {
                    input.Jump = true;
                    _pendingJump = false;
                    Debug.Log("[Input] _pendingJump consumed -> input.Jump=true");
                }
                if (_pendingDash)
                {
                    input.Dash = true;
                    _pendingDash = false;
                    Debug.Log("[Input] _pendingDash consumed -> input.Dash=true");
                }
            }

            input.TargetEntityId = targetEntityId;
            return (input, moveDirection, snappedInputDirection);
        }
    }
}
