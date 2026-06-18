/// <summary>
/// Generic instant-attack ability. Fires ActiveSlot immediately on activate.
/// Used for abilities with no special aiming (E, R, F, and fallback).
/// </summary>
public class SimpleAttack : Ability
{
    public override string Name => Data.Name;
    public override byte SlotNumber { get; set; }
    private bool _fired;

    public override void OnActivate(PlayerController player) => _fired = false;
    public override AbilityInputState? Tick(PlayerController player, float delta)
    {
        if (_fired) return null;
        _fired = true;
        TriggerEffects(player);
        return new AbilityInputState { ActiveSlot = SlotNumber };
    }
}
