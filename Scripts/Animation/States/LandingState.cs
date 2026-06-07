#nullable enable
using Godot;

/// <summary>
/// Landing state — brief recovery after hitting the ground (~10 frames / 0.167s).
/// Cancelable into jump or dash on input. Attacks are handled by _UnhandledInput
/// before _Process, so they cancel naturally.
/// Auto-transitions to Idle or Run when timer expires.
/// </summary>
public sealed partial class LandingState : State
{
    public LandingState()
    {
        AnimationName = "Land";
    }

    [Export]
    private float _landingDuration = 0.167f; // ~10 frames at 60fps

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

        // Cancel landing on jump or dash
        if (InputCtrl.JumpJustPressed)
        {
            StateMachine.TransitionTo("air");
            return;
        }
        if (InputCtrl.DashJustPressed)
        {
            StateMachine.TransitionTo("idle");
            return;
        }

        // Auto-transition when landing animation finishes
        if (_timer <= 0f)
        {
            if (Player.MoveDirection.LengthSquared() > 0.001f)
                StateMachine.TransitionTo("run");
            else
                StateMachine.TransitionTo("idle");
        }
    }
}
