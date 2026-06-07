#nullable enable
using Godot;

/// <summary>
/// Centralized input state for the current frame.
/// Polled once per frame in PlayerController._Process.
/// States read from this instead of calling Godot Input directly.
/// </summary>
public sealed class InputController
{
    // ── Frame state ──
    public bool JumpJustPressed { get; set; }
    public bool DashJustPressed { get; set; }

    /// <summary>
    /// Read current Godot input and store into frame state.
    /// Call once per frame (in _Process) before StateMachine._Process.
    /// </summary>
    public void Poll()
    {
        JumpJustPressed = Input.IsActionJustPressed("jump");
        DashJustPressed = Input.IsActionJustPressed("dash");
    }
}
