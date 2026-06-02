#nullable enable
using Godot;
using SlopArena.Shared;

public static class ChannelerAbilities
{
	public static void Frostbolt(CombatComponent combat)
	{
		if (combat.GetOwnerNode() is not Node3D owner) return;
		var tree = owner.GetTree();
		if (tree?.CurrentScene == null) return;

		Vector3 forward = combat.GetCameraForward();
		Vector3 origin = combat.GetOwnerPosition() + forward * 2f + new Vector3(0f, 1.5f, 0f);

		ProjectileHelpers.CreateProjectile(origin, forward, 40f, 2.5f,
			new Color(0.4f, 0.7f, 1f), tree, combat, 12f, forward * 8f + new Vector3(0f, 3f, 0f), StatusType.Slowed, 3f);
	}

	public static void DragonsBreath(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		float range = 5f, radius = 3f;

		StatusSpells.CreateConeVisual(combat, pos, forward, range, radius, new Color(1f, 0.4f, 0f, 0.4f), 0.3f);

		var hits = combat.CheckMeleeCone(pos, forward, range, 60f, 8f, 3f, 1f);
		combat.ApplyStatusToLastHit(StatusType.Burn, 3f);

		foreach (ulong targetId in hits)
		{
			if (combat.HasStatusOnTarget(targetId, StatusType.Burn))
			{
				combat.ApplyStatusToEntity(targetId, StatusType.Burn, 5f);
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 4f, 0f, 0f, 0f);
			}
		}
	}

	public static void IceLance(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();

		StatusSpells.CreateBeamVisual(combat, pos, forward, 12f, new Color(0.5f, 0.8f, 1f, 0.5f), 0.3f);

		var hits = combat.CheckMeleeCone(pos, forward, 12f, 4f, 15f, 12f, 5f);

		foreach (ulong targetId in hits)
		{
			if (combat.ConsumeStatusOnTarget(targetId, StatusType.Slowed))
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 15f, 5f, 5f, 5f);
		}
	}

	public static void Meteor(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = new Vector3(pos.X + forward.X * 8f, 0.5f, pos.Z + forward.Z * 8f);
		float radius = 4f;

		StatusSpells.CreateCircleVisual(combat, target, radius, new Color(1f, 0.3f, 0f, 0.3f), 2.5f);
		combat.CheckCircleHit(target, radius, 3f, 0f, 0f);
		combat.ApplyStatusToLastHit(StatusType.Burn, 3f);

		var timer = combat.GetTree().CreateTimer(1.5f);
		timer.Timeout += () =>
		{
			StatusSpells.CreateImpactVisual(combat, target, radius, new Color(1f, 0.2f, 0f));
			combat.CheckCircleHit(target, radius, 25f, 20f, 10f);
		};
	}
}
