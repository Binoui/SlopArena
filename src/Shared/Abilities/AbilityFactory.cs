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
            CharacterClass.Nilus => CreateNilusAbility(slot, airborne),
            CharacterClass.FightGuy => null,
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
