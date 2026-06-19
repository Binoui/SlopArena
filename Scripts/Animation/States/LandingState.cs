#nullable enable
using Godot;

/// <summary>
/// Landing state — brief recovery after hitting the ground (~10 ticks / 0.167s).
/// Cancelable into jump or dash on input. Attacks are handled by _UnhandledInput
/// before _Process, so they cancel naturally.
/// Auto-transitions to Idle or Run when timer expires.
/// </summary>
public sealed partial class LandingState : State
{
    public LandingState()
    {
        AnimationName = ""; // No animation — crossfade in FSM handles the transition
    }

    /// <summary>
    /// ~10 ticks at 60fps (0.167s)
    /// </summary>
    [Export]
    private ushort _landingTicks = 10;

    private ushort _ticksRemaining;

    public override void Enter()
    {
        _ticksRemaining = _landingTicks;
        Movement.ResetJumps();
        Player.SetModelEmission(new Color(0.9f, 0.9f, 0.2f)); // Yellow
        base.Enter();
    }

    public override void OnProcess(float delta)
    {
        // Cancel landing on dash (jump is handled centrally in PlayerController)
        if (InputCtrl.DashJustPressed)
        {
            StateMachine.TransitionTo("idle");
            return;
        }
    }

    public override void OnPhysicsProcess(float delta)
    {
        if (--_ticksRemaining == 0)
        {
            if (Player.MoveDirection.LengthSquared() > 0.001f)
                StateMachine.TransitionTo("run");
            else
                StateMachine.TransitionTo("idle");
        }
    }
}
