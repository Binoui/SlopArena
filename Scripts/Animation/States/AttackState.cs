#nullable enable
using Godot;

/// <summary>
/// Generic attack state for all ability slots.
/// Animation lock handled by sim via AnimLockTicks.
/// OnProcess transitions back to idle/run/air when AnimLockTicks expires.
/// </summary>
public sealed partial class AttackState : State
{
    public AttackState()
    {
        AnimationName = "melee";
        CanMove = false;
    }

    /// <summary>Set by PlayerController before TransitionTo("attack").</summary>
    public string NextAnimName { get; set; } = "";

    public override void Enter()
    {
        if (!string.IsNullOrEmpty(NextAnimName))
            AnimationName = NextAnimName;
        Player.SetModelEmission(new Color(1.0f, 0.2f, 0.2f), 2.0f); // Red
        base.Enter();
    }

    /// <summary>Chain to the next combo stage without leaving the state.</summary>
    public void ChainTo(string animName)
    {
        NextAnimName = animName;
        AnimationName = animName;
        if (AnimPlayback != null)
            AnimPlayback.Travel(animName);
    }

    public override void Exit()
    {
        Player.ClearModelEmission();
        base.Exit();
    }

    public override void OnProcess(float delta)
    {
        if (Movement.State.AnimLockTicks > 0)
            return;

        if (Movement.IsGrounded)
        {
            StateMachine.TransitionTo(
                Player.MoveDirection.LengthSquared() > 0.001f ? "run" : "idle");
        }
        else
        {
            StateMachine.TransitionTo("air");
        }
    }
}
