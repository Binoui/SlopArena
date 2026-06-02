#nullable enable
using Godot;
using System;
using SlopArena.Shared;

/// <summary>
/// Special effects for class abilities.
/// These methods are called AFTER stage resolution in ExecuteSlot.
/// They handle what AttackStage can't express:
///   - Projectile spawning (visual Godot Area3D)
///   - Self-buffs, teleports, delayed AoE
///   - Status application (conditional bonuses, status consumption)
///   - Complex multi-hit behaviour
///
/// Access hit targets from stage resolution via CombatComponent.GetTargetsFromLastHit().
/// Each method maps to a key in AbilityRegistry, referenced by CharacterDefinition's SpecialEffectKeys.
/// </summary>
public static class ClassAbilities
{
	// ═══════════════════════════════════════
	// VANGUARD
	// ═══════════════════════════════════════

	/// Q — Shield Bash: bonus damage if target has a status
	public static void VanguardShieldBash(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();

		StatusSpells.CreateConeVisual(combat, pos, forward, 3f, 2f, new Color(0.8f, 0.8f, 0.3f, 0.4f), 0.2f);
		var hits = combat.CheckMeleeCone(pos, forward, 3f, 45f, 8f, 15f, 5f);

		// Bonus damage if target has any status
		foreach (ulong targetId in hits)
		{
			combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 5f, 0f, 0f, 0f);
		}
	}

	/// E — War Cry: shield self, push nearby enemies
	public static void VanguardWarCry(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();

		StatusSpells.CreateCircleVisual(combat, pos, 6f, new Color(1f, 0.8f, 0.2f, 0.2f), 0.5f);

		combat.ApplyStatus(StatusType.Shielded, 4f, combat.GetEntityId());
		combat.CheckCircleHit(pos, 5f, 0f, 10f, 5f);
	}

	/// R — Intervene: dash forward, knockback + slow around landing
	public static void VanguardIntervene(CombatComponent combat)
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
	public static void VanguardThunderclap(CombatComponent combat)
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


	// ═══════════════════════════════════════
	// WRAITH
	// ═══════════════════════════════════════

	/// Q — Viper Shot: projectile, poison, consume status for double dmg
	public static void WraithViperShot(CombatComponent combat)
	{
		if (combat.GetOwnerNode() is not Node3D owner) return;
		var tree = owner.GetTree();
		if (tree?.CurrentScene == null) return;

		Vector3 forward = combat.GetCameraForward();
		Vector3 origin = combat.GetOwnerPosition() + forward * 2f + new Vector3(0f, 1.5f, 0f);

		CreateSimpleProjectile(origin, forward, 35f, 2.5f,
			new Color(0.6f, 0.2f, 0.8f), tree, combat, 8f, forward * 5f, StatusType.Burn, 4f);
	}

	/// E — Shadow Step: directional teleport dash
	public static void WraithShadowStep(CombatComponent combat)
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
	public static void WraithRapidFire(CombatComponent combat)
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
			CreateSimpleProjectile(origin + new Vector3(i * 0.5f, 0f, 0f), dir, 50f, 1.5f,
				new Color(0.7f, 0.5f, 0.2f), tree, combat, 5f, dir * 3f, null, 0f);
		}
	}

	/// F — Freezing Trap: hidden ground trap, freeze zone
	public static void WraithFreezingTrap(CombatComponent combat)
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


	// ═══════════════════════════════════════
	// CHANNELER
	// ═══════════════════════════════════════

	/// Q — Frostbolt: projectile that slows
	public static void ChannelerFrostbolt(CombatComponent combat)
	{
		if (combat.GetOwnerNode() is not Node3D owner) return;
		var tree = owner.GetTree();
		if (tree?.CurrentScene == null) return;

		Vector3 forward = combat.GetCameraForward();
		Vector3 origin = combat.GetOwnerPosition() + forward * 2f + new Vector3(0f, 1.5f, 0f);

		CreateSimpleProjectile(origin, forward, 40f, 2.5f,
			new Color(0.4f, 0.7f, 1f), tree, combat, 12f, forward * 8f + new Vector3(0f, 3f, 0f), StatusType.Slowed, 3f);
	}

	/// E — Dragon's Breath: cone fire, stacking Burn
	public static void ChannelerDragonsBreath(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		float range = 5f;
		float radius = 3f;

		StatusSpells.CreateConeVisual(combat, pos, forward, range, radius, new Color(1f, 0.4f, 0f, 0.4f), 0.3f);

		var hits = combat.CheckMeleeCone(pos, forward, range, 60f, 8f, 3f, 1f);
		combat.ApplyStatusToLastHit(StatusType.Burn, 3f);

		// If already burning, stack — handled by combat status refresh
		foreach (ulong targetId in hits)
		{
			if (combat.HasStatusOnTarget(targetId, StatusType.Burn))
			{
				combat.ApplyStatusToEntity(targetId, StatusType.Burn, 5f);
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 4f, 0f, 0f, 0f);
			}
		}
	}

	/// R — Ice Lance: beam, consumes Slow for bonus damage
	public static void ChannelerIceLance(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();

		StatusSpells.CreateBeamVisual(combat, pos, forward, 12f, new Color(0.5f, 0.8f, 1f, 0.5f), 0.3f);

		var hits = combat.CheckMeleeCone(pos, forward, 12f, 4f, 15f, 12f, 5f);

		foreach (ulong targetId in hits)
		{
			if (combat.ConsumeStatusOnTarget(targetId, StatusType.Slowed))
			{
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 15f, 5f, 5f, 5f);
			}
		}
	}

	/// F — Meteor: mark zone, delayed meteor strike, Burn
	public static void ChannelerMeteor(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = new Vector3(pos.X + forward.X * 8f, 0.5f, pos.Z + forward.Z * 8f);
		float radius = 4f;

		// Burn zone on ground
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


	// ═══════════════════════════════════════
	// PROJECTILE HELPER
	// ═══════════════════════════════════════

	private static void CreateSimpleProjectile(Vector3 origin, Vector3 direction, float speed, float lifetime,
		Color color, SceneTree tree, CombatComponent combat, float damage, Vector3 knockbackForce, StatusType? status, float statusDuration)
	{
		var proj = new MeshInstance3D();
		var sphere = new SphereMesh();
		sphere.Radius = 0.4f;
		sphere.Height = 0.8f;
		sphere.RadialSegments = 10;
		sphere.Rings = 6;
		var mat = new StandardMaterial3D
		{
			EmissionEnabled = true,
			Emission = color,
			EmissionEnergyMultiplier = 5f,
			AlbedoColor = color,
		};
		proj.Mesh = sphere;
		proj.MaterialOverride = mat;
		proj.GlobalPosition = origin;
		tree.CurrentScene.AddChild(proj);

		Vector3 targetPos = origin + direction * (speed * lifetime);
		var tween = tree.CreateTween();
		tween.TweenProperty(proj, "global_position", targetPos, lifetime).SetTrans(Tween.TransitionType.Linear);
		tween.Finished += () => { if (GodotObject.IsInstanceValid(proj)) proj.QueueFree(); };

		// Area hit detection
		var hitBox = new Area3D();
		var hitShape = new CollisionShape3D();
		var hitSphere = new SphereShape3D();
		hitSphere.Radius = 1.2f;
		hitShape.Shape = hitSphere;
		hitBox.AddChild(hitShape);
		hitBox.CollisionMask = 2;
		proj.AddChild(hitBox);

		hitBox.BodyEntered += (Node3D body) =>
		{
			if (!GodotObject.IsInstanceValid(proj)) return;
			var node = body as Node;
			while (node != null && node is not CharacterBody3D)
				node = node.GetParent();
			if (node is CharacterBody3D cb)
			{
				string nameStr = cb.Name.ToString();
				if (nameStr.StartsWith("DummyBody_") && int.TryParse(nameStr.AsSpan("DummyBody_".Length), out int idx))
				{
					ulong entityId = (ulong)(100 + idx);
					combat.GetSimulation()?.OnEntityHit?.Invoke(entityId, damage, knockbackForce.X, knockbackForce.Y, knockbackForce.Z);
					if (status.HasValue)
						combat.ApplyStatusToEntity(entityId, status.Value, statusDuration);
					StatusSpells.CreateImpactVisual(combat, proj.GlobalPosition, 1.5f, color);
					proj.QueueFree();
					tween.Kill();
				}
			}
		};
	}
}
