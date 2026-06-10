#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Registry of all class ability special effects. Maps a string key to the actual method.
/// Each key is referenced by CharacterDefinition's AbilityData.SpecialEffectKeys.
///
/// Adding a new character:
///   1. Create Scripts/Characters/{Name}/{Name}Abilities.cs
///   2. Add entries here matching the keys in your CharacterDefinition
/// </summary>
public static class AbilityRegistry
{
    private static readonly Dictionary<string, Action<CombatComponent>> Effects = new()
        {
			// Manki — Mad Bomber Monkey
			{ "MankiAerosolFlame", MankiAbilities.AerosolFlame },
            { "MankiRoundBomb", MankiAbilities.RoundBomb },
            { "MankiDynamiteJump", MankiAbilities.DynamiteJump },
            { "MankiDiveBomb", MankiAbilities.DiveBomb },
            { "MankiBigBoom", MankiAbilities.BigBoom },
        };

    /// <summary>
    /// Execute a special effect by key name.
    /// Called from PlayerController.ExecuteSlot after stage resolution.
    /// Returns false if the key was not found.
    /// </summary>
    public static bool Execute(string key, CombatComponent combat)
    {
        if (key == null || !Effects.TryGetValue(key, out var effect))
            return false;
        effect(combat);
        return true;
    }
}
