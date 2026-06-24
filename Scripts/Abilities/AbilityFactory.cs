using SlopArena.Shared;

/// <summary>
/// Creates the appropriate Ability class from the ability's AbilityTypeId.
/// Type 2 (Round Bomb) → RoundBomb, Type 3 (Aerosol Flame) → AerosolFlame, else → SimpleAttack.
///
/// No per-character branches — the AbilityTypeId declares what kind of ability it is.
/// </summary>
public static class AbilityFactory
{
    public static Ability Create(int slotIndex, bool airborne, CharacterDefinition def)
    {
        var spec = def.GetSlotAbility(slotIndex, airborne);
        if (spec == null)
            return new SimpleAttack { Data = spec, SlotNumber = (byte)(slotIndex + 1) };

        return spec.AbilityTypeId switch
        {
            3 => new AerosolFlame { Data = spec },
            2 => new RoundBomb { Data = spec },
            _ => new SimpleAttack { Data = spec, SlotNumber = (byte)(slotIndex + 1) },
        };
    }
}
