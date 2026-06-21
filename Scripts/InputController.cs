#nullable enable
using Godot;
using System;
using SlopArena.Shared;

/// <summary>
/// Centralized input state for the current frame.
/// Polled once per frame in PlayerController._Process.
/// States read from this instead of calling Godot Input directly.
///
/// Supports two modes:
/// 1. Human input: Poll() reads from Godot Input
/// 2. AI input: InjectAI() overrides with synthetic input
/// </summary>
public sealed class InputController
{
    /// <summary>
    /// ── Frame state ──
    /// </summary>
    public bool JumpJustPressed { get; set; }
    public bool DashJustPressed { get; set; }

    /// <summary>
    /// ── AI injection ──
    /// </summary>
    private bool _aiControlled = false;
    private InputState _aiInput;

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

    /// <summary>
    /// Read current input and store into frame state.
    /// Call once per frame (in _Process) before StateMachine._Process.
    /// Uses AI input if InjectAI() was called, otherwise reads from Godot Input.
    /// </summary>
    public void Poll()
    {
        if (_aiControlled)
        {
            // AI-driven: use injected input
            JumpJustPressed = _aiInput.Jump;
            DashJustPressed = _aiInput.Dash;
        }
        else
        {
            // Human-driven: read Godot input
            JumpJustPressed = Input.IsActionJustPressed("jump");
            DashJustPressed = Input.IsActionJustPressed("dash");
        }
    }

    /// <summary>
    /// Get movement input for current frame.
    /// Returns AI input if AI-controlled, otherwise reads Godot Input.
    /// </summary>
    public (float moveX, float moveY) GetMovement()
    {
        if (_aiControlled)
        {
            return (_aiInput.MoveX, _aiInput.MoveY);
        }
        else
        {
            // Human input from Godot
            float x = Input.GetAxis("move_left", "move_right");
            float y = Input.GetAxis("move_forward", "move_backward");
            return (x, y);
        }
    }

    /// <summary>
    /// Check if AI mode is active.
    /// </summary>
    public bool IsAIControlled() => _aiControlled;

    /// <summary>
    /// Build a full InputState for one frame, including camera-relative direction math,
    /// 8-direction snap, aim data, NPC path, and FSM movement gate.
    ///
    /// Parameters owned by PlayerController (passed in, not cleared here):
    ///   bodyYaw = GlobalRotation.Y (for FacingYaw)
    ///   pendingSlotPress = _pendingSlotPress (consumed by caller after return)
    ///   abilityAimYaw / abilityAimDistance = set by ability Tick
    ///
    /// Returns (InputState, world-space moveDirection, camera-relative snappedInputDirection).
    /// </summary>
    public (InputState input, Vector3 moveDirection, Vector2 snappedInputDirection) BuildInputState(
        CameraMount? camera,
        float bodyYaw,
        bool isNPC,
        bool isAiming,
        byte pendingSlotPress,
        float? abilityAimYaw,
        ushort? abilityAimDistance,
        StateMachine? fsm)
    {
        var input = new InputState();

        // ── NPC path: use injected AI input directly ──
        if (isNPC && _aiControlled)
        {
            var (moveX, moveY) = GetMovement();
            input.MoveX = moveX;
            input.MoveY = moveY;
            input.Up = moveY < -0.3f;
            input.Down = moveY > 0.3f;
            input.Left = moveX < -0.3f;
            input.Right = moveX > 0.3f;
            input.Jump = JumpJustPressed;
            input.Dash = DashJustPressed;
            input.Crouch = false;
            input.ActiveSlot = pendingSlotPress;

            Vector3 moveDir = new Vector3(moveX, 0f, moveY).Normalized();
            Vector2 snappedDir = new Vector2(moveX, moveY);
            return (input, moveDir, snappedDir);
        }

        // ── Player path: camera-relative 8-direction input ──
        Vector3 camForward = Vector3.Forward;
        Vector3 camRight = Vector3.Right;
        if (camera != null)
        {
            camForward = camera.GetForwardDirection();
            camRight = camera.GetRightDirection();
        }

        // Build raw camera-relative direction
        Vector3 rawDir = Vector3.Zero;
        if (Input.IsActionPressed("move_forward")) rawDir += camForward;
        if (Input.IsActionPressed("move_back")) rawDir -= camForward;
        if (Input.IsActionPressed("move_left")) rawDir -= camRight;
        if (Input.IsActionPressed("move_right")) rawDir += camRight;

        Vector3 moveDirection = Vector3.Zero;
        Vector2 snappedInputDirection = Vector2.Zero;

        if (rawDir.LengthSquared() > 0.001f)
        {
            // Convert to camera-relative 2D coordinates
            float rawForward = rawDir.Dot(camForward);
            float rawRight = rawDir.Dot(camRight);

            // Snap to 8 directions
            float angle = MathF.Atan2(rawRight, rawForward);
            const float snapStep = MathF.PI / 4f; // 45°
            float snappedAngle = MathF.Round(angle / snapStep) * snapStep;

            float fwd = MathF.Cos(snappedAngle);
            float rgt = MathF.Sin(snappedAngle);

            snappedInputDirection = new Vector2(rgt, fwd);
            moveDirection = (camForward * fwd) + (camRight * rgt);
            moveDirection = moveDirection.Normalized();
        }

        input.MoveX = moveDirection.X;
        input.MoveY = moveDirection.Z;
        input.Up = moveDirection.Z < -0.3f;
        input.Down = moveDirection.Z > 0.3f;
        input.Left = moveDirection.X < -0.3f;
        input.Right = moveDirection.X > 0.3f;
        input.Jump = Input.IsActionJustPressed("jump");
        input.Dash = Input.IsActionJustPressed("dash");
        input.Crouch = Input.IsActionPressed("crouch");
        input.ActiveSlot = pendingSlotPress;
        input.IsAiming = isAiming;

        // Facing yaw from body rotation
        float deg = Mathf.RadToDeg(bodyYaw);
        input.FacingYaw = (short)Math.Clamp(deg * 100f, -32768, 32767);

        // Aim yaw from camera (combat facing), overridden by active ability
        float aimDeg = camera != null ? Mathf.RadToDeg(camera.GetCameraYaw()) : deg;
        input.AimYaw = (short)Math.Clamp(aimDeg * 100f, -32768, 32767);
        if (input.ActiveSlot > 0 || input.Jump || input.Dash)
            GD.Print($"[Input] FacingYaw={input.FacingYaw}deg AimYaw={input.AimYaw}deg bodyYaw={deg:F2} camYaw={aimDeg:F2}");
        input.AimDistance = 0;

        // Active ability overrides aim data
        if (abilityAimYaw.HasValue)
        {
            float throwDeg = Mathf.RadToDeg(abilityAimYaw.Value);
            input.AimYaw = (short)Math.Clamp(throwDeg * 100f, -32768, 32767);
        }
        if (abilityAimDistance.HasValue)
            input.AimDistance = abilityAimDistance.Value;

        // FSM movement gate: zero out input if state disallows movement
        if (fsm != null && !fsm.CanMove())
        {
            input.MoveX = 0f;
            input.MoveY = 0f;
            input.Jump = false;
            input.Dash = false;
            moveDirection = Vector3.Zero;
            snappedInputDirection = Vector2.Zero;
        }

        return (input, moveDirection, snappedInputDirection);
    }
}
