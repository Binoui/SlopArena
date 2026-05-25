#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using MoveBox.Shared;

/// <summary>
/// Generic combat component usable by PlayerController, Dummy, or AI bots.
/// 
/// Responsibilities:
/// - Owns the SpellSystem (cooldowns, spell registry, slot assignment)
/// - Resolves spell effects via Shared/SpellResolver (pure C#)
/// - Communicates hits back via OnEntityHit
/// - Handles knockback application
/// - Manages status effects (apply, tick, consume)
/// 
/// Architecture:
///   CombatComponent
///     ├── SpellSystem (cooldowns, slot management)
///     ├── LocalSimulation (projectile tracking, entity positions)
///     └── SpellResolver (Shared, pure C# math)
/// </summary>
public partial class CombatComponent : Node
{
	// ==========================================
	// REFERENCES
	// ==========================================
	
	private Node3D? _owner;
	private LocalSimulation? _simulation;
	private ulong _entityId = 1;
	private SpellSystem? _spellSystem;
	
	// ==========================================
	// EVENTS
	// ==========================================
	
	/// <summary>
	/// Fired when this entity takes damage.
	/// Parameters: damage, knockbackX, knockbackY, knockbackZ
	/// </summary>
	public event Action<float, float, float, float>? OnTakeDamage;
	
	/// <summary>
	/// Fired when this entity deals damage to another entity.
	/// Parameters: targetEntityId, damage, knockbackX, knockbackY, knockbackZ
	/// </summary>
	public event Action<ulong, float, float, float, float>? OnDealDamage;
	
	/// <summary>
	/// Fired when a status is applied (for visual feedback).
	/// Parameters: statusType, duration, sourceEntityId
	/// </summary>
	public event Action<StatusType, float, ulong>? OnStatusApplied;
	
	/// <summary>
	/// Fired when a status is consumed (removed for bonus effect).
	/// </summary>
	public event Action<StatusType>? OnStatusConsumed;
	
	/// <summary>
	/// Fired when a status expires naturally.
	/// </summary>
	public event Action<StatusType>? OnStatusExpired;
	
	// ==========================================
	// STATUS EFFECTS
	// ==========================================
	
	private Dictionary<StatusType, float> _statuses = new Dictionary<StatusType, float>();
	
	/// <summary>
	/// Tracks which entities were hit in the most recent hit check,
	/// so spell effects can apply statuses to what they just hit.
	/// </summary>
	private List<ulong> _lastHitTargets = new List<ulong>();
	
	// ==========================================
	// SETUP
	// ==========================================
	
	/// <summary>
	/// Initialize the combat component.
	/// </summary>
	/// <param name="owner">The owning Node3D (player, dummy, etc.)</param>
	/// <param name="simulation">Reference to the local simulation</param>
	/// <param name="entityId">Unique entity ID in the simulation</param>
	public void Setup(Node3D owner, LocalSimulation simulation, ulong entityId)
	{
		_owner = owner;
		_simulation = simulation;
		_entityId = entityId;
		
		// Create SpellSystem as child
		_spellSystem = new SpellSystem();
		_spellSystem.Name = "SpellSystem";
		AddChild(_spellSystem);
	}
	
	/// <summary>
	/// Get the SpellSystem for UI/slot management.
	/// </summary>
	public SpellSystem? GetSpellSystem() => _spellSystem;
	
	/// <summary>
	/// Get the entity ID in the simulation.
	/// </summary>
	public ulong GetEntityId() => _entityId;
	
	// ==========================================
	// SPELL CASTING
	// ==========================================
	
	/// <summary>
	/// Trigger a spell slot (checks cooldowns, executes effect).
	/// </summary>
	public bool TriggerSlot(SlotType slot)
	{
		if (_spellSystem == null) return false;
		return _spellSystem.TriggerSlot(slot, this);
	}
	
	/// <summary>
	/// Fire a projectile spell toward the given direction.
	/// Called by spell effects (RangedSpells, MeleeSpells).
	/// </summary>
	public void FireProjectile(ushort spellId, Vector3 origin, Vector3 direction)
	{
		if (_simulation == null) return;
		_simulation.FireSpell(spellId, origin, direction, _entityId);
	}
	
	/// <summary>
	/// Check melee cone hit against all entities in the simulation.
	/// Uses Shared/SpellResolver for pure C# math.
	/// Also tracks hit targets for subsequent status application.
	/// Returns the list of target entity IDs that were hit.
	/// </summary>
	public List<ulong> CheckMeleeCone(Vector3 origin, Vector3 forward, float range, float halfAngleDeg, float damage, float knockbackForce, float knockbackUpward)
	{
		_lastHitTargets.Clear();
		
		if (_simulation == null) return _lastHitTargets;
		
		float halfAngleRad = halfAngleDeg * MathF.PI / 180f;
		
		// Build entity list for SpellResolver
		var entities = new List<SpellResolver.EntityData>();
		foreach (var kvp in _simulation.Entities)
		{
			entities.Add(new SpellResolver.EntityData
			{
				Id = kvp.Key,
				PosX = kvp.Value.pos.X,
				PosY = kvp.Value.pos.Y,
				PosZ = kvp.Value.pos.Z,
				Radius = kvp.Value.radius,
				Active = kvp.Value.active
			});
		}
		
		var results = SpellResolver.ResolveConeHit(
			origin.X, origin.Y, origin.Z,
			forward.X, forward.Z,
			halfAngleRad, range,
			damage, knockbackForce, knockbackUpward,
			_entityId,
			entities
		);
		
		foreach (var hit in results)
		{
			_lastHitTargets.Add(hit.TargetEntityId);
			_simulation.OnEntityHit?.Invoke(hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
			OnDealDamage?.Invoke(hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
		}
		
		return _lastHitTargets;
	}
	
	/// <summary>
	/// Check circular AoE hit at a position.
	/// Uses Shared/SpellResolver for pure C# math.
	/// Also tracks hit targets for subsequent status application.
	/// </summary>
	public List<ulong> CheckCircleHit(Vector3 center, float radius, float damage, float knockbackForce, float knockbackUpward)
	{
		_lastHitTargets.Clear();
		
		if (_simulation == null) return _lastHitTargets;
		
		var entities = new List<SpellResolver.EntityData>();
		foreach (var kvp in _simulation.Entities)
		{
			entities.Add(new SpellResolver.EntityData
			{
				Id = kvp.Key,
				PosX = kvp.Value.pos.X,
				PosY = kvp.Value.pos.Y,
				PosZ = kvp.Value.pos.Z,
				Radius = kvp.Value.radius,
				Active = kvp.Value.active
			});
		}
		
		var results = SpellResolver.ResolveCircleHit(
			center.X, center.Y, center.Z,
			radius,
			damage, knockbackForce, knockbackUpward,
			_entityId,
			entities
		);
		
		foreach (var hit in results)
		{
			_lastHitTargets.Add(hit.TargetEntityId);
			_simulation.OnEntityHit?.Invoke(hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
			OnDealDamage?.Invoke(hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
		}
		
		return _lastHitTargets;
	}
	
	// ==========================================
	// STATUS EFFECTS
	// ==========================================
	
	/// <summary>
	/// Apply a status effect to this entity for a duration.
	/// If status already exists, refreshes to max duration.
	/// </summary>
	public void ApplyStatus(StatusType type, float duration, ulong sourceEntityId = 0)
	{
		if (duration <= 0f) return;
		
		_statuses[type] = duration;
		OnStatusApplied?.Invoke(type, duration, sourceEntityId);
	}
	
	/// <summary>
	/// Check if this entity currently has a status active.
	/// </summary>
	public bool HasStatus(StatusType type)
	{
		return _statuses.ContainsKey(type) && _statuses[type] > 0f;
	}
	
	/// <summary>
	/// Get remaining duration of a status (0 if not active).
	/// </summary>
	public float GetStatusDuration(StatusType type)
	{
		if (_statuses.TryGetValue(type, out float duration))
			return duration;
		return 0f;
	}
	
	/// <summary>
	/// Consume (remove) a status and return true if it was active.
	/// Used for one-shot bonus effects.
	/// </summary>
	public bool ConsumeStatus(StatusType type)
	{
		if (HasStatus(type))
		{
			_statuses.Remove(type);
			OnStatusConsumed?.Invoke(type);
			return true;
		}
		return false;
	}
	
	/// <summary>
	/// Remove a status without consuming it.
	/// </summary>
	public void RemoveStatus(StatusType type)
	{
		if (_statuses.Remove(type))
		{
			OnStatusExpired?.Invoke(type);
		}
	}
	
	/// <summary>
	/// Get all active statuses and their remaining durations.
	/// </summary>
	public Dictionary<StatusType, float> GetAllStatuses()
	{
		var active = new Dictionary<StatusType, float>();
		foreach (var kvp in _statuses)
		{
			if (kvp.Value > 0f)
				active[kvp.Key] = kvp.Value;
		}
		return active;
	}
	
	/// <summary>
	/// Apply a status to all entities hit in the most recent CheckMeleeCone/CheckCircleHit call.
	/// This is used by spell effects after dealing damage.
	/// </summary>
	public void ApplyStatusToLastHit(StatusType type, float duration)
	{
		foreach (ulong targetId in _lastHitTargets)
		{
			// We need to find the target's CombatComponent to apply the status
			// The target's CombatComponent is found via the entity system
			ApplyStatusToEntity(targetId, type, duration);
		}
	}
	
	/// <summary>
	/// Apply a status to a specific entity by ID.
	/// Looks up the entity's CombatComponent via the simulation's entity-owner mapping.
	/// </summary>
	public void ApplyStatusToEntity(ulong targetEntityId, StatusType type, float duration)
	{
		if (_simulation == null) return;
		
		// Route via simulation's status apply event
		_simulation.OnStatusApply?.Invoke(targetEntityId, type, duration, _entityId);
	}
	
	/// <summary>
	/// Check if the entity this is attached to has a specific status 
	/// and can consume it. Used by spells on the caster (self-buffs, etc.)
	/// </summary>
	public bool ConsumeStatusOnTarget(ulong targetEntityId, StatusType type)
	{
		if (_simulation == null) return false;
		
		// Route consumption request via simulation
		if (_simulation.OnStatusConsume != null)
			return _simulation.OnStatusConsume(targetEntityId, type);
		return false;
	}
	
	// ==========================================
	// DAMAGE / KNOCKBACK
	// ==========================================
	
	/// <summary>
	/// Apply knockback to the owner entity.
	/// </summary>
	public void ApplyKnockback(Vector3 force)
	{
		if (_owner is PlayerController player)
		{
			player.ApplyKnockback(force);
		}
		// Future: handle AI bot knockback here too
	}
	
	/// <summary>
	/// Take damage (called by LocalSimulation when this entity is hit).
	/// Applies status modifiers: Vulnérable → +30%, Bouclier → absorbs damage.
	/// </summary>
	public void TakeDamage(float damage, float kbX, float kbY, float kbZ)
	{
		float finalDamage = damage;
		
		// Check Vulnérable → +30% damage
		if (ConsumeStatus(StatusType.Vulnerable))
		{
			finalDamage *= 1.3f;
		}
		
		// Check Bouclier → absorbs damage
		if (HasStatus(StatusType.Bouclier))
		{
			float shieldAbsorb = damage * 0.5f; // 50% damage reduction while shielded
			finalDamage -= shieldAbsorb;
			if (finalDamage < 0f) finalDamage = 0f;
			
			// Bouclier expires after absorbing
			RemoveStatus(StatusType.Bouclier);
		}
		
		OnTakeDamage?.Invoke(finalDamage, kbX, kbY, kbZ);
		ApplyKnockback(new Vector3(kbX, kbY, kbZ));
	}
	
	// ==========================================
	// STATUS TICK (called from _Process)
	// ==========================================
	
	public override void _Process(double delta)
	{
		float dt = (float)delta;
		
		// Tick down status durations
		var expired = new List<StatusType>();
		foreach (var kvp in _statuses)
		{
			float newTime = kvp.Value - dt;
			if (newTime <= 0f)
			{
				expired.Add(kvp.Key);
			}
			else
			{
				_statuses[kvp.Key] = newTime;
			}
		}
		
		foreach (var type in expired)
		{
			_statuses.Remove(type);
			OnStatusExpired?.Invoke(type);
		}
		
		// Tick Brulure damage
		if (_statuses.ContainsKey(StatusType.Brulure) && _statuses[StatusType.Brulure] > 0f)
		{
			// Brulure ticks once per second
			// We handle this in the simulation for now - just visual/event
		}
		
		// Electrifie stacking
		if (_statuses.ContainsKey(StatusType.Electrifie) && _statuses[StatusType.Electrifie] > 0f)
		{
			// Electrifie at 2+ stacks stuns - handled by spell effects currently
		}
	}
	
	// ==========================================
	// ACCESSORS (used by spell effects)
	// ==========================================
	
	/// <summary>
	/// Get the owner Node3D.
	/// </summary>
	public Node3D? GetOwnerNode() => _owner;
	
	/// <summary>
	/// Get the simulation reference.
	/// </summary>
	public LocalSimulation? GetSimulation() => _simulation;
	
	// ==========================================
	// POSITION HELPERS
	// ==========================================
	
	/// <summary>
	/// Get the forward direction of the owner (for melee spells).
	/// </summary>
	public Vector3 GetOwnerForward()
	{
		if (_owner != null)
		{
			Vector3 forward = -_owner.Transform.Basis.Z;
			forward.Y = 0;
			return forward.Normalized();
		}
		return Vector3.Forward;
	}
	
	/// <summary>
	/// Get the camera forward direction (for ranged spells).
	/// Falls back to owner forward if no camera.
	/// </summary>
	public Vector3 GetCameraForward()
	{
		if (_owner is PlayerController player)
		{
			return player.GetCameraForward();
		}
		return GetOwnerForward();
	}
	
	/// <summary>
	/// Get the owner's global position.
	/// </summary>
	public Vector3 GetOwnerPosition()
	{
		return _owner?.GlobalPosition ?? Vector3.Zero;
	}
}
