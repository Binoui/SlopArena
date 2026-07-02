namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Concrete LMB combo ability used by all characters.
    /// Stages come from the character's AbilitySpec definition.
    /// </summary>
    public class LmbCombo : StageChainAbility
    {
        protected override AttackStage[] GetStages(CharacterDefinition def)
        {
            var spec = def.GetSlotAbility(Slot, false);
            return spec?.Stages ?? Array.Empty<AttackStage>();
        }
    }
}
