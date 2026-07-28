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
        (1, false) => new MankiAerosolFlame(), // RMB ground
        (1, true) => new AirRmbAttack(),       // RMB air
        (2, _) => new MankiRoundBomb(),        // Q
        (3, _) => new MankiGrapple(),          // E
        (4, _) => new MankiBazooka(),          // R
        (5, _) => new MankiOverclock(),        // F
        _ => null,
    };

    private static ServerAbility? CreateFightGuyAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),          // LMB ground
        (0, true) => new AirLmbCombo(),        // AirLMB
        (1, false) => new FightGuyUppercut(),  // RMB ground
        (1, true) => new AirRmbAttack(),       // RMB air
        (2, _) => new FightGuyKiShot(),        // Q
        (3, _) => new FightGuyCycloneKick(),   // E
        (4, _) => new FightGuyDragonKick(),    // R
        (5, _) => new FightGuyTempest(),       // F
        _ => null,
    };

    private static ServerAbility? CreateKistuAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),          // LMB ground — light slash combo
        (0, true) => new AirLmbCombo(),        // AirLMB — air slash combo
        (1, false) => new LungeChargeAttack(), // RMB — charged spin (kill move)
        (1, true) => new AirRmbAttack(),       // AirRMB — falling slash spike
        (2, _) => new KistuCounter(),          // Q — counter/parry
        (3, _) => new LungeChargeAttack(),     // E — charged dash slash
        (4, _) => new KistuRisingSlash(),      // R — rising slash (signature)
        (5, _) => new KistuUltFlurry(),        // F — blade flurry ult
        _ => null,
    };

    private static ServerAbility? CreateNilusAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),          // LMB — rift claws
        (0, true) => new AirLmbCombo(),        // AirLMB — void rake
        (1, false) => new LungeChargeAttack(), // RMB — entropy lance (tap/charged)
        (1, true) => new AirRmbAttack(),       // AirRMB — collapse
        (2, _) => new NilusVoidRift(),         // Q — void rift
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
