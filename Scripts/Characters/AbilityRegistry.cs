#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Registry of all class ability effects. Maps a string key to the actual method.
/// Add new entries here when creating new character abilities.
/// Each character's CharacterDefinition.ClassAbilityKeys references these keys.
/// </summary>
public static class AbilityRegistry
{
	private static readonly Dictionary<string, Action<CombatComponent>> Effects = new()
	{
		// Vanguard
		{ "VanguardShieldBash", ClassAbilities.VanguardShieldBash },
		{ "VanguardWarCry", ClassAbilities.VanguardWarCry },
		{ "VanguardIntervene", ClassAbilities.VanguardIntervene },
		{ "VanguardThunderclap", ClassAbilities.VanguardThunderclap },

		// Wraith
		{ "WraithViperShot", ClassAbilities.WraithViperShot },
		{ "WraithShadowStep", ClassAbilities.WraithShadowStep },
		{ "WraithRapidFire", ClassAbilities.WraithRapidFire },
		{ "WraithFreezingTrap", ClassAbilities.WraithFreezingTrap },

		// Channeler
		{ "ChannelerFrostbolt", ClassAbilities.ChannelerFrostbolt },
		{ "ChannelerDragonsBreath", ClassAbilities.ChannelerDragonsBreath },
		{ "ChannelerIceLance", ClassAbilities.ChannelerIceLance },
		{ "ChannelerMeteor", ClassAbilities.ChannelerMeteor },
	};

	/// <summary>
	/// Execute a class ability by key name.
	/// The key comes from CharacterDefinition.ClassAbilityKeys[index].
	/// </summary>
	public static bool Execute(string key, CombatComponent combat)
	{
		if (key == null || !Effects.TryGetValue(key, out var effect))
			return false;
		effect(combat);
		return true;
	}
}
