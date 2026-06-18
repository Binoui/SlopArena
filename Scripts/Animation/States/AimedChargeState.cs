#nullable enable
using Godot;

/// <summary>
/// FSM state for hold-to-charge abilities.
///
/// Responsibilities (thin):
///   - Blocks movement via CanMove = false
///   - Plays the charge animation loop
///
/// What the ability class handles:
///   - Configures this state via Configure() before ActivateAbility
///   - OnActivate → transitions FSM to this state
///   - Tick → detects release, fires ActiveSlot
///   - OnDeactivate → FSM transitions back
///
/// This keeps ability logic in Ability classes and animation/movement
/// constraints in the FSM — clean separation.
/// </summary>
public sealed partial class AimedChargeState : State
{
    public AimedChargeState()
    {
        AnimationName = "";
        CanMove = false;
    }

    /// <summary>
    /// Configure with the charge animation name from the ability spec.
    /// Must be called before this state is entered.
    /// </summary>
    public void Configure(string chargeAnimName)
    {
        AnimationName = chargeAnimName;
    }

    public override void Enter()
    {
        base.Enter(); // plays charge loop anim via AnimPlayback.Travel
    }

    public override void Exit()
    {
        base.Exit();
    }
}
