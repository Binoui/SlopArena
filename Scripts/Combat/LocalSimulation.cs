#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Local simulation — entity registry and hit routing.
///
/// Responsibilities:
/// - Track entity positions for hit detection
/// - Route hits (OnEntityHit), status applies (OnStatusApply), status consumes (OnStatusConsume)
/// - Map entity IDs to CombatComponents for status interaction
///
/// In the future, this will receive authoritative state from the Server.
/// For now, it runs locally so we can test combat.
/// </summary>
public partial class LocalSimulation : Node
{
	/// <summary>
	/// Entity positions for hit detection.
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
	/// </summary>
	public Action<ulong, float, float, float, float>? OnEntityHit;

	/// <summary>
	/// Fired to apply a status to a target entity.
	/// Main.cs listens and routes to the target's CombatComponent.
	/// </summary>
	public Action<ulong, StatusType, float, ulong>? OnStatusApply;

	/// <summary>
	/// Fired to consume a status on a target entity.
	/// Returns true if the status was active and consumed.
	/// </summary>
	public Func<ulong, StatusType, bool>? OnStatusConsume;
}
