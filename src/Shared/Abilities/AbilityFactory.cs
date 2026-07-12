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
            CharacterClass.FightGuy => CreateFightGuyAbility(slot, airborne),
            _ => null,
        };
    }

    private static ServerAbility? CreateMankiAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),     // LMB ground
        (0, true) => new AirLmbCombo(),            // AirLMB — multi-hit air combo (stages from spec)
        (1, false) => null,                     // RMB — data-driven ChargeAttack
        (2, _) => new MankiRoundBomb(),        // Q (same ground/air)
        (3, _) => new MankiGrapple(),          // E — Grapple Gun
        (4, _) => new MankiBazooka(),          // R — Bazooka
        (5, _) => new MankiOverclock(),        // F — Overclock
        _ => null, // No ServerAbility = data-driven fallback
    };

    private static ServerAbility? CreateFightGuyAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),         // LMB — 3-hit combo
        (0, true) => new AirLmbCombo(),             // AirLMB — multi-hit air combo
        (1, _) => null,                             // RMB/AirRMB — data-driven
        (2, _) => new FightGuyKiShot(),        // Q — aimed projectile
        (3, _) => new FightGuyCycloneKick(),           // E — forward engage + stun
        (4, _) => new FightGuyDragonKick(),            // R — conditional finisher
        (5, _) => new FightGuyTempest(),              // F — ultimate
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
