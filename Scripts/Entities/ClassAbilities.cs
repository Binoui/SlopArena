using Godot;
using System;
using SlopArena.Shared;

/// <summary>
/// All class-specific abilities for the 3 playable classes.
/// Each ability takes a CombatComponent and performs its effect.
/// </summary>
public static class ClassAbilities
{
	// ==========================================
	// VANGUARD
	// ==========================================
	
	/// LMB — Rocket Punch: charged punch forward, massive knockback
	public static void VanguardRocketPunch(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		float range = 4f;
		
		StatusSpells.CreateConeVisual(combat, pos, forward, range, 3f, new Color(1f, 0.7f, 0.2f, 0.5f), 0.3f);
		
		var hits = combat.CheckMeleeCone(pos, forward, range, 60f, 25f, 40f, 10f);
		// If adrenaline max (5 stacks consumed), bonus stun
		foreach (ulong targetId in hits)
		{
			combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 10f, 0f, 0f, 0f);
		}
	}
	
	/// RMB — Ground Slam: jump then slam ground, AoE knockback + Vulnerable
	public static void VanguardGroundSlam(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 groundPos = new Vector3(pos.X, 0.5f, pos.Z);
		float radius = 5f;
		
		// Launch self upward slightly first
		if (combat.GetOwnerNode() is CharacterBody3D body)
		{
			body.Velocity = new Vector3(body.Velocity.X, 10f, body.Velocity.Z);
		}
		
		// Visual indicator on ground
		StatusSpells.CreateCircleVisual(combat, groundPos, radius, new Color(0.8f, 0.5f, 0.1f, 0.4f), 1f);
		
		var timer = combat.GetTree().CreateTimer(0.3f);
		timer.Timeout += () =>
		{
			// Impact effect
			StatusSpells.CreateImpactVisual(combat, groundPos, radius, new Color(1f, 0.6f, 0f));
			combat.CheckCircleHit(groundPos, radius, 15f, 20f, 8f);
			combat.ApplyStatusToLastHit(StatusType.Vulnerable, 4f);
		};
	}
	
	/// 1 — Shield Bash: forward strike, short stun, double if status active
	public static void VanguardShieldBash(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		
		StatusSpells.CreateConeVisual(combat, pos, forward, 3f, 2f, new Color(0.8f, 0.8f, 0.3f, 0.4f), 0.2f);
		var hits = combat.CheckMeleeCone(pos, forward, 3f, 45f, 8f, 15f, 5f);
		
		// If target has any status, apply extra stun-like effect
		foreach (ulong targetId in hits)
		{
			combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 5f, 0f, 0f, 0f);
		}
	}
	
	/// 2 — War Cry: taunt, armor, cancel stun
	public static void VanguardWarCry(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		
		// Visual: ring expanding outward
		StatusSpells.CreateCircleVisual(combat, pos, 6f, new Color(1f, 0.8f, 0.2f, 0.2f), 0.5f);
		
		// Apply shield status
		combat.ApplyStatus(StatusType.Shielded, 4f, combat.GetEntityId());
		
		// Push nearby enemies
		combat.CheckCircleHit(pos, 5f, 0f, 10f, 5f);
	}
	
	/// 3 — Intervene: dash to ally/target, shield, push around
	public static void VanguardIntervene(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = pos + forward * 8f;
		
		// Dash forward
		if (combat.GetOwnerNode() is CharacterBody3D body)
		{
			body.Velocity = forward * 30f;
		}
		
		// Visual
		StatusSpells.CreateCircleVisual(combat, target, 3f, new Color(0.5f, 0.8f, 1f, 0.3f), 1f);
		
		var timer = combat.GetTree().CreateTimer(0.2f);
		timer.Timeout += () =>
		{
			combat.CheckCircleHit(target, 3f, 5f, 12f, 5f);
			combat.ApplyStatusToLastHit(StatusType.Slowed, 3f);
		};
	}
	
	/// 4 — Thunderclap: leap to location, shock + disable zone
	public static void VanguardThunderclap(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = new Vector3(pos.X + forward.X * 6f, 0.5f, pos.Z + forward.Z * 6f);
		
		// Leap visual
		StatusSpells.CreateCircleVisual(combat, target, 4f, new Color(1f, 0.9f, 0.5f, 0.5f), 0.8f);
		
		var timer = combat.GetTree().CreateTimer(0.3f);
		timer.Timeout += () =>
		{
			StatusSpells.CreateImpactVisual(combat, target, 4f, new Color(1f, 1f, 0.5f));
			combat.CheckCircleHit(target, 4f, 20f, 25f, 10f);
		};
	}


	// ==========================================
	// WRAITH
	// ==========================================
	
	/// LMB — Shadow Strike: fast melee, bonus if moving
	public static void WraithShadowStrike(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		
		StatusSpells.CreateConeVisual(combat, pos, forward, 4f, 2.5f, new Color(0.6f, 0.2f, 0.8f, 0.3f), 0.15f);
		
		float dmg = 12f;
		float kb = 8f;
		
		// Bonus damage if recently moved (dash/sprint check via velocity)
		if (combat.GetOwnerNode() is CharacterBody3D body && body.Velocity.Length() > 5f)
		{
			dmg *= 1.5f;
			kb *= 1.5f;
		}
		
		combat.CheckMeleeCone(pos, forward, 4f, 60f, dmg, kb, 3f);
	}
	
	/// RMB — Shadow Step: directional teleport dash, 2 charges
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
		
		// Teleport visual
		StatusSpells.CreateImpactVisual(combat, pos, 2f, new Color(0.5f, 0.1f, 0.8f));
		StatusSpells.CreateImpactVisual(combat, target, 2f, new Color(0.5f, 0.1f, 0.8f));
	}
	
	/// 1 — Viper Shot: projectile, poison, consume status for double dmg
	public static void WraithViperShot(CombatComponent combat)
	{
		if (combat.GetOwnerNode() is not Node3D owner) return;
		var tree = owner.GetTree();
		if (tree?.CurrentScene == null) return;
		
		Vector3 forward = combat.GetCameraForward();
		Vector3 origin = combat.GetOwnerPosition() + forward * 2f + new Vector3(0f, 1.5f, 0f);
		
		float speed = 35f;
		float lifetime = 2.5f;
		float damage = 8f;
		
		CreateSimpleProjectile(origin, forward, speed, lifetime, 
			new Color(0.6f, 0.2f, 0.8f), tree, combat, damage, forward * 5f, StatusType.Burn, 4f);
	}
	
	/// 2 — Rapid Fire: 3 quick projectiles in cone
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
	
	/// 3 — Disengage: backward leap + forward projectile that slows
	public static void WraithDisengage(CombatComponent combat)
	{
		if (combat.GetOwnerNode() is not Node3D owner) return;
		var tree = owner.GetTree();
		if (tree?.CurrentScene == null) return;
		
		if (combat.GetOwnerNode() is CharacterBody3D body)
		{
			body.Velocity = -combat.GetCameraForward() * 20f + new Vector3(0f, 8f, 0f);
		}
		
		Vector3 forward = combat.GetCameraForward();
		Vector3 origin = owner.GlobalPosition + forward * 3f + new Vector3(0f, 1f, 0f);
		CreateSimpleProjectile(origin, forward, 30f, 2f,
			new Color(0.5f, 0.8f, 0.5f), tree, combat, 6f, forward * 4f, StatusType.Slowed, 3f);
		
		StatusSpells.CreateCircleVisual(combat, owner.GlobalPosition, 2f, new Color(0.5f, 0.8f, 0.5f, 0.2f), 0.3f);
	}
	
	/// 4 — Freezing Trap: hidden ground trap, freeze zone
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


	// ==========================================
	// CHANNELER
	// ==========================================
	
	/// LMB — Frostbolt: projectile slow, immobilize if already slowed
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
	
	/// RMB — Dragon's Breath: held fire cone, stacking burn
	public static void ChannelerDragonsBreath(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		float range = 5f;
		float radius = 3f;
		
		StatusSpells.CreateConeVisual(combat, pos, forward, range, radius, new Color(1f, 0.4f, 0f, 0.4f), 0.3f);
		
		var hits = combat.CheckMeleeCone(pos, forward, range, 60f, 8f, 3f, 1f);
		combat.ApplyStatusToLastHit(StatusType.Burn, 3f);
		
		// If already burning, stack more — handled by combat status refresh
		foreach (ulong targetId in hits)
		{
			if (combat.HasStatusOnTarget(targetId, StatusType.Burn))
			{
				combat.ApplyStatusToEntity(targetId, StatusType.Burn, 5f);
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 4f, 0f, 0f, 0f);
			}
		}
	}
	
	/// 1 — Ice Lance: beam, damage scales with slow/freeze
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
				// Consumed slow → bonus damage (simulate the "frozen" pop)
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 15f, 5f, 5f, 5f);
			}
		}
	}
	
	/// 2 — Fireball: piercing projectile, burn
	public static void ChannelerFireball(CombatComponent combat)
	{
		if (combat.GetOwnerNode() is not Node3D owner) return;
		var tree = owner.GetTree();
		if (tree?.CurrentScene == null) return;
		
		Vector3 forward = combat.GetCameraForward();
		Vector3 origin = combat.GetOwnerPosition() + forward * 2f + new Vector3(0f, 1.5f, 0f);
		
		CreateSimpleProjectile(origin, forward, 45f, 3f,
			new Color(1f, 0.4f, 0f), tree, combat, 14f, forward * 8f, StatusType.Burn, 4f);
	}
	
	/// 3 — Meteor: mark zone, delayed meteor strike, disable
	public static void ChannelerMeteor(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = new Vector3(pos.X + forward.X * 8f, 0.5f, pos.Z + forward.Z * 8f);
		float radius = 4f;
		
		// Burn zone on ground
		StatusSpells.CreateCircleVisual(combat, target, radius, new Color(1f, 0.3f, 0f, 0.3f), 2.5f);
		
		// Initial zone hit
		combat.CheckCircleHit(target, radius, 3f, 0f, 0f);
		combat.ApplyStatusToLastHit(StatusType.Burn, 3f);
		
		var timer = combat.GetTree().CreateTimer(1.5f);
		timer.Timeout += () =>
		{
			StatusSpells.CreateImpactVisual(combat, target, radius, new Color(1f, 0.2f, 0f));
			combat.CheckCircleHit(target, radius, 25f, 20f, 10f);
		};
	}
	
	/// 4 — Blink: short teleport
	public static void ChannelerBlink(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		Vector3 target = pos + forward * 8f;
		
		if (combat.GetOwnerNode() is CharacterBody3D body)
		{
			body.GlobalPosition = new Vector3(target.X, pos.Y, target.Z);
		}
		
		StatusSpells.CreateImpactVisual(combat, pos, 2f, new Color(0.3f, 0.5f, 1f));
		StatusSpells.CreateImpactVisual(combat, target, 2f, new Color(0.3f, 0.5f, 1f));
	}
	
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
		
		// Move via tween
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
