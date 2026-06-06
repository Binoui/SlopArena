#nullable enable
using Godot;

/// <summary>
/// Generic attack state for all ability slots.
/// PlayerController sets NextAnimName before TransitionTo("attack")
/// or calls ChainTo() for combo chaining within the same state.
///
/// Lives inside the "Attacks" sub-StateMachine in the AnimationTree.
/// Travel paths use the "Attacks/" prefix.
/// </summary>
public sealed partial class AttackState : State
{
    public AttackState()
    {
        AnimationName = "Attacks/melee";
    }

    private const string AttackPrefix = "Attacks/";

    /// <summary>
    /// Set by PlayerController before TransitionTo("attack").
    /// Should be just the state name (e.g. "melee"), without the prefix.
    /// </summary>
    public string NextAnimName { get; set; } = "";

    public override void Enter()
    {
        if (!string.IsNullOrEmpty(NextAnimName))
            AnimationName = AttackPrefix + NextAnimName;
        base.Enter();
    }

    /// <summary>
    /// Chain to the next combo stage without leaving the state.
    /// Prepends the "Attacks/" prefix automatically.
    /// </summary>
    public void ChainTo(string animName)
    {
        NextAnimName = animName;
        AnimationName = AttackPrefix + animName;
        if (AnimPlayback != null)
            AnimPlayback.Travel(AnimationName);
    }

    public override void OnProcess(float delta)
    {
        // Wait for animation lock to expire
        if (Movement.State.AnimLockTicks > 0)
            return;

        // Combo window open — stay, PlayerController may call ChainTo()
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
            StateMachine.TransitionTo("air");
        }
    }
}
