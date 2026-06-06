#nullable enable
using Godot;

/// <summary>
/// Generic attack state for all ability slots.
/// PlayerController sets NextAnimName before TransitionTo("attack")
/// or calls ChainTo() for combo chaining within the same state.
///
/// Lifecycle:
///   Enter() → plays attack animation
///   OnProcess() → waits for AnimLockTicks to expire
///   → combo chain window open → PlayerController calls ChainTo() for next stage
///   → no combo → transition back to idle/run/fall
/// </summary>
public sealed partial class AttackState : State
{
    public AttackState()
    {
        AnimationName = "melee";
    }

    /// <summary>
    /// Set by PlayerController before TransitionTo("attack").
    /// Also used by ChainTo() for combo chaining.
    /// </summary>
    public string NextAnimName { get; set; } = "";

    public override void Enter()
    {
        if (!string.IsNullOrEmpty(NextAnimName))
            AnimationName = NextAnimName;
        base.Enter();
    }

    /// <summary>
    /// Chain to the next combo stage without leaving the state.
    /// Call this from PlayerController when a combo chain is detected.
    /// </summary>
    public void ChainTo(string animName)
    {
        NextAnimName = animName;
        AnimationName = animName;
        if (AnimPlayback != null)
            AnimPlayback.Travel(animName);
    }

    public override void OnProcess(float delta)
    {
        // Wait for animation lock to expire
        if (Movement.State.AnimLockTicks > 0)
            return;

        // If a combo window is still open, stay — PlayerController will
        // call ChainTo() if the player presses the button again.
        if (Movement.State.ComboTimerTicks > 0)
            return;

        // Attack finished — return to movement
        if (Player.IsOnFloor())
        {
            StateMachine.TransitionTo(
                Player.MoveDirection.LengthSquared() > 0.001f ? "run" : "idle");
        }
        else
        {
            StateMachine.TransitionTo("fall");
        }
    }
}
