#nullable enable
using Godot;

/// <summary>
/// Jump state — rising phase of a jump (single or double jump, duration-based).
/// Uses the dedicated "jump" AnimationTree node.
/// Auto-transitions to FallState after JumpDurationTicks.
///
/// Duration is per-character from MovementStats.JumpDurationTicks.
/// Jump input is handled centrally in PlayerController._Process.
/// </summary>
public sealed partial class JumpState : State
{
    private ushort _ticksRemaining;

    public JumpState()
    {
        AnimationName = "jump";
    }

    public override void Enter()
    {
        _ticksRemaining = Player.CharDef.Movement.JumpDurationTicks;
        base.Enter();
        Player.SetModelEmission(new Color(0.5f, 0.8f, 1.0f)); // Light blue
    }

    public override void Exit()
    {
        Player.ClearModelEmission();
        base.Exit();
    }

    public override void OnPhysicsProcess(float delta)
    {
        // Decrement tick counter; transition to fall when rising phase ends
        if (_ticksRemaining > 0)
        {
            _ticksRemaining--;
            if (_ticksRemaining == 0)
            {
                StateMachine.TransitionTo("fall");
            }
        }
    }
}
