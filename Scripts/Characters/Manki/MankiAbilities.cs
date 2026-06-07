#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Special effects for Manki (Fire Monkey) abilities.
/// Called AFTER stage resolution.
/// Each method maps to a key in AbilityRegistry.
/// </summary>
public static class MankiAbilities
{
	/// Q — Fire Lash: ground kick, fire arc, slows
	public static void FireLash(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		StatusSpells.CreateConeVisual(combat, pos, forward, 4f, 2.5f, new Color(1f, 0.5f, 0f, 0.3f), 0.3f);
	}

	/// E — Rising Flame: vertical uppercut, anti-air / recovery
	public static void RisingFlame(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		StatusSpells.CreateImpactVisual(combat, pos + Vector3.Up * 2f, 1.5f, new Color(1f, 0.6f, 0f));
	}

	/// R — Ember Burst: small AoE explosion around self, push
	public static void EmberBurst(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		StatusSpells.CreateCircleVisual(combat, pos, 3f, new Color(1f, 0.4f, 0f, 0.3f), 0.4f);
		StatusSpells.CreateImpactVisual(combat, pos, 2f, new Color(1f, 0.7f, 0f));
	}

	/// F (Ult) — Inferno Dance: dash + auto-combo + explosion finish
	public static void InfernoDance(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		StatusSpells.CreateCircleVisual(combat, pos, 4f, new Color(1f, 0.3f, 0f, 0.4f), 0.8f);
		StatusSpells.CreateImpactVisual(combat, pos, 3.5f, new Color(1f, 0.8f, 0.2f));
	}
}
