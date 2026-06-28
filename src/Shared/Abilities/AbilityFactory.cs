using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Factory for instantiating server-side abilities.
/// Maps (CharacterClass, slot) to concrete ServerAbility implementations.
/// Slot: 0=LMB, 1=RMB, 2=Q, 3=E, 4=R, 5=F
/// </summary>
public static class AbilityFactory
{
    /// <summary>
    /// Create a server ability instance by character class and slot.
    /// Returns null if the slot has no ServerAbility (data-driven fallback).
    /// </summary>
    public static ServerAbility? CreateServer(CharacterClass characterClass, byte slot, bool airborne)
    {
        return characterClass switch
        {
            CharacterClass.Manki => CreateMankiAbility(slot, airborne),
            CharacterClass.Bunny => CreateBunnyAbility(slot, airborne),
            _ => null,
        };
    }

    private static ServerAbility? CreateMankiAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new MankiLmbCombo(),     // LMB ground
        (0, true) => null,                      // AirLMB — data-driven fallback (separate spec)
        (1, false) => new MankiAerosolFlame(), // RMB
        (2, _) => new MankiRoundBomb(),        // Q (same ground/air)
        (3, _) => null,                         // E — data-driven ExplosiveMineSpec
        (4, _) => new MankiDiveBomb(),         // R — Dive Bomb
        (5, _) => new MankiOverclock(),        // F — Overclock
        _ => null, // No ServerAbility = data-driven fallback
    };

    private static ServerAbility? CreateBunnyAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        // TODO: Implement Bunny abilities
        _ => null,
    };

    /// <summary>
    /// Initialize an ability's metadata from its spec definition.
    /// </summary>
    public static void InitFromSpec(ServerAbility ability, AbilitySpec spec, byte slot)
    {
        ability.Slot = slot;
        ability.Cooldown = spec.CooldownTicks;
        ability.AnimationNames = spec.AnimationNames ?? Array.Empty<string>();
    }
}
