using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Factory for instantiating server-side abilities.
/// Maps (CharacterClass, slot) to concrete ServerAbility implementations.
/// Slot: 0=LMB, 1=RMB (retired — the RMB is the camera-lock toggle, ADR-0018, no attack),
/// 2=Slot1 (key "1"), 3=E, 4=R, 5=F, 6-10=Slot2-5/A
/// </summary>
public static class AbilityFactory
{
    /// <summary>
    /// Create a server ability instance by character class and slot.
    /// </summary>
    public static ServerAbility? CreateServer(CharacterClass characterClass, byte slot, bool airborne)
    {
        return characterClass switch
        {
            CharacterClass.Manki => CreateMankiAbility(slot, airborne),
            CharacterClass.FightGuy => CreateFightGuyAbility(slot, airborne),
            CharacterClass.Kistu => CreateKistuAbility(slot, airborne),
            CharacterClass.Nilus => CreateNilusAbility(slot, airborne),
            _ => null,
        };
    }

    private static ServerAbility? CreateMankiAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),          // LMB ground
        (0, true) => new AirLmbCombo(),        // AirLMB

        (2, _) => new MankiRoundBomb(),        // Slot1 (key "1")
        (3, _) => new MankiGrapple(),          // E
        (4, _) => new MankiBazooka(),          // R
        (5, _) => new MankiOverclock(),        // F
        _ => null,
    };

    private static ServerAbility? CreateFightGuyAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),          // LMB ground — jab
        (0, true) => new AirLmbCombo(),        // AirLMB — rising kick

        (2, false) => new LmbCombo(),          // key "1" — Low Kick (normal)
        (2, true) => new AirLmbCombo(),        // key "1" air — Double Punch
        (3, _) => new FightGuyRisingKick(),    // E — Rising Dragon (upward mobility / recovery)
        (4, _) => new FightGuyCycloneKick(),   // R — Cyclone Kick (moved from E, issue #117)
        (5, _) => new FightGuyDragonBeam(),      // F — Dragon Beam
        (6, false) => new LmbCombo(),          // key "2" — Straight Punch (normal)
        (6, true) => new AirLmbCombo(),        // key "2" air — Floating Kick
        (7, false) => new LmbCombo(),          // key "3" — Sweeping Kick (normal)
        (7, true) => new AirLmbCombo(),        // key "3" air — High Kick
        (8, false) => new LmbCombo(),          // key "4" — Double Kick (normal)
        (8, true) => new AirLmbCombo(),        // key "4" air — Air Smash
        (10, _) => new FightGuyKiShot(),         // A key (slot 11) — Ki Shot
        _ => null,                             // key "5" — empty (demo)
    };

    private static ServerAbility? CreateKistuAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),          // LMB ground (no spec — rejected before factory)
        (0, true) => new AirLmbCombo(),        // AirLMB (no spec — rejected before factory)

        (2, false) => new LmbCombo(),          // key "1" — Quick Slash (normal)
        (2, true) => new AirLmbCombo(),        // key "1" air — Air Slash
        (3, _) => new KistuDashSlash(),        // E — directional dash slash (aim + release)
        (4, _) => new KistuRisingSlash(),      // R — rising slash (signature)
        (5, _) => new KistuUltFlurry(),        // F — blade flurry ult
        (6, false) => new LmbCombo(),          // key "2" — Double Slash (normal)
        (6, true) => new AirLmbCombo(),        // key "2" air — Reverse Slash
        (7, false) => new LmbCombo(),          // key "3" — Up Slash (normal)
        (7, true) => new AirLmbCombo(),        // key "3" air — Air Up Slash
        (8, false) => new LmbCombo(),          // key "4" — Heavy Down Slash (normal)
        (8, true) => new AirLmbCombo(),        // key "4" air — Air Heavy Down Slash
        _ => null,
    };

    private static ServerAbility? CreateNilusAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),          // LMB — rift claws
        (0, true) => new AirLmbCombo(),        // AirLMB — void rake

        (2, _) => new NilusVoidRift(),         // Slot1 (key "1") — void rift
        (3, _) => new NilusRiftwalk(),         // E — riftwalk
        (4, _) => new NilusNetherGrasp(),      // R — nether grasp
        (5, _) => new NilusEventHorizon(),     // F — event horizon
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
