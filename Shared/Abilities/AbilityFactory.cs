using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Factory for instantiating and initializing server-side abilities.
/// Maps AbilityTypeId to concrete ServerAbility subclasses.
/// </summary>
public static class AbilityFactory
{
    /// <summary>
    /// Create a server ability instance by type ID.
    /// 1 = MankiLmbCombo.
    /// </summary>
    public static ServerAbility CreateServer(byte typeId)
    {
        return typeId switch
        {
            1 => new MankiLmbCombo(),
            _ => throw new ArgumentException($"Unknown AbilityTypeId: {typeId}"),
        };
    }

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
