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
/// - Auto-route hits to target's CombatComponent.TakeDamage()
/// - Map entity IDs to CombatComponents for status interaction
///
/// In the future, this will receive authoritative state from the Server.
/// For now, it runs locally so we can test combat.
/// </summary>
public partial class LocalSimulation : Node
{
    /// <summary>
    /// Entity hurtbox capsules for hit detection.
    /// Key = entity ID, Value = list of (start, end, radius) — capsules following bones.
    /// Start==end means a sphere (degenerate capsule).
    /// </summary>
    public Dictionary<ulong, List<(Vector3 start, Vector3 end, float radius)>> Entities { get; set; } = new();

    /// <summary>
    /// Map entityId → CombatComponent for status routing.
    /// Populated by Main.cs when entities are registered.
    /// </summary>
    public Dictionary<ulong, CombatComponent> CombatComponents { get; set; } = new();

    /// <summary>
    /// Fired when an entity takes damage (after CombatComponent.TakeDamage is called).
    /// External listeners (Main.cs for UI, auto-target, etc.) subscribe here.
    /// Parameters: entityId, damage, knockbackX, knockbackY, knockbackZ
    /// </summary>
    public Action<ulong, float, float, float, float>? OnEntityHit;

    /// <summary>
    /// Route a hit to the target entity's CombatComponent.
    /// Called by ability resolvers when a hit connects.
    /// </summary>
    public void RouteHit(ulong entityId, float damage, float kbX, float kbY, float kbZ)
    {
        // Apply damage via CombatComponent (status modifiers, damage%, knockback)
        if (CombatComponents.TryGetValue(entityId, out var targetCombat))
            targetCombat.TakeDamage(damage, kbX, kbY, kbZ);

        // Fire external handlers (UI, auto-target, damage numbers)
        OnEntityHit?.Invoke(entityId, damage, kbX, kbY, kbZ);
    }

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
