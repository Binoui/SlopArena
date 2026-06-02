#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Special effects for Wraith abilities.
/// Called AFTER stage resolution — access hit targets via CombatComponent.GetTargetsFromLastHit().
/// Each method maps to a key in AbilityRegistry.
/// </summary>
public static class WraithAbilities
{
	/// Q — Viper Shot: projectile, applies Burn
	public static void ViperShot(CombatComponent combat)
	{
		if (combat.GetOwnerNode() is not Node3D owner) return;
		var tree = owner.GetTree();
		if (tree?.CurrentScene == null) return;

		Vector3 forward = combat.GetCameraForward();
		Vector3 origin = combat.GetOwnerPosition() + forward * 2f + new Vector3(0f, 1.5f, 0f);

		ProjectileHelpers.CreateProjectile(origin, forward, 35f, 2.5f,
			new Color(0.6f, 0.2f, 0.8f), tree, combat, 8f, forward * 5f, StatusType.Burn, 4f);
	}

	/// E — Shadow Step: directional teleport dash
	public static void ShadowStep(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = pos + forward * 10f;

		if (combat.GetOwnerNode() is CharacterBody3D body)
		{
			body.GlobalPosition = target;
			body.Velocity = forward * 15f;
		}

		StatusSpells.CreateImpactVisual(combat, pos, 2f, new Color(0.5f, 0.1f, 0.8f));
		StatusSpells.CreateImpactVisual(combat, target, 2f, new Color(0.5f, 0.1f, 0.8f));
	}

	/// R — Rapid Fire: 3 quick projectiles in cone
	public static void RapidFire(CombatComponent combat)
	{
		if (combat.GetOwnerNode() is not Node3D owner) return;
		var tree = owner.GetTree();
		if (tree?.CurrentScene == null) return;

		Vector3 forward = combat.GetCameraForward();
		Vector3 origin = combat.GetOwnerPosition() + forward * 2f + new Vector3(0f, 1.5f, 0f);

		for (int i = -1; i <= 1; i++)
		{
			float angle = i * 0.2f;
			Vector3 dir = new Vector3(
				forward.X * Mathf.Cos(angle) - forward.Z * Mathf.Sin(angle),
				0f,
				forward.X * Mathf.Sin(angle) + forward.Z * Mathf.Cos(angle)
			).Normalized();
			ProjectileHelpers.CreateProjectile(origin + new Vector3(i * 0.5f, 0f, 0f), dir, 50f, 1.5f,
				new Color(0.7f, 0.5f, 0.2f), tree, combat, 5f, dir * 3f, null, 0f);
		}
	}

	/// F — Freezing Trap: ground trap, freeze zone
	public static void FreezingTrap(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 trapPos = new Vector3(pos.X + forward.X * 3f, 0.3f, pos.Z + forward.Z * 3f);
		float radius = 3f;
		float delay = 0.5f;

		var indicator = StatusSpells.CreateAoEIndicator(trapPos, radius, new Color(0.3f, 0.7f, 1f, 0.2f));
		if (combat.GetOwnerNode() is Node3D owner)
			owner.GetTree().CurrentScene?.AddChild(indicator);

		var timer = combat.GetTree().CreateTimer(delay);
		timer.Timeout += () =>
		{
			if (!GodotObject.IsInstanceValid(indicator)) return;
			StatusSpells.CreateImpactVisual(combat, trapPos, radius, new Color(0.5f, 0.8f, 1f));
			combat.CheckCircleHit(trapPos, radius, 10f, 5f, 2f);
			combat.ApplyStatusToLastHit(StatusType.Slowed, 4f);
			indicator.QueueFree();
		};
	}
}
