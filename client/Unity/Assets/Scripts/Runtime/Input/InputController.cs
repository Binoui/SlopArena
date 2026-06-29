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
    /// 1. Human input — reads Keyboard.current / Mouse.current directly
    /// 2. AI input — injected via InjectAI() for NPCs
    ///
    /// Call Poll() at the start of each frame (Update or FixedUpdate)
    /// before accessing state or calling BuildInputState().
    /// </summary>
    public class InputController : MonoBehaviour
    {
        // ── Frame state (set by Poll) ──
        /// <summary>Pending jump: set by Poll, consumed by BuildInputState.</summary>
        private bool _pendingJump;
        /// <summary>Pending dash: set by Poll, consumed by BuildInputState.</summary>
        private bool _pendingDash;
        /// <summary>Whether the Q key is currently held down (for aiming release detection).</summary>
        public bool IsQKeyHeld { get; private set; }
        /// <summary>Whether the right mouse button is currently held down (for RMB charge).</summary>
        public bool IsRmbHeld { get; private set; }
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
        /// Slot press: LMB=1, RMB=2, Q=3, E=4, R=5, F=6.
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
            if (kb.spaceKey.wasPressedThisFrame) _pendingJump = true;
            if (kb.shiftKey.wasPressedThisFrame) _pendingDash = true;
            // Ability slot presses (only one per frame — priority order)
            if (mouse.leftButton.wasPressedThisFrame)
                _pendingSlotPress = 1;
            else if (mouse.rightButton.wasPressedThisFrame)
                _pendingSlotPress = 2;
            else if (kb.qKey.wasPressedThisFrame)
                _pendingSlotPress = 3;
            else if (kb.eKey.wasPressedThisFrame)
                _pendingSlotPress = 4;
            else if (kb.rKey.wasPressedThisFrame)
                _pendingSlotPress = 5;
            else if (kb.fKey.wasPressedThisFrame)
                _pendingSlotPress = 6;

            // Track held keys for aiming release detection
            if (!_aiControlled)
            {
                IsQKeyHeld = kb.qKey.isPressed;
                IsRmbHeld = mouse != null && mouse.rightButton.isPressed;
            }
            else
            {
                IsQKeyHeld = false;
                IsRmbHeld = false;
            }
        }

        /// <summary>
        /// Returns and clears the pending slot press byte.
        /// Call after BuildInputState() to pass the value, then consume here.
        /// </summary>
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
        /// Returns AI input if AI-controlled, otherwise reads WASD from Keyboard.
        /// </summary>
        public Vector2 GetMovement()
        {
            if (_aiControlled)
                return new Vector2(_aiInput.MoveX, _aiInput.MoveY);

            var kb = Keyboard.current;
            float x = 0f;
            float y = 0f;
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.wKey.isPressed) y += 1f;
            if (kb.sKey.isPressed) y -= 1f;
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
            bool isAiming,
            byte pendingSlotPress,
            float? abilityAimYawRad,
            ushort? abilityAimDistance,
            Func<bool>? canMove)
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
                input.Crouch = false;
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

            // Build raw camera-relative direction from WASD
            var kb = Keyboard.current;
            Vector3 rawDir = Vector3.zero;
            if (kb.wKey.isPressed) rawDir += camForward;
            if (kb.sKey.isPressed) rawDir -= camForward;
            if (kb.aKey.isPressed) rawDir -= camRight;
            if (kb.dKey.isPressed) rawDir += camRight;

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
            input.Down = moveDirection.z < -0.3f;
            input.Left = moveDirection.x < -0.3f;
            input.Right = moveDirection.x > 0.3f;
            input.Crouch = kb != null && kb.ctrlKey.isPressed;
            input.ActiveSlot = pendingSlotPress;
            input.IsAiming = isAiming;

            // Facing yaw from body rotation
            float deg = bodyYawDeg;
            input.FacingYaw = (short)Math.Clamp(deg * 100f, -32768f, 32767f);

            // Aim yaw from camera (combat facing), overridden by active ability
            float aimDeg = camera != null ? camera.GetCameraYawDeg() : deg;
            input.AimYaw = (short)Math.Clamp(aimDeg * 100f, -32768f, 32767f);
            input.AimDistance = 0;

            // Active ability overrides aim data
            if (abilityAimYawRad.HasValue)
            {
                float throwDeg = abilityAimYawRad.Value * Mathf.Rad2Deg;
                input.AimYaw = (short)Math.Clamp(throwDeg * 100f, -32768f, 32767f);
            }
            if (abilityAimDistance.HasValue)
                input.AimDistance = abilityAimDistance.Value;
            // FSM movement gate: zero out input if state disallows movement
            if (canMove != null && !canMove())
            {
                input.MoveX = 0f;
                input.MoveY = 0f;
                input.Jump = false;
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

            return (input, moveDirection, snappedInputDirection);
        }
    }
}
