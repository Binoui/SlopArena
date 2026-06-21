#nullable enable
using Godot;

/// <summary>
/// Warp state — C# FSM state for the warp animation.
/// Actual movement is handled by the simulation (Simulation.ProcessWarp).
/// This state just manages the animation (run → attack blend on approach).
/// </summary>
public sealed partial class WarpState : State
{
    public WarpState()
    {
        AnimationName = "Run";
    }

    public override void Enter()
    {
        base.Enter();
        Player.SetModelEmission(new Color(0.3f, 0.9f, 1.0f));
    }

    public override void Exit()
    {
        Player.ClearModelEmission();
        base.Exit();
    }
}
