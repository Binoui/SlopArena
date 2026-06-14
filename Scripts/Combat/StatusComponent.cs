#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Manages status effects (buffs/debuffs) on a single entity.
/// Extracted from CombatComponent — ready to move to Shared/ for server authority.
/// </summary>
public partial class StatusComponent : Node
{
    private Dictionary<StatusType, float> _statuses = new();
    private LocalServerBridge? _simulation;
    private ulong _entityId;

    public event Action<StatusType, float, ulong>? OnStatusApplied;
    public event Action<StatusType>? OnStatusConsumed;
    public event Action<StatusType>? OnStatusExpired;

    public void Setup(LocalServerBridge simulation, ulong entityId)
    {
        _simulation = simulation;
        _entityId = entityId;
    }

    // ═══ SELF STATUS ═══

    public void ApplyStatus(StatusType type, float duration, ulong sourceEntityId = 0)
    {
        if (duration <= 0f) return;
        _statuses[type] = duration;
        OnStatusApplied?.Invoke(type, duration, sourceEntityId);
    }

    public bool HasStatus(StatusType type)
        => _statuses.ContainsKey(type) && _statuses[type] > 0f;

    public float GetStatusDuration(StatusType type)
        => _statuses.TryGetValue(type, out float d) ? d : 0f;

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

    public void RemoveStatus(StatusType type)
    {
        if (_statuses.Remove(type))
            OnStatusExpired?.Invoke(type);
    }

    public Dictionary<StatusType, float> GetAllStatuses()
    {
        var active = new Dictionary<StatusType, float>();
        foreach (var kvp in _statuses)
            if (kvp.Value > 0f) active[kvp.Key] = kvp.Value;
        return active;
    }

    // ═══ CROSS-ENTITY STATUS ═══

    public bool HasStatusOnTarget(ulong targetEntityId, StatusType type)
    {
        if (_simulation != null && _simulation.CombatComponents.TryGetValue(targetEntityId, out var tc))
            return tc.GetStatusComponent().HasStatus(type);
        return false;
    }

    public void ApplyStatusToEntity(ulong targetEntityId, StatusType type, float duration)
    {
        _simulation?.OnStatusApply?.Invoke(targetEntityId, type, duration, _entityId);
    }

    public bool ConsumeStatusOnTarget(ulong targetEntityId, StatusType type)
    {
        if (_simulation?.OnStatusConsume != null)
            return _simulation.OnStatusConsume(targetEntityId, type);
        return false;
    }

    // ═══ DAMAGE MODIFIERS ═══

    /// <summary>Apply status modifiers to incoming damage. Returns modified damage.</summary>
    public float ModifyIncomingDamage(float damage)
    {
        float result = damage;

        if (ConsumeStatus(StatusType.Vulnerable))
            result *= 1.3f;

        if (HasStatus(StatusType.Shielded))
        {
            result -= damage * 0.5f;
            if (result < 0f) result = 0f;
            RemoveStatus(StatusType.Shielded);
        }

        return result;
    }

    // ═══ TICK ═══

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var expired = new List<StatusType>();
        foreach (var kvp in _statuses)
        {
            float newTime = kvp.Value - dt;
            if (newTime <= 0f)
                expired.Add(kvp.Key);
            else
                _statuses[kvp.Key] = newTime;
        }

        foreach (var type in expired)
        {
            _statuses.Remove(type);
            OnStatusExpired?.Invoke(type);
        }
    }
}
