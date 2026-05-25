using Godot;
using System;
using System.Collections.Generic;
using MoveBox.Shared;

/// <summary>
/// Manages projectile visuals via Object Pooling.
/// Receives commands from LocalSimulation (or eventually Server) to spawn/move/despawn projectiles.
/// 
/// The simulation logic lives in Shared/ProjectileState.cs (pure C#).
/// This class only handles the visual representation in Godot.
/// </summary>
public partial class ProjectileManager : Node3D
{
	/// <summary>
	/// Pool of visual projectile nodes, keyed by SpellID.
	/// </summary>
	private Dictionary<ushort, Queue<Node3D>> _pools = new();
	
	/// <summary>
	/// Active projectile visuals, keyed by ProjectileID.
	/// </summary>
	private Dictionary<ulong, Node3D> _activeVisuals = new();
	
	/// <summary>
	/// Prefab factory: creates a visual node for a given spell.
	/// Extend this to support new spell visuals.
	/// </summary>
	private Node3D CreateVisual(ushort spellId)
	{
		switch (spellId)
		{
			case 1: // Fireball
				return new Fireball();
			case 2: // Ice Lance (placeholder - reuse fireball for now)
				return new Fireball();
			default:
				return new Fireball();
		}
	}
	
	/// <summary>
	/// Spawn a projectile visual at the given position.
	/// Called by LocalSimulation when a projectile is created.
	/// </summary>
	public void SpawnProjectile(ulong projectileId, ushort spellId, Vector3 position, Vector3 direction)
	{
		Node3D visual = GetFromPool(spellId);
		
		if (visual is Fireball fireball)
		{
			fireball.Reset(position, direction);
		}
		else
		{
			visual.GlobalPosition = position;
			visual.Visible = true;
		}
		
		_activeVisuals[projectileId] = visual;
	}
	
	/// <summary>
	/// Update a projectile visual's position.
	/// Called by LocalSimulation each frame.
	/// </summary>
	public void UpdateProjectilePosition(ulong projectileId, Vector3 position)
	{
		if (_activeVisuals.TryGetValue(projectileId, out var visual))
		{
			visual.GlobalPosition = position;
		}
	}
	
	/// <summary>
	/// Despawn a projectile visual and return it to the pool.
	/// Called by LocalSimulation when a projectile hits or expires.
	/// </summary>
	public void DespawnProjectile(ulong projectileId)
	{
		if (_activeVisuals.TryGetValue(projectileId, out var visual))
		{
			ReturnToPool(visual);
			_activeVisuals.Remove(projectileId);
		}
	}
	
	/// <summary>
	/// Despawn all active projectiles (e.g., on scene reset).
	/// </summary>
	public void DespawnAll()
	{
		foreach (var kvp in _activeVisuals)
		{
			ReturnToPool(kvp.Value);
		}
		_activeVisuals.Clear();
	}
	
	private Node3D GetFromPool(ushort spellId)
	{
		if (!_pools.ContainsKey(spellId))
			_pools[spellId] = new Queue<Node3D>();
		
		var pool = _pools[spellId];
		if (pool.Count > 0)
		{
			var visual = pool.Dequeue();
			visual.Visible = true;
			return visual;
		}
		
		// Create new visual
		var newVisual = CreateVisual(spellId);
		AddChild(newVisual);
		newVisual.Visible = false; // Will be shown by SpawnProjectile
		return newVisual;
	}
	
	private void ReturnToPool(Node3D visual)
	{
		visual.Visible = false;
		
		// Find which pool this belongs to
		foreach (var kvp in _pools)
		{
			// Simple heuristic: check type
			if (visual.GetType().Name == GetVisualTypeName(kvp.Key))
			{
				kvp.Value.Enqueue(visual);
				return;
			}
		}
		
		// Fallback: put in first pool
		if (_pools.Count > 0)
		{
			var firstPool = _pools.Values.GetEnumerator();
			firstPool.MoveNext();
			firstPool.Current.Enqueue(visual);
		}
	}
	
	private string GetVisualTypeName(ushort spellId)
	{
		return spellId switch
		{
			1 => "Fireball",
			2 => "Fireball", // Ice Lance placeholder
			_ => "Fireball",
		};
	}
}
