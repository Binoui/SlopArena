#nullable enable
using Godot;
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
    // ── Frame state ──
    public bool JumpJustPressed { get; set; }
    public bool DashJustPressed { get; set; }

    // ── AI injection ──
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
    /// Check if attack button is pressed this frame.
    /// </summary>
    public bool IsAttackPressed()
    {
        if (_aiControlled)
        {
            return _aiInput.Attack;
        }
        else
        {
            return Input.IsActionPressed("attack");
        }
    }

    /// <summary>
    /// Check if AI mode is active.
    /// </summary>
    public bool IsAIControlled() => _aiControlled;
}
