namespace SlopArena.Shared.Abilities;

/// <summary>
/// Marker for cooked capabilities that own a hold-to-aim, release-to-fire phase
/// (AimedProjectile abilities and the reusable targeted-leap lifecycle).
/// <see cref="CookedTimelineAbility"/> freezes stage time while a capability owns
/// <see cref="ActionState.Aiming"/>, so authored timeouts cannot terminate the hold;
/// the timeline resumes when the capability transitions into its action phase.
/// </summary>
public interface IAimHoldCapability
{
}
