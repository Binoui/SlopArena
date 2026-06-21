namespace SlopArena.Shared.Abilities;

/// <summary>
/// Fallback melee ability (AbilityTypeId = 0).
/// Generic forward strike that hits once and ends.
/// </summary>
public sealed class GenericMelee : ServerAbility
{
    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        // Default melee: just play the first animation frame
        AnimIndex = 0;
        s.State = ActionState.Attacking;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        // Tick advances; EndAbility is called by the ability system when AnimLockTicks expires
    }
}
