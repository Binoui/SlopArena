#nullable enable
using Godot;

/// <summary>
/// Landing state — brief recovery after hitting the ground.
/// Transitions to Idle or Run after a short timer.
/// </summary>
public sealed partial class LandingState : State
{
    public LandingState()
    {
        AnimationName = "Land";
    }

    [Export]
    private float _landingDuration = 0.2f;

    private float _timer;

    public override void Enter()
    {
        _timer = _landingDuration;
        Movement.ResetJumps();
        base.Enter();
    }

    public override void OnProcess(float delta)
    {
        _timer -= delta;

        if (_timer <= 0f)
        {
            // Decide: run or idle based on input
            if (Player.MoveDirection.LengthSquared() > 0.001f)
                StateMachine.TransitionTo("run");
            else
                StateMachine.TransitionTo("idle");
            return;
        }
    }
}
