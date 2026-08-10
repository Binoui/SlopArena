#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using SlopArena.Shared;
using SlopArena.Client.Camera;
using System;

namespace SlopArena.Client.Input
{
    /// <summary>
    /// Centralized input controller for SlopArena.
    /// Polls Unity InputSystem once per frame and builds InputState for the sim.
    ///
    /// Supports two modes:
    /// 1. Human input — reads Keyboard.current / Mouse.current through the remappable
    ///    <see cref="InputBindings"/> config (ADR-0016: ZQSD/WASD movement, Space jump,
    ///    Shift dodge, C burst, X fast-fall, 10 move slots on 1-5/A/E/R/F + LMB/RMB).
    /// 2. AI input — injected via InjectAI() for NPCs
    ///
    /// Call Poll() at the start of each frame (Update or FixedUpdate)
    /// before accessing state or calling BuildInputState().
    /// </summary>
    public class InputController : MonoBehaviour
    {
        /// <summary>Remappable bindings. Assign an asset in the inspector or drop one in
        /// Resources/InputBindings; without either the layout-preset defaults apply.</summary>
        [SerializeField] private InputBindings _bindings;

        private void Awake()
        {
            if (_bindings == null)
                _bindings = Resources.Load<InputBindings>("InputBindings");
        }

        /// <summary>Resolved key for a bindable action (override or layout default).</summary>
        private Key Bind(BindableAction action)
            => _bindings != null ? _bindings.GetKey(action) : InputBindings.DefaultKey(action);

        // ── Frame state (set by Poll) ──
        /// <summary>Pending jump: set by Poll, consumed by BuildInputState.</summary>
        private bool _pendingJump;
        /// <summary>Pending dash: set by Poll, consumed by BuildInputState.</summary>
        private bool _pendingDash;
        /// <summary>Pending burst: set by Poll, consumed by BuildInputState (ADR-0014).</summary>
        private bool _pendingBurst;
        /// <summary>
        /// Returns true if the key/button for the given slot index (0-based) is currently held.
        /// Slot 0 = LMB, 1 = RMB, 2 = key "1", 3 = E, 4 = R, 5 = F, 6-9 = keys "2"-"5", 10 = A.
        /// Follows the remapped bindings (ADR-0016).
        /// </summary>
        public bool IsSlotKeyHeld(byte slotIdx) => slotIdx switch
        {
            0 => Mouse.current != null && Mouse.current.leftButton.isPressed,
            1 => Mouse.current != null && Mouse.current.rightButton.isPressed,
            _ => Keyboard.current != null && Keyboard.current[SlotAction(slotIdx)].isPressed,
        };

        /// <summary>Slot index (0-based) → the bindable action that triggers it (slots 2-10).</summary>
        private static BindableAction SlotAction(byte slotIdx) => slotIdx switch
        {
            2 => BindableAction.Slot1,
            3 => BindableAction.SlotE,
            4 => BindableAction.SlotR,
            5 => BindableAction.SlotF,
            6 => BindableAction.Slot2,
            7 => BindableAction.Slot3,
            8 => BindableAction.Slot4,
            9 => BindableAction.Slot5,
            10 => BindableAction.SlotA,
            _ => BindableAction.Slot1,
        };

        // ── AI injection ──
        private bool _aiControlled;
        private InputState _aiInput;

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
        }

        /// <summary>
        /// Clear AI control (switch back to human input).
        /// </summary>
        public void ClearAI()
        {
            _aiControlled = false;
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
        /// Slot presses (ActiveSlot wire values): LMB=1, RMB=2, key"1"=3, E=4, R=5, F=6,
        /// key"2"=7, key"3"=8, key"4"=9, key"5"=10, A=11 (AbilitySlots, ADR-0016).
        /// Consume via <see cref="ConsumePendingSlotPress"/> after BuildInputState.
        /// </summary>
        public void Poll()
        {
            if (_aiControlled)
            {
                // AI-driven: use injected input
                _pendingJump = _aiInput.Jump;
                _pendingDash = _aiInput.Dash;
                return;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb[Bind(BindableAction.Jump)].wasPressedThisFrame) _pendingJump = true;
            if (kb[Bind(BindableAction.Dash)].wasPressedThisFrame) _pendingDash = true;
            if (kb[Bind(BindableAction.Burst)].wasPressedThisFrame) _pendingBurst = true;
            // Ability slot presses (only one per frame — priority order)
            if (mouse.leftButton.wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.Lmb;
            else if (mouse.rightButton.wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.Rmb;
            else if (kb[Bind(BindableAction.Slot1)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.Slot1;
            else if (kb[Bind(BindableAction.SlotE)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.E;
            else if (kb[Bind(BindableAction.SlotR)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.R;
            else if (kb[Bind(BindableAction.SlotF)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.F;
            else if (kb[Bind(BindableAction.Slot2)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.Slot2;
            else if (kb[Bind(BindableAction.Slot3)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.Slot3;
            else if (kb[Bind(BindableAction.Slot4)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.Slot4;
            else if (kb[Bind(BindableAction.Slot5)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.Slot5;
            else if (kb[Bind(BindableAction.SlotA)].wasPressedThisFrame)
                _pendingSlotPress = AbilitySlots.A;
        }

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
            _pendingSlotPress = 0;
        }

        public byte ConsumePendingSlotPress()
        {
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

            var kb = Keyboard.current;
            float x = 0f;
            float y = 0f;
            if (kb[Bind(BindableAction.MoveLeft)].isPressed) x -= 1f;
            if (kb[Bind(BindableAction.MoveRight)].isPressed) x += 1f;
            if (kb[Bind(BindableAction.MoveUp)].isPressed) y += 1f;
            if (kb[Bind(BindableAction.MoveDown)].isPressed) y -= 1f;
            return new Vector2(x, y);
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

            // ── NPC path: use injected AI input directly ──
            if (isNPC && _aiControlled)
            {
                var move = GetMovement();
                input.MoveX = move.x;
                input.MoveY = move.y;
                input.Up = move.y > 0.3f;
                input.Down = move.y < -0.3f;
                input.Left = move.x < -0.3f;
                input.Right = move.x > 0.3f;
                input.JumpHeld = _aiInput.JumpHeld;
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

            // ── Player path: camera-relative 8-direction input ──
            Vector3 camForward = Vector3.forward;
            Vector3 camRight = Vector3.right;
            if (camera != null)
            {
                camForward = camera.GetForwardDirection();
                camRight = camera.GetRightDirection();
            }

            // Build raw camera-relative direction from the bound movement keys
            var kb = Keyboard.current;
            Vector3 rawDir = Vector3.zero;
            if (kb[Bind(BindableAction.MoveUp)].isPressed) rawDir += camForward;
            if (kb[Bind(BindableAction.MoveDown)].isPressed) rawDir -= camForward;
            if (kb[Bind(BindableAction.MoveLeft)].isPressed) rawDir -= camRight;
            if (kb[Bind(BindableAction.MoveRight)].isPressed) rawDir += camRight;

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

            // Populate InputState
            input.MoveX = moveDirection.x;
            input.MoveY = moveDirection.z;
            input.Up = moveDirection.z > 0.3f;
            // Down (fast fall, issue #116): driven by the DEDICATED FastFall key (X by
            // default) — NOT by backward movement. Drifting backward must never fast-fall.
            // Left un-gated by canMove: the sim gates it (airborne + not hitstun), and it
            // must work through air attacks.
            input.Down = kb[Bind(BindableAction.FastFall)].isPressed;
            input.Left = moveDirection.x < -0.3f;
            input.Right = moveDirection.x > 0.3f;
            // Short hop (issue #116): Jump is the press edge; JumpHeld is the physical
            // hold state the sim counts for the release window.
            input.JumpHeld = kb[Bind(BindableAction.Jump)].isPressed;
            // Burst fires even when the FSM gates movement — it must work during
            // hitstop/hitstun (the gate zeroes Jump/Dash only).
            input.Burst = _pendingBurst;
            _pendingBurst = false;
            input.ActiveSlot = pendingSlotPress;
            input.IsAiming = aimCtx.IsAiming;

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
