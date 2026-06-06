#nullable enable
using Godot;

/// <summary>
/// Jump state — airborne, going upward (velocity.Y > 0).
/// Transitions to Fall when the character starts descending (velocity.Y < 0).
/// </summary>
public sealed partial class JumpState : State
{
    public JumpState()
    {
        AnimationName = "Jump";
    }

    public override void OnProcess(float delta)
    {
        // Transition to Fall at the apex
        if (Player.Velocity.Y < 0f && !Player.IsOnFloor())
        {
            StateMachine.TransitionTo("fall");
            return;
        }

        // Instant landing (grounded with downwards velocity cancelled by simulation)
        if (Player.IsOnFloor() && Player.Velocity.Y <= 0f)
        {
            StateMachine.TransitionTo("landing");
            return;
        }
    }
}
