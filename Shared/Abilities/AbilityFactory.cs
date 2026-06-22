using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Factory for instantiating and initializing server-side abilities.
/// Uses character class + ability slot to namespace ability IDs.
/// </summary>
public static class AbilityFactory
{
    /// <summary>
    /// Create a server ability instance by character class and ability type ID.
    /// Type ID is character-specific (each character has its own ID namespace).
    /// </summary>
    public static ServerAbility CreateServer(CharacterClass characterClass, byte typeId)
    {
        return characterClass switch
        {
            CharacterClass.Manki => CreateMankiAbility(typeId),
            CharacterClass.Bunny => CreateBunnyAbility(typeId),
            _ => throw new ArgumentException($"Unknown character class: {characterClass}"),
        };
    }

    private static ServerAbility CreateMankiAbility(byte typeId) => typeId switch
    {
        1 => new MankiLmbCombo(),
        2 => new MankiRoundBomb(),
        3 => new MankiAerosolFlame(),
        _ => throw new ArgumentException($"Unknown Manki ability typeId: {typeId}"),
    };

    private static ServerAbility CreateBunnyAbility(byte typeId) => typeId switch
    {
        // TODO: Implement Bunny abilities
        _ => throw new NotImplementedException($"Bunny ability {typeId} not implemented yet"),
    };

    /// <summary>
    /// Initialize an ability's metadata from its spec definition.
    /// Slot is passed separately (0-based: 0 = LMB, 1 = RMB, etc.).
    /// </summary>
    public static void InitFromSpec(ServerAbility ability, AbilitySpec spec, byte slot)
    {
        ability.Slot = slot;
        ability.Cooldown = spec.CooldownTicks;
        ability.AnimationNames = spec.AnimationNames ?? Array.Empty<string>();
    }
}
