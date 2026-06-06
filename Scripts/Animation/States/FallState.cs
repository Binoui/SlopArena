#nullable enable
using Godot;

/// <summary>
/// Fall state — airborne, descending (velocity.Y < 0).
/// Transitions to Landing when the player touches the ground.
/// </summary>
public sealed partial class FallState : State
{
    public FallState()
    {
        AnimationName = "Fall";
    }

    public override void OnProcess(float delta)
    {
        // Landing detection
        if (Player.IsOnFloor() && Player.Velocity.Y <= 0f)
        {
            StateMachine.TransitionTo("landing");
            return;
        }

        // If somehow going back up (knockback upward), re-transition to Jump
        if (Player.Velocity.Y > 0.5f && !Player.IsOnFloor())
        {
            StateMachine.TransitionTo("jump");
            return;
        }
    }
}
