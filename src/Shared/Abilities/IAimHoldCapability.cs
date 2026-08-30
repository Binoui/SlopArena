namespace SlopArena.Shared.Abilities;

/// <summary>
/// Marker for cooked capabilities that own a hold-to-aim, release-to-fire phase
/// (AimedProjectile family: Manki Q Round Bomb, FightGuy Q Ki Shot, Bonk E
/// Targeted Jump Slam). <see cref="CookedTimelineAbility"/> freezes the stage
/// clock while such a capability holds <see cref="ActionState.Aiming"/>, so an
/// authored action-stage timeout can never terminate the aim hold; the timeline
/// resumes when the capability transitions into its action phase.
/// </summary>
public interface IAimHoldCapability
{
}
