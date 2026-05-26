#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Status-interactive spells — the core of SlopArena's combo system.
/// 
/// Design principles:
/// - Apply spells apply a status to targets on hit
/// - Consume spells check targets for a status and grant bonus effects
/// - Basic spells are simple damage with no interaction
/// - Elite spells are long-CD haymakers that consume statuses for massive impact
/// 
/// This creates emergent combos: players pick spells whose interactions they discover.
/// </summary>
public static class StatusSpells
{
	// ==========================================
	// APPLY SPELLS — Apply a status effect
	// ==========================================
	
	/// <summary>
	/// Frost Bolt: projectile, applies Ralenti 3s.
	/// </summary>
	public static void FrostBolt(CombatComponent combat)
	{
		Vector3 origin = combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f);
		combat.FireProjectile(1, origin, combat.GetCameraForward());
		// Status applied by projectile hit via simulation
	}
	
	/// <summary>
	/// Shadow Mark: fast projectile, applies Marked 5s.
	/// </summary>
	public static void ShadowMark(CombatComponent combat)
	{
		Vector3 origin = combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f);
		combat.FireProjectile(1, origin, combat.GetCameraForward());
	}
	
	/// <summary>
	/// Ignite: slow projectile, applies Burn 4s.
	/// </summary>
	public static void Ignite(CombatComponent combat)
	{
		Vector3 origin = combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f);
		combat.FireProjectile(1, origin, combat.GetCameraForward());
	}
	
	/// <summary>
	/// Static Shock: beam hitscan, applies Electrified 3s.
	/// </summary>
	public static void StaticShock(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		CreateBeamVisual(combat, pos, forward, 15f, new Color(0.2f, 0.6f, 1f, 0.4f), 0.3f);
		
		var hits = combat.CheckMeleeCone(pos, forward, 15f, 5f, 10f, 5f, 2f); // Narrow beam
		combat.ApplyStatusToLastHit(StatusType.Electrified, 3f);
	}
	
	/// <summary>
	/// Sunder Armor: melee cone, applies Vulnerable 4s.
	/// </summary>
	public static void SunderArmor(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		CreateConeVisual(combat, pos, forward, 5f, 3f, new Color(0.8f, 0.3f, 0f, 0.3f), 0.3f);
		
		combat.CheckMeleeCone(pos, forward, 5f, 60f, 8f, 10f, 5f);
		combat.ApplyStatusToLastHit(StatusType.Vulnerable, 4f);
	}
	
	/// <summary>
	/// Radiant Shield: self-buff, applies Bouclier (4s).
	/// </summary>
	public static void RadiantShield(CombatComponent combat)
	{
		combat.ApplyStatus(StatusType.Shielded, 4f, combat.GetEntityId());
		
		// Visual: gold glow around self
		var pos = combat.GetOwnerPosition();
		CreateCircleVisual(combat, pos, 2f, new Color(1f, 0.8f, 0.2f, 0.3f), 4f);
	}
	
	/// <summary>
	/// Freezing Trap: delayed AoE ground trap, applies Ralenti + bonus if already Ralenti.
	/// </summary>
	public static void FreezingTrap(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		float distance = 5f;
		float radius = 4f;
		float delay = 0.8f;
		
		Vector3 impactPos = new Vector3(pos.X + forward.X * distance, 0.5f, pos.Z + forward.Z * distance);
		
		// Visual indicator
		var indicator = CreateAoEIndicator(impactPos, radius, new Color(0.3f, 0.7f, 1f, 0.3f));
		AddToScene(combat, indicator);
		
		var delayTimer = combat.GetTree().CreateTimer(delay);
		delayTimer.Timeout += () =>
		{
			if (!GodotObject.IsInstanceValid(indicator)) return;
			
			// Apply status + damage
			combat.CheckCircleHit(impactPos, radius, 10f, 5f, 2f);
			combat.ApplyStatusToLastHit(StatusType.Slowed, 4f);
			
			// If target already Ralenti → also stun
			// (handled by checking after hit)
			foreach (ulong targetId in combat.GetSimulation()?.CombatComponents?.Keys ?? new Dictionary<ulong, CombatComponent>().Keys)
			{
				if (combat.ConsumeStatusOnTarget(targetId, StatusType.Slowed))
				{
					// Re-apply Ralenti + extra stun is handled by the duration refresh
					combat.ApplyStatusToEntity(targetId, StatusType.Slowed, 4f);
				}
			}
			
			var cleanup = combat.GetTree().CreateTimer(0.5f);
			cleanup.Timeout += () => indicator.QueueFree();
		};
	}
	
	/// <summary>
	/// Corrupted Ground: AoE zone, applies Vulnerable + Burn.
	/// </summary>
	public static void CorruptedGround(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		float distance = 6f;
		float radius = 5f;
		float duration = 4f;
		
		Vector3 impactPos = new Vector3(pos.X + forward.X * distance, 0.5f, pos.Z + forward.Z * distance);
		
		// Visual zone
		var zone = CreateAoEIndicator(impactPos, radius, new Color(0.5f, 0f, 0.5f, 0.3f));
		AddToScene(combat, zone);
		
		// Initial hit
		combat.CheckCircleHit(impactPos, radius, 5f, 3f, 1f);
		combat.ApplyStatusToLastHit(StatusType.Vulnerable, 3f);
		
		// Tick damage over time (Burn ticks)
		var tickCount = 0;
		var tickTimer = combat.GetTree().CreateTimer(0.5f, false);
		System.Action tick = () => {};
		tick = () =>
		{
			if (!GodotObject.IsInstanceValid(zone)) return;
			tickCount++;
			if (tickCount > duration * 2) return;
			
			combat.CheckCircleHit(impactPos, radius, 3f, 0f, 0f);
			combat.ApplyStatusToLastHit(StatusType.Burn, 3f);
			
			var nextTick = combat.GetTree().CreateTimer(0.5f, false);
			nextTick.Timeout += tick;
		};
		tickTimer.Timeout += tick;
		
		var cleanup = combat.GetTree().CreateTimer(duration);
		cleanup.Timeout += () => zone.QueueFree();
	}
	
	// ==========================================
	// CONSUME SPELLS — Consume a status for bonus
	// ==========================================
	
	/// <summary>
	/// Piercing Shot: projectile, CONSUME Marked → +100% damage.
	/// </summary>
	public static void PiercingShot(CombatComponent combat)
	{
		Vector3 origin = combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f);
		combat.FireProjectile(1, origin, combat.GetCameraForward());
		// Bonus damage handled via projectile's SpellDefinition or via hit check
	}
	
	/// <summary>
	/// Frost Lance: beam, CONSUME Ralenti → stun 0.75s.
	/// </summary>
	public static void FrostLance(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		CreateBeamVisual(combat, pos, forward, 12f, new Color(0.5f, 0.8f, 1f, 0.5f), 0.3f);
		
		var hits = combat.CheckMeleeCone(pos, forward, 12f, 4f, 25f, 15f, 5f);
		foreach (ulong targetId in hits)
		{
			if (combat.ConsumeStatusOnTarget(targetId, StatusType.Slowed))
			{
				// Bonus: apply stun-like effect (longer stun duration)
				// For now, just deal extra damage via simulation
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 15f, 0f, 0f, 0f);
			}
		}
	}
	
	/// <summary>
	/// Combustion: melee strike, CONSUME Burn → AoE explosion.
	/// </summary>
	public static void Combustion(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		
		var hits = combat.CheckMeleeCone(pos, forward, 5f, 90f, 20f, 15f, 5f);
		
		bool anyConsumed = false;
		foreach (ulong targetId in hits)
		{
			if (combat.ConsumeStatusOnTarget(targetId, StatusType.Burn))
			{
				anyConsumed = true;
			}
		}
		
		if (anyConsumed)
		{
			// Explosion AoE on self
			var explosionPos = pos + forward * 2f;
			combat.CheckCircleHit(explosionPos, 4f, 30f, 20f, 8f);
			
			// Visual explosion
			var boom = CreateAoEIndicator(explosionPos, 4f, new Color(1f, 0.5f, 0f, 0.5f));
			AddToScene(combat, boom);
			var cleanup = combat.GetTree().CreateTimer(0.5f);
			cleanup.Timeout += () => boom.QueueFree();
		}
	}
	
	/// <summary>
	/// Execute: melee cone, CONSUME Vulnerable → +150% damage.
	/// </summary>
	public static void Execute(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		CreateConeVisual(combat, pos, forward, 6f, 4f, new Color(1f, 0f, 0f, 0.4f), 0.4f);
		
		var hits = combat.CheckMeleeCone(pos, forward, 6f, 60f, 25f, 30f, 10f);
		foreach (ulong targetId in hits)
		{
			if (combat.ConsumeStatusOnTarget(targetId, StatusType.Vulnerable))
			{
				// Bonus damage
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 37f, 5f, 10f, 5f);
			}
		}
	}
	
	/// <summary>
	/// Overload: AoE around self, CONSUME Electrified → stun 1.5s.
	/// </summary>
	public static void Overload(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		CreateCircleVisual(combat, pos, 5f, new Color(0.2f, 0.4f, 1f, 0.4f), 0.5f);
		
		var hits = combat.CheckCircleHit(pos, 5f, 15f, 15f, 5f);
		foreach (ulong targetId in hits)
		{
			if (combat.ConsumeStatusOnTarget(targetId, StatusType.Electrified))
			{
				// Bonus stun + damage
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 20f, 0f, 5f, 0f);
			}
		}
	}
	
	/// <summary>
	/// Shield Bash: melee strike, CONSUME Bouclier → stun.
	/// </summary>
	public static void ShieldBash(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		CreateConeVisual(combat, pos, forward, 4f, 2f, new Color(1f, 0.9f, 0.5f, 0.4f), 0.3f);
		
		var hits = combat.CheckMeleeCone(pos, forward, 4f, 45f, 10f, 40f, 5f);
		
		// Check if CASTER has Bouclier → consume for stun on primary target
		if (combat.ConsumeStatus(StatusType.Shielded))
		{
			if (hits.Count > 0)
			{
				// Bonus damage on first hit
				combat.GetSimulation()?.OnEntityHit?.Invoke(hits[0], 20f, 10f, 5f, 10f);
			}
		}
	}
	
	// ==========================================
	// BASIC SPELLS (no status interaction)
	// ==========================================
	
	/// <summary>
	/// Wind Slash: basic melee attack.
	/// </summary>
	public static void WindSlash(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		CreateConeVisual(combat, pos, forward, 5f, 3f, new Color(0.8f, 0.8f, 0.8f, 0.2f), 0.2f);
		combat.CheckMeleeCone(pos, forward, 5f, 90f, 18f, 10f, 3f);
	}
	
	/// <summary>
	/// Arcane Shot: basic ranged attack.
	/// </summary>
	public static void ArcaneShot(CombatComponent combat)
	{
		Vector3 origin = combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f);
		combat.FireProjectile(1, origin, combat.GetCameraForward());
	}
	
	// ==========================================
	// MOBILITY / UTILITY
	// ==========================================
	
	/// <summary>
	/// Dash Roll: quick dodge with i-frames.
	/// </summary>
	public static void DashRoll(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		combat.ApplyKnockback(forward * 50f);
		
		// Visual
		var pos = combat.GetOwnerPosition();
		CreateCircleVisual(combat, pos, 1.5f, new Color(0.5f, 0.5f, 0.5f, 0.2f), 0.3f);
	}
	
	/// <summary>
	/// Blink: teleport forward.
	/// </summary>
	public static void Blink(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		float blinkDist = 10f;
		Vector3 newPos = combat.GetOwnerPosition() + forward * blinkDist;
		newPos.Y = Math.Max(newPos.Y, 2f);
		
		var owner = combat.GetOwnerNode();
		if (owner != null)
		{
			owner.GlobalPosition = newPos;
			if (owner is CharacterBody3D cb)
				cb.Velocity = Vector3.Zero;
		}
	}
	
	/// <summary>
	/// Counter: parry next attack. For now, buff self.
	/// </summary>
	public static void Counter(CombatComponent combat)
	{
		// Simple: apply Bouclier + a brief counter window
		combat.ApplyStatus(StatusType.Shielded, 1.5f, combat.GetEntityId());
		
		// Visual
		var pos = combat.GetOwnerPosition();
		var shield = CreateAoEIndicator(pos, 2f, new Color(0.5f, 0.8f, 1f, 0.2f));
		AddToScene(combat, shield);
		var cleanup = combat.GetTree().CreateTimer(1.5f);
		cleanup.Timeout += () => shield.QueueFree();
	}
	
	// ==========================================
	// ELITE SPELLS (Ultimates)
	// ==========================================
	
	/// <summary>
	/// Meteor Rain: 5 meteors over 3s. CONSUME Burn → +50% damage per meteor.
	/// </summary>
	public static void MeteorRain(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		float distance = 12f;
		float radius = 5f;
		int meteorCount = 5;
		float interval = 3f / meteorCount;
		
		Vector3 centerPos = new Vector3(pos.X + forward.X * distance, 0.5f, pos.Z + forward.Z * distance);
		
		// Check if caster has Burn → consume for bonus
		bool empowered = combat.ConsumeStatus(StatusType.Burn);
		float dmgMult = empowered ? 1.5f : 1f;
		
		for (int i = 0; i < meteorCount; i++)
		{
			float t = i * interval;
			var timer = combat.GetTree().CreateTimer(t);
			float offsetX = (float)(new Random().NextDouble() - 0.5f) * radius * 2f;
			float offsetZ = (float)(new Random().NextDouble() - 0.5f) * radius * 2f;
			Vector3 meteorPos = new Vector3(centerPos.X + offsetX, 0.5f, centerPos.Z + offsetZ);
			
			timer.Timeout += () =>
			{
				CreateImpactVisual(combat, meteorPos, radius, new Color(1f, 0.3f, 0f));
				combat.CheckCircleHit(meteorPos, radius, 40f * dmgMult, 30f, 15f);
			};
		}
	}
	
	/// <summary>
	/// Annihilate: massive cone. CONSUME Vulnerable → +100% damage.
	/// </summary>
	public static void Annihilate(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		CreateConeVisual(combat, pos, forward, 8f, 6f, new Color(1f, 0f, 0f, 0.6f), 0.6f);
		
		var hits = combat.CheckMeleeCone(pos, forward, 8f, 90f, 100f, 60f, 20f);
		
		// Check targets for Vulnerable
		foreach (ulong targetId in hits)
		{
			if (combat.ConsumeStatusOnTarget(targetId, StatusType.Vulnerable))
			{
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, 100f, 10f, 20f, 10f);
			}
		}
	}
	
	/// <summary>
	/// Storm Surge: your next spells cast faster. CONSUME Electrified → longer.
	/// </summary>
	public static void StormSurge(CombatComponent combat)
	{
		// Buff: reduce cast times by 50% for next 3 spells
		combat.ApplyStatus(StatusType.Electrified, 6f, combat.GetEntityId()); // reuse status as buff tracker
		
		bool empowered = combat.ConsumeStatus(StatusType.Electrified);
		float duration = empowered ? 10f : 6f;
		
		// Visual
		var pos = combat.GetOwnerPosition();
		var aura = CreateAoEIndicator(pos, 3f, new Color(0.2f, 0.5f, 1f, 0.3f));
		AddToScene(combat, aura);
		var cleanup = combat.GetTree().CreateTimer(duration);
		cleanup.Timeout += () => aura.QueueFree();
		
		GD.Print($"Storm Surge activated! Duration: {duration}s");
	}
	
	/// <summary>
	/// Dark Pact: sacrifice HP for massive burst. CONSUME Bouclier → no HP cost.
	/// </summary>
	public static void DarkPact(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		
		bool shielded = combat.ConsumeStatus(StatusType.Shielded);
		
		if (!shielded)
		{
			// Sacrifice HP: damage self
			combat.GetSimulation()?.OnEntityHit?.Invoke(combat.GetEntityId(), 30f, 0f, 0f, 0f);
		}
		
		// Projectile dealing 80 damage + applies Burn + Vulnerable
		combat.FireProjectile(1, pos + forward * 2f + new Vector3(0f, 1f, 0f), forward);
		
		GD.Print($"Dark Pact! Shielded: {shielded}");
	}
	
	// ==========================================
	// NEW STATUS SPELLS
	// ==========================================
	
	/// <summary>
	/// Feedback Pulse: AoE around self, CONSUME ALL statuses -> +dmg per status consumed.
	/// </summary>
	public static void FeedbackPulse(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		float radius = 5f;
		
		CreateCircleVisual(combat, pos, radius, new Color(0.8f, 0.2f, 0.8f, 0.4f), 0.5f);
		
		// Count and consume all active statuses on each target
		var hits = combat.CheckCircleHit(pos, radius, 10f, 5f, 2f);
		foreach (ulong targetId in hits)
		{
			float bonusDmg = 0f;
			foreach (StatusType status in Enum.GetValues<StatusType>())
			{
				if (combat.ConsumeStatusOnTarget(targetId, status))
					bonusDmg += 15f;
			}
			if (bonusDmg > 0f)
			{
				combat.GetSimulation()?.OnEntityHit?.Invoke(targetId, bonusDmg, 0f, 0f, 0f);
			}
		}
		
		// Also consume statuses from self (caster burst)
		float selfBonus = 0f;
		foreach (StatusType status in Enum.GetValues<StatusType>())
		{
			if (combat.ConsumeStatus(status))
				selfBonus += 10f;
		}
		if (selfBonus > 0f)
		{
			GD.Print($"Feedback Pulse consumed {selfBonus / 10f} statuses from self!");
		}
	}
	
	/// <summary>
	/// Dark Harvest: ranged projectile, CONSUME any status -> heal caster.
	/// </summary>
	public static void DarkHarvest(CombatComponent combat)
	{
		Vector3 origin = combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f);
		combat.FireProjectile(1, origin, combat.GetCameraForward());
		GD.Print("Dark Harvest!");
	}
	
	/// <summary>
	/// Force Push: AoE pushback around self.
	/// </summary>
	public static void ForcePush(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		float radius = 5f;
		
		CreateCircleVisual(combat, pos, radius, new Color(0.6f, 0.8f, 1f, 0.3f), 0.4f);
		
		// Push with strong knockback
		combat.CheckCircleHit(pos, radius, 5f, 40f, 15f);
	}
	
	/// <summary>
	/// Phase Shift: brief invulnerability + shield.
	/// </summary>
	public static void PhaseShift(CombatComponent combat)
	{
		// Apply Bouclier for a brief duration
		combat.ApplyStatus(StatusType.Shielded, 1.5f, combat.GetEntityId());
		
		// Visual: shimmer around self
		var pos = combat.GetOwnerPosition();
		var aura = CreateAoEIndicator(pos, 2.5f, new Color(0.3f, 0.6f, 1f, 0.2f));
		AddToScene(combat, aura);
		var cleanup = combat.GetTree().CreateTimer(1.5f);
		cleanup.Timeout += () => aura.QueueFree();
		
		GD.Print("Phase Shift active!");
	}
	
	/// <summary>
	/// Purify: remove all debuffs from self.
	/// </summary>
	public static void Purify(CombatComponent combat)
	{
		// Remove all negative statuses (all except Bouclier - which is positive)
		combat.RemoveStatus(StatusType.Slowed);
		combat.RemoveStatus(StatusType.Burn);
		combat.RemoveStatus(StatusType.Marked);
		combat.RemoveStatus(StatusType.Electrified);
		combat.RemoveStatus(StatusType.Vulnerable);
		
		// Visual: cleansing burst
		var pos = combat.GetOwnerPosition();
		CreateCircleVisual(combat, pos, 3f, new Color(0.5f, 1f, 0.5f, 0.3f), 0.5f);
		
		GD.Print("Purify: debuffs cleansed!");
	}
	
	/// <summary>
	/// Magic Barrier: +50% magic resist (reduces incoming damage).
	/// </summary>
	public static void MagicBarrier(CombatComponent combat)
	{
		// Apply Bouclier with longer duration to represent magic barrier
		combat.ApplyStatus(StatusType.Shielded, 6f, combat.GetEntityId());
		
		// Visual: blue barrier
		var pos = combat.GetOwnerPosition();
		var barrier = CreateAoEIndicator(pos, 3f, new Color(0.2f, 0.3f, 1f, 0.2f));
		AddToScene(combat, barrier);
		var cleanup = combat.GetTree().CreateTimer(6f);
		cleanup.Timeout += () => barrier.QueueFree();
		
		GD.Print("Magic Barrier active!");
	}
	
	/// <summary>
	/// Blood Pact: sacrifice HP to gain damage buff.
	/// </summary>
	public static void BloodPact(CombatComponent combat)
	{
		// Self-damage
		combat.GetSimulation()?.OnEntityHit?.Invoke(combat.GetEntityId(), 20f, 0f, 0f, 0f);
		
		// Apply Electrified as a damage buff (consumed by Storm Surge for longer duration)
		combat.ApplyStatus(StatusType.Electrified, 6f, combat.GetEntityId());
		
		// Visual: red burst
		var pos = combat.GetOwnerPosition();
		CreateCircleVisual(combat, pos, 3f, new Color(1f, 0.2f, 0f, 0.4f), 0.5f);
		
		GD.Print("Blood Pact: HP sacrificed for power!");
	}
	
	/// <summary>
	/// Time Warp: zone that slows enemies (applies Ralenti).
	/// </summary>
	public static void TimeWarp(CombatComponent combat)
	{
		Vector3 forward = combat.GetCameraForward();
		Vector3 pos = combat.GetOwnerPosition();
		float distance = 10f;
		float radius = 6f;
		float duration = 5f;
		
		Vector3 centerPos = new Vector3(pos.X + forward.X * distance, 0.5f, pos.Z + forward.Z * distance);
		
		// Visual zone
		var zone = CreateAoEIndicator(centerPos, radius, new Color(0.2f, 0.5f, 0.8f, 0.3f));
		AddToScene(combat, zone);
		
		// Initial hit
		combat.CheckCircleHit(centerPos, radius, 5f, 0f, 0f);
		combat.ApplyStatusToLastHit(StatusType.Slowed, 3f);
		
		// Tick apply Ralenti over duration
		var tickCount = 0;
		var tickTimer = combat.GetTree().CreateTimer(0.5f, false);
		System.Action tick = null;
		tick = () =>
		{
			if (!GodotObject.IsInstanceValid(zone)) return;
			tickCount++;
			if (tickCount > duration * 2) return;
			
			combat.CheckCircleHit(centerPos, radius, 0f, 0f, 0f);
			combat.ApplyStatusToLastHit(StatusType.Slowed, 3f);
			
			var nextTick = combat.GetTree().CreateTimer(0.5f, false);
			nextTick.Timeout += tick;
		};
		tickTimer.Timeout += tick;
		
		var cleanup = combat.GetTree().CreateTimer(duration);
		cleanup.Timeout += () => zone.QueueFree();
		
		GD.Print("Time Warp active!");
	}
	
	// ==========================================
	// VISUAL HELPERS
	// ==========================================
	
	private static MeshInstance3D CreateAoEIndicator(Vector3 position, float radius, Color color)
	{
		var mesh = new MeshInstance3D();
		var cyl = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.3f };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = new Color(color.R, color.G, color.B),
			EmissionEnergyMultiplier = 2f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		cyl.Material = mat;
		mesh.Mesh = cyl;
		mesh.GlobalPosition = position;
		return mesh;
	}
	
	private static void CreateCircleVisual(CombatComponent combat, Vector3 pos, float radius, Color color, float duration)
	{
		var mesh = new MeshInstance3D();
		var cyl = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.2f };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		cyl.Material = mat;
		mesh.Mesh = cyl;
		mesh.GlobalPosition = new Vector3(pos.X, 0.2f, pos.Z);
		AddToScene(combat, mesh);
		var timer = combat.GetTree().CreateTimer(duration);
		timer.Timeout += () => mesh.QueueFree();
	}
	
	private static void CreateConeVisual(CombatComponent combat, Vector3 pos, Vector3 forward, float range, float radius, Color color, float duration)
	{
		var mesh = new MeshInstance3D();
		var cylinder = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = range };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		mesh.Mesh = cylinder;
		mesh.MaterialOverride = mat;
		float midX = pos.X + forward.X * range * 0.5f;
		float midZ = pos.Z + forward.Z * range * 0.5f;
		float yaw = MathF.Atan2(-forward.X, -forward.Z);
		mesh.Rotation = new Vector3(0f, yaw, 0f);
		
		var owner = combat.GetOwnerNode();
		if (owner != null)
		{
			owner.AddChild(mesh);
			mesh.GlobalPosition = new Vector3(midX, 2f, midZ);
		}
		
		var timer = combat.GetTree().CreateTimer(duration);
		timer.Timeout += () => mesh.QueueFree();
	}
	
	private static void CreateBeamVisual(CombatComponent combat, Vector3 origin, Vector3 direction, float length, Color color, float duration)
	{
		var mesh = new MeshInstance3D();
		var cylinder = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = length };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = new Color(color.R, color.G, color.B),
			EmissionEnergyMultiplier = 3f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		mesh.Mesh = cylinder;
		mesh.MaterialOverride = mat;
		float midX = origin.X + direction.X * length * 0.5f;
		float midZ = origin.Z + direction.Z * length * 0.5f;
		float yaw = MathF.Atan2(-direction.X, -direction.Z);
		mesh.Rotation = new Vector3(0f, yaw, 0f);
		mesh.GlobalPosition = new Vector3(midX, 2f, midZ);
		AddToScene(combat, mesh);
		
		var timer = combat.GetTree().CreateTimer(duration);
		timer.Timeout += () => mesh.QueueFree();
	}
	
	private static void CreateImpactVisual(CombatComponent combat, Vector3 pos, float radius, Color color)
	{
		var mesh = CreateAoEIndicator(pos, radius, new Color(color.R, color.G, color.B, 0.5f));
		AddToScene(combat, mesh);
		var timer = combat.GetTree().CreateTimer(0.4f);
		timer.Timeout += () => mesh.QueueFree();
	}
	
	private static void AddToScene(CombatComponent combat, Node3D node)
	{
		var tree = combat.GetTree();
		if (tree?.CurrentScene != null)
			tree.CurrentScene.AddChild(node);
	}
}
