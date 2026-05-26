using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Local simulation of combat logic.
/// In the future, this will be replaced by the authoritative Server.
/// For now, it runs the same Shared logic locally so we can test.
/// 
/// Responsibilities:
/// - Track active projectiles (ProjectileState from Shared)
/// - Check hits against entities (using CombatMath from Shared)
/// - Notify ProjectileManager to update visuals
/// - Notify DummyManager/PlayerController of hits
/// </summary>
public partial class LocalSimulation : Node
{
	private ulong _nextProjectileId = 1;
	private Dictionary<ulong, ProjectileState> _projectiles = new();
	
	/// <summary>
	/// Reference to the visual manager.
	/// </summary>
	public ProjectileManager? ProjectileVisuals { get; set; }
	
	/// <summary>
	/// Reference to entity positions for hit detection.
	/// Key = entity ID, Value = (position, radius, isActive)
	/// </summary>
	public Dictionary<ulong, (Vector3 pos, float radius, bool active)> Entities { get; set; } = new();
	
	/// <summary>
	/// Map entityId → CombatComponent for status routing.
	/// Populated by Main.cs when entities are registered.
	/// </summary>
	public Dictionary<ulong, CombatComponent> CombatComponents { get; set; } = new();
	
	/// <summary>
	/// Fired when an entity takes damage.
	/// Parameters: entityId, damage, knockbackX, knockbackY, knockbackZ
	/// Public Action (not event) so CombatComponent and spells can invoke it.
	/// </summary>
	public Action<ulong, float, float, float, float>? OnEntityHit;
	
	/// <summary>
	/// Fired to apply a status to a target entity.
	/// Parameters: targetEntityId, statusType, duration, sourceEntityId
	/// Main.cs listens and routes to the target's CombatComponent.
	/// </summary>
	public Action<ulong, StatusType, float, ulong>? OnStatusApply;
	
	/// <summary>
	/// Fired to consume a status on a target entity.
	/// Parameters: targetEntityId, statusType
	/// Returns true if the status was active and consumed.
	/// </summary>
	public Func<ulong, StatusType, bool>? OnStatusConsume;
	
	/// <summary>
	/// Fire a spell from a caster position in a direction.
	/// Returns the projectile ID, or 0 if the spell doesn't spawn a projectile.
	/// </summary>
	public ulong FireSpell(ushort spellId, Vector3 casterPos, Vector3 direction, ulong casterEntityId)
	{
		var spell = SpellCatalog.GetSpell(spellId);
		
		switch (spell.Shape)
		{
			case SpellShape.SlowProjectile:
			case SpellShape.FastProjectile:
				return SpawnProjectile(spell, casterPos, direction, casterEntityId);
				
			case SpellShape.Beam:
				// Hitscan - check line immediately
				CheckBeamHit(casterPos, direction, spell, casterEntityId);
				return 0;
				
			case SpellShape.MeleeCone:
				CheckConeHit(casterPos, direction, spell, casterEntityId);
				return 0;
				
			case SpellShape.DelayedAoE:
			case SpellShape.Trap:
				// These are handled visually by the spell effect (RangedSpells/MeleeSpells)
				// The simulation just needs to know about them for hit detection
				// For now, they're triggered via CombatComponent.CheckCircleHit
				return 0;
				
			default:
				return 0;
		}
	}
	
	private ulong SpawnProjectile(SpellDefinition spell, Vector3 casterPos, Vector3 direction, ulong casterEntityId)
	{
		Vector3 dir = new Vector3(direction.X, 0f, direction.Z).Normalized();
		ulong id = _nextProjectileId++;
		
		var state = new ProjectileState(
			id, spell.SpellID,
			casterPos.X, casterPos.Y, casterPos.Z,
			dir.X, dir.Y, dir.Z
		);
		
		_projectiles[id] = state;
		
		// Notify visuals
		ProjectileVisuals?.SpawnProjectile(id, spell.SpellID, casterPos, dir);
		
		return id;
	}
	
	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		
		List<ulong> toRemove = new();
		
		foreach (var kvp in _projectiles)
		{
			ulong id = kvp.Key;
			ProjectileState state = kvp.Value;
			
			// Save old position for line intersection check
			float oldX = state.PosX;
			float oldZ = state.PosZ;
			
			// Update projectile position (pure math from Shared)
			var spell = SpellCatalog.GetSpell(state.SpellID);
			bool stillActive = state.Update(dt, spell);
			
			if (!stillActive)
			{
				toRemove.Add(id);
				continue;
			}
			
			// Update visual position
			ProjectileVisuals?.UpdateProjectilePosition(id, 
				new Vector3(state.PosX, state.PosY, state.PosZ));
			
			// Check hits against entities
			bool hit = CheckProjectileHit(id, state, spell, oldX, oldZ);
			if (hit)
			{
				toRemove.Add(id);
			}
		}
		
		// Cleanup
		foreach (var id in toRemove)
		{
			_projectiles.Remove(id);
			ProjectileVisuals?.DespawnProjectile(id);
		}
	}
	
	private bool CheckProjectileHit(ulong projectileId, ProjectileState state, SpellDefinition spell, float oldX, float oldZ)
	{
		foreach (var entityKvp in Entities)
		{
			ulong entityId = entityKvp.Key;
			var (pos, radius, active) = entityKvp.Value;
			
			if (!active) continue;
			
			// Line intersection check (segment from old pos to new pos)
			bool intersects = CombatMath.LineIntersectsCircle(
				oldX, oldZ,
				state.PosX, state.PosZ,
				pos.X, pos.Z,
				radius
			);
			
			if (intersects)
			{
				// Calculate knockback
				CombatMath.CalculateKnockback(
					state.PosX, state.PosY, state.PosZ,
					pos.X, pos.Y, pos.Z, // Attacker position approximated
					spell.KnockbackForce, spell.KnockbackUpward,
					out float kbX, out float kbY, out float kbZ
				);
				
				OnEntityHit?.Invoke(entityId, spell.Damage, kbX, kbY, kbZ);
				return true; // Projectile consumed
			}
		}
		
		return false;
	}
	
	private void CheckBeamHit(Vector3 origin, Vector3 direction, SpellDefinition spell, ulong casterEntityId)
	{
		foreach (var entityKvp in Entities)
		{
			ulong entityId = entityKvp.Key;
			var (pos, radius, active) = entityKvp.Value;
			
			if (!active || entityId == casterEntityId) continue;
			
			// Check if entity is within beam range and near the line
			float dx = pos.X - origin.X;
			float dz = pos.Z - origin.Z;
			float dist = MathF.Sqrt(dx * dx + dz * dz);
			
			if (dist > spell.Range + radius) continue;
			
			// Check if entity is within beam width (perpendicular distance)
			Vector3 dir = new Vector3(direction.X, 0f, direction.Z).Normalized();
			Vector3 toEntity = new Vector3(dx, 0f, dz);
			float perpDist = (toEntity - dir * toEntity.Dot(dir)).Length();
			
			if (perpDist < spell.Radius + radius)
			{
				CombatMath.CalculateKnockback(
					pos.X, pos.Y, pos.Z,
					origin.X, origin.Y, origin.Z,
					spell.KnockbackForce, spell.KnockbackUpward,
					out float kbX, out float kbY, out float kbZ
				);
				
				OnEntityHit?.Invoke(entityId, spell.Damage, kbX, kbY, kbZ);
			}
		}
	}
	
	private void CheckCircleHit(Vector3 center, SpellDefinition spell, ulong casterEntityId)
	{
		foreach (var entityKvp in Entities)
		{
			ulong entityId = entityKvp.Key;
			var (pos, radius, active) = entityKvp.Value;
			
			if (!active || entityId == casterEntityId) continue;
			
			bool inRange = CombatMath.IsInCircle(
				pos.X, pos.Y, pos.Z,
				center.X, center.Y, center.Z,
				spell.Radius + radius // Add entity radius
			);
			
			if (inRange)
			{
				CombatMath.CalculateKnockback(
					pos.X, pos.Y, pos.Z,
					center.X, center.Y, center.Z,
					spell.KnockbackForce, spell.KnockbackUpward,
					out float kbX, out float kbY, out float kbZ
				);
				
				OnEntityHit?.Invoke(entityId, spell.Damage, kbX, kbY, kbZ);
			}
		}
	}
	
	private void CheckConeHit(Vector3 origin, Vector3 direction, SpellDefinition spell, ulong casterEntityId)
	{
		float halfAngle = MathF.PI / 4f; // 45 degrees half-angle = 90 degree cone
		
		foreach (var entityKvp in Entities)
		{
			ulong entityId = entityKvp.Key;
			var (pos, radius, active) = entityKvp.Value;
			
			if (!active || entityId == casterEntityId) continue;
			
			bool inCone = CombatMath.IsInCone(
				pos.X, pos.Y, pos.Z,
				origin.X, origin.Y, origin.Z,
				direction.X, direction.Z,
				halfAngle,
				spell.Range + radius
			);
			
			if (inCone)
			{
				CombatMath.CalculateKnockback(
					pos.X, pos.Y, pos.Z,
					origin.X, origin.Y, origin.Z,
					spell.KnockbackForce, spell.KnockbackUpward,
					out float kbX, out float kbY, out float kbZ
				);
				
				OnEntityHit?.Invoke(entityId, spell.Damage, kbX, kbY, kbZ);
			}
		}
	}
}
