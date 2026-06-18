using SlopArena.Shared;

/// <summary>
/// Creates the appropriate Ability class from the ability's spec type.
/// AerosolFlameSpec → AerosolFlame, RoundBombSpec → RoundBomb, else → SimpleAttack.
///
/// No per-character branches — the spec type already declares what kind of ability it is.
/// </summary>
public static class AbilityFactory
{
    public static Ability Create(int slotIndex, bool airborne, CharacterDefinition def)
    {
        var spec = def.GetSlotAbility(slotIndex, airborne);

        return spec switch
        {
            AerosolFlameSpec => new AerosolFlame { Data = spec },
            RoundBombSpec   => new RoundBomb { Data = spec },
            _               => new SimpleAttack { Data = spec, SlotNumber = (byte)(slotIndex + 1) },
        };
    }
}
