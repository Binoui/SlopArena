namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Air LMB combo ability used by all characters.
    /// Stages come from the character's airborne slot 0 AbilitySpec definition.
    /// </summary>
    public class AirLmbCombo : StageChainAbility
    {
        protected override AttackStage[] GetStages(CharacterDefinition def)
        {
            var spec = def.GetSlotAbility(Slot, airborne: true);
            return spec?.Stages ?? System.Array.Empty<AttackStage>();
        }
    }
}
