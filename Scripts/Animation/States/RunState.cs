#nullable enable
using Godot;

/// <summary>
/// Grounded running state — player has horizontal velocity.
/// Transitions to: Idle (when stopping), Jump (on jump press), Fall (off edge).
/// </summary>
public sealed partial class RunState : State
{
    public RunState()
    {
        AnimationName = "Run";
    }

    public override void Enter()
    {
        Movement.ResetJumps();
        base.Enter();
    }

    public override void OnProcess(float delta)
    {
        // Jump
        if (Input.IsActionJustPressed("jump") && Player.IsOnFloor())
        {
            StateMachine.TransitionTo("jump");
            return;
        }

        // Fall off edge
        if (!Player.IsOnFloor() && Player.Velocity.Y < 0f)
        {
            StateMachine.TransitionTo("fall");
            return;
        }

        // Idle when stopping
        var hVel = new Vector3(Player.Velocity.X, 0f, Player.Velocity.Z);
        if (hVel.Length() < 0.5f)
        {
            StateMachine.TransitionTo("idle");
            return;
        }
    }
}
