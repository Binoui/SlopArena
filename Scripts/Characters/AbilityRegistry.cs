#nullable enable
using Godot;
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
		// Vanguard
		{ "VanguardShieldBash", VanguardAbilities.ShieldBash },
		{ "VanguardWarCry", VanguardAbilities.WarCry },
		{ "VanguardIntervene", VanguardAbilities.Intervene },
		{ "VanguardThunderclap", VanguardAbilities.Thunderclap },

		// Wraith
		{ "WraithViperShot", WraithAbilities.ViperShot },
		{ "WraithShadowStep", WraithAbilities.ShadowStep },
		{ "WraithRapidFire", WraithAbilities.RapidFire },
		{ "WraithFreezingTrap", WraithAbilities.FreezingTrap },

		// Channeler
		{ "ChannelerFrostbolt", ChannelerAbilities.Frostbolt },
		{ "ChannelerDragonsBreath", ChannelerAbilities.DragonsBreath },
		{ "ChannelerIceLance", ChannelerAbilities.IceLance },
		{ "ChannelerMeteor", ChannelerAbilities.Meteor },
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
