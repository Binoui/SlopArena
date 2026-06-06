#nullable enable
using Godot;

/// <summary>
/// Grounded idle state — player stands still.
/// Transitions to: air (on jump or off edge), run (when moving).
/// </summary>
public sealed partial class IdleState : State
{
    public IdleState()
    {
        AnimationName = "Movement/Idle";
    }

    public override void Enter()
    {
        Movement.State.ComboStage = 0;
        Movement.ResetJumps();
        base.Enter();
    }

    public override void OnProcess(float delta)
    {
        // Jump
        if (Input.IsActionJustPressed("jump") && Player.IsOnFloor())
        {
            StateMachine.TransitionTo("air");
            return;
        }

        // Walked off edge — go to air (blend will handle jump→fall naturally)
        if (!Player.IsOnFloor() && Player.Velocity.Y < 0f)
        {
            StateMachine.TransitionTo("air");
            return;
        }

        // Run when moving
        var hVel = new Vector3(Player.Velocity.X, 0f, Player.Velocity.Z);
        if (hVel.Length() > 0.5f && Player.IsOnFloor())
        {
            StateMachine.TransitionTo("run");
            return;
        }
    }
}
