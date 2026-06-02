#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Special effects for Vanguard abilities.
/// Called AFTER stage resolution — access hit targets via CombatComponent.GetTargetsFromLastHit().
/// Each method maps to a key in AbilityRegistry.
/// </summary>
public static class VanguardAbilities
{
	/// Q — Shield Bash: bonus damage if target has a status
	public static void ShieldBash(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();

		StatusSpells.CreateConeVisual(combat, pos, forward, 3f, 2f, new Color(0.8f, 0.8f, 0.3f, 0.4f), 0.2f);
		var hits = combat.CheckMeleeCone(pos, forward, 3f, 45f, 8f, 15f, 5f);

		foreach (ulong targetId in hits)
			combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 5f, 0f, 0f, 0f);
	}

	/// E — War Cry: shield self, push nearby enemies
	public static void WarCry(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		StatusSpells.CreateCircleVisual(combat, pos, 6f, new Color(1f, 0.8f, 0.2f, 0.2f), 0.5f);
		combat.ApplyStatus(StatusType.Shielded, 4f, combat.GetEntityId());
		combat.CheckCircleHit(pos, 5f, 0f, 10f, 5f);
	}

	/// R — Intervene: dash forward, knockback + slow around landing
	public static void Intervene(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = pos + forward * 8f;

		if (combat.GetOwnerNode() is CharacterBody3D body)
			body.Velocity = forward * 30f;

		StatusSpells.CreateCircleVisual(combat, target, 3f, new Color(0.5f, 0.8f, 1f, 0.3f), 1f);

		var timer = combat.GetTree().CreateTimer(0.2f);
		timer.Timeout += () =>
		{
			combat.CheckCircleHit(target, 3f, 5f, 12f, 5f);
			combat.ApplyStatusToLastHit(StatusType.Slowed, 3f);
		};
	}

	/// F — Thunderclap: leap to location, shock + disable zone
	public static void Thunderclap(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = new Vector3(pos.X + forward.X * 6f, 0.5f, pos.Z + forward.Z * 6f);

		StatusSpells.CreateCircleVisual(combat, target, 4f, new Color(1f, 0.9f, 0.5f, 0.5f), 0.8f);

		var timer = combat.GetTree().CreateTimer(0.3f);
		timer.Timeout += () =>
		{
			StatusSpells.CreateImpactVisual(combat, target, 4f, new Color(1f, 1f, 0.5f));
			combat.CheckCircleHit(target, 4f, 20f, 25f, 10f);
		};
	}
}
