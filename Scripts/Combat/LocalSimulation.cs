#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Local simulation — entity state, hurtbox computation, hitbox resolution.
///
/// Responsibilities:
/// - Track entity positions + facing (from CharacterState)
/// - Compute world-space hurtbox capsules from CharacterDefinition offsets
/// - Resolve all active hitboxes each frame via SpellResolver
/// - Route damage to CombatComponents
///
/// Server-compatible: Tick() uses only floats and pure C# math.
/// All Godot types are in the update layer, not in the simulation core.
/// </summary>
public partial class LocalSimulation : Node
{
    /// <summary>
    /// Minimal entity state needed for hit detection.
    /// </summary>
    private struct EntitySimState
    {
        public float PX, PY, PZ;
        public float FacingYaw;
        public CharacterDefinition CharDef;
    }
    private readonly Dictionary<ulong, EntitySimState> _states = new();

    /// <summary>
    /// Map entityId → CombatComponent for damage/status routing.
    /// Populated by Main/MatchManager when entities are registered.
    /// </summary>
    public Dictionary<ulong, CombatComponent> CombatComponents { get; set; } = new();

    /// <summary>
    /// Update an entity's state each frame (called from MatchManager).
    /// </summary>
    public void UpdateEntityState(ulong id, float px, float py, float pz, float yaw, CharacterDefinition def)
    {
        _states[id] = new EntitySimState { PX = px, PY = py, PZ = pz, FacingYaw = yaw, CharDef = def };
    }

    /// <summary>
    /// Remove an entity from state tracking (e.g., on despawn).
    /// </summary>
    public void RemoveEntity(ulong id) => _states.Remove(id);

    /// <summary>
    /// Resolve all active hitboxes against all entities this tick.
    /// Computes world-space hurtbox positions from stored states + character definitions.
    /// </summary>
    public void Tick()
    {
        if (_states.Count == 0) return;

        // Build entity list from tracked states + definition capsules
        var entityList = new List<SpellResolver.EntityData>();
        foreach (var kvp in _states)
        {
            var state = kvp.Value;
            float cos = Mathf.Cos(state.FacingYaw);
            float sin = Mathf.Sin(state.FacingYaw);

            foreach (var cap in state.CharDef.HurtboxCapsules)
            {
                // Rotate capsule offsets by character facing yaw
                float sx = state.PX + cap.Sx * cos - cap.Sz * sin;
                float sy = state.PY + cap.Sy;
                float sz = state.PZ + cap.Sx * sin + cap.Sz * cos;
                float ex = state.PX + cap.Ex * cos - cap.Ez * sin;
                float ey = state.PY + cap.Ey;
                float ez = state.PZ + cap.Ex * sin + cap.Ez * cos;

                bool isCapsule = (sx != ex || sy != ey || sz != ez);
                entityList.Add(new SpellResolver.EntityData
                {
                    Id = kvp.Key,
                    PosX = sx, PosY = sy, PosZ = sz,
                    Radius = cap.Radius,
                    Shape = isCapsule ? HitboxShape.Capsule : HitboxShape.Sphere,
                    EndX = ex, EndY = ey, EndZ = ez,
                    Active = true,
                });
            }
        }

        // Resolve hitboxes
        var results = SpellResolver.Tick(entityList);
        foreach (var hit in results)
        {
            RouteHit(hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
            OnDealDamage?.Invoke(hit.OwnerEntityId, hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
        }
    }

    /// <summary>
    /// Route a hit to the target entity's CombatComponent.
    /// </summary>
    public void RouteHit(ulong entityId, float damage, float kbX, float kbY, float kbZ)
    {
        if (CombatComponents.TryGetValue(entityId, out var targetCombat))
            targetCombat.TakeDamage(damage, kbX, kbY, kbZ);
        OnEntityHit?.Invoke(entityId, damage, kbX, kbY, kbZ);
    }

    // ── EVENTS (for UI, auto-target, etc.) ──

    /// <summary>Fired when an entity takes damage.</summary>
    public Action<ulong, float, float, float, float>? OnEntityHit;

    /// <summary>Fired when an entity deals damage to another.</summary>
    public Action<ulong, ulong, float, float, float, float>? OnDealDamage;

    /// <summary>Route status application to target's CombatComponent.</summary>
    public Action<ulong, StatusType, float, ulong>? OnStatusApply;

    /// <summary>Route status consumption to target's CombatComponent.</summary>
    public Func<ulong, StatusType, bool>? OnStatusConsume;

    // ── DEBUG / EXTERNAL ACCESS ──

    /// <summary>
    /// Get current world-space hurtbox capsules for debug draw.
    /// Returns same data as Tick() uses.
    /// </summary>
    public List<(float sx, float sy, float sz, float ex, float ey, float ez, float radius, bool isCapsule)> GetHurtboxCapsules()
    {
        var result = new List<(float, float, float, float, float, float, float, bool)>();
        foreach (var kvp in _states)
        {
            var state = kvp.Value;
            float cos = Mathf.Cos(state.FacingYaw);
            float sin = Mathf.Sin(state.FacingYaw);
            foreach (var cap in state.CharDef.HurtboxCapsules)
            {
                float sx = state.PX + cap.Sx * cos - cap.Sz * sin;
                float sy = state.PY + cap.Sy;
                float sz = state.PZ + cap.Sx * sin + cap.Sz * cos;
                float ex = state.PX + cap.Ex * cos - cap.Ez * sin;
                float ey = state.PY + cap.Ey;
                float ez = state.PZ + cap.Ex * sin + cap.Ez * cos;
                bool capsule = (sx != ex || sy != ey || sz != ez);
                result.Add((sx, sy, sz, ex, ey, ez, cap.Radius, capsule));
            }
        }
        return result;
    }
}
