#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Generic combat component usable by PlayerController, Dummy, or AI bots.
///
/// Responsibilities:
/// - Resolves spell effects via Shared/SpellResolver (pure C#)
/// - Communicates hits back via OnEntityHit
/// - Handles knockback application
/// - Manages status effects (apply, tick, consume)
///
/// Architecture:
///   CombatComponent
///     ├── LocalSimulation (entity positions, hit routing)
///     └── SpellResolver (Shared, pure C# math)
/// </summary>
public partial class CombatComponent : Node
{
    // ==========================================
    // REFERENCES
    // ==========================================

    private Node3D? _owner;
    private LocalServerBridge? _simulation;
    private ulong _entityId = 1;
    private SpellVFXManager? _spellVFX;
    private TargetLockSystem? _targetLock;
    private AttackWarping? _warpSystem;
    private StatusComponent _statusComp = null!;

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

    // Status events delegated to StatusComponent — use GetStatusComponent().OnStatusApplied etc.

    // ==========================================
    // STATUS EFFECTS (delegated to StatusComponent)
    // ==========================================

    private readonly SpellResolver _spellResolver = new();

    /// <summary>
    /// Tracks which entities were hit in the most recent hit check,
    /// so spell effects can apply statuses to what they just hit.
    /// </summary>
    private List<ulong> _lastHitTargets = new List<ulong>();

    public StatusComponent GetStatusComponent() => _statusComp;

    public void ApplyStatus(StatusType type, float duration, ulong sourceEntityId = 0)
        => _statusComp.ApplyStatus(type, duration, sourceEntityId);

    public bool HasStatus(StatusType type) => _statusComp.HasStatus(type);
    public float GetStatusDuration(StatusType type) => _statusComp.GetStatusDuration(type);
    public bool ConsumeStatus(StatusType type) => _statusComp.ConsumeStatus(type);
    public void RemoveStatus(StatusType type) => _statusComp.RemoveStatus(type);

    public Dictionary<StatusType, float> GetAllStatuses() => _statusComp.GetAllStatuses();

    public List<ulong> GetTargetsFromLastHit() => _lastHitTargets;

    public bool HasStatusOnTarget(ulong targetEntityId, StatusType type)
        => _statusComp.HasStatusOnTarget(targetEntityId, type);

    public void ApplyStatusToLastHit(StatusType type, float duration)
    {
        foreach (ulong targetId in _lastHitTargets)
            _statusComp.ApplyStatusToEntity(targetId, type, duration);
    }

    public void ApplyStatusToEntity(ulong targetEntityId, StatusType type, float duration)
        => _statusComp.ApplyStatusToEntity(targetEntityId, type, duration);

    public bool ConsumeStatusOnTarget(ulong targetEntityId, StatusType type)
        => _statusComp.ConsumeStatusOnTarget(targetEntityId, type);

    // ==========================================
    // SETUP
    // ==========================================

    public void Setup(Node3D owner, LocalServerBridge simulation, ulong entityId, SpellVFXManager? spellVFX = null, TargetLockSystem? targetLock = null)
    {
        _owner = owner;
        _simulation = simulation;
        _entityId = entityId;
        _spellVFX = spellVFX;
        _targetLock = targetLock;
        _statusComp = new StatusComponent { Name = "StatusComponent" };
        AddChild(_statusComp);
        _statusComp.Setup(simulation, entityId);

        if (targetLock != null)
        {
            _warpSystem = new AttackWarping();
            _warpSystem.Setup(owner, targetLock);
            AddChild(_warpSystem);
        }
    }

    public SpellVFXManager? GetSpellVFX() => _spellVFX;
    public ulong GetEntityId() => _entityId;

    // ==========================================
    // MELEE HIT DETECTION
    // ==========================================

    /// <summary>
    /// Check melee cone hit against all entities in the simulation.
    public List<ulong> CheckMeleeCone(Vector3 origin, Vector3 forward, float range, float damage, float knockbackForce, float knockbackUpward)
    {
        _lastHitTargets.Clear();
        if (_simulation == null) return _lastHitTargets;

        var entities = BuildEntityList();
        var hb = new Hitbox
        {
            X = origin.X + (forward.X * range * 0.5f),
            Y = origin.Y + 1f,
            Z = origin.Z + (forward.Z * range * 0.5f),
            Radius = range * 0.5f,
            DurationTicks = 5,
            Damage = damage,
            KnockbackForce = knockbackForce,
            KnockbackUpward = knockbackUpward,
            OwnerId = _entityId,
        };
        _spellResolver.Spawn(hb);

        foreach (var hit in _spellResolver.Tick(entities))
        {
            _lastHitTargets.Add(hit.TargetEntityId);
            _simulation.RouteHit(hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
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

        var entities = BuildEntityList();
        var hb = new Hitbox
        {
            X = center.X,
            Y = center.Y,
            Z = center.Z,
            Radius = radius,
            DurationTicks = 5,
            Damage = damage,
            KnockbackForce = knockbackForce,
            KnockbackUpward = knockbackUpward,
            OwnerId = _entityId,
        };
        _spellResolver.Spawn(hb);

        foreach (var hit in _spellResolver.Tick(entities))
        {
            _lastHitTargets.Add(hit.TargetEntityId);
            _simulation.RouteHit(hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
            OnDealDamage?.Invoke(hit.TargetEntityId, hit.Damage, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
        }

        return _lastHitTargets;
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
    /// Applies status modifiers: Vulnerable → +30%, Bouclier → absorbs damage.
    /// </summary>
    public void TakeDamage(float damage, float kbX, float kbY, float kbZ)
    {
        float finalDamage = _statusComp.ModifyIncomingDamage(damage);

        // Apply damage to movement component
        if (_owner is PlayerController player)
            player.ApplyDamageToMovement(finalDamage);

        OnTakeDamage?.Invoke(finalDamage, kbX, kbY, kbZ);
        ApplyKnockback(new Vector3(kbX, kbY, kbZ));
    }

    // ==========================================
    // Status tick handled by StatusComponent._Process

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
    public LocalServerBridge? GetSimulation() => _simulation;

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

    // ==========================================
    // DKO-STYLE ATTACK EXECUTION
    // ==========================================

    /// <summary>
    /// Execute attack with Range-based range checking + warping.
    /// Checks target distance and initiates warp if in warp range.
    /// Callback fires after warp completes (or immediately if no warp needed).
    /// </summary>
    public void ExecuteAttackWithWarp(AttackStage stage, float warpSpeed, Action onAttackStart)
    {
        // No target lock system → execute immediately
        if (!stage.UseTargetLock || _targetLock == null || _warpSystem == null)
        {
            onAttackStart?.Invoke();
            return;
        }

        // No valid target → execute immediately
        if (_targetLock.CurrentTarget == null)
        {
            onAttackStart?.Invoke();
            return;
        }

        float distToTarget = _targetLock.GetDistanceToTarget();

        // Check ranges
        if (distToTarget <= stage.AttackRange)
        {
            // In attack range → execute immediately
            onAttackStart?.Invoke();
        }
        else if (distToTarget <= stage.WarpRange)
        {
            // In warp range → dash toward target first
            GD.Print($"[Warp] Target {distToTarget:F1}m away, warping to {stage.AttackRange:F1}m");
            _warpSystem.StartWarp(stage.AttackRange, warpSpeed, () => onAttackStart?.Invoke());
        }
        else
        {
            // Out of range → attack in place (will likely miss)
            onAttackStart?.Invoke();
        }
    }

    /// <summary>
    /// Cancel active warp (e.g., player got hit during warp startup).
    /// </summary>
    public void CancelAttackWarp()
    {
        _warpSystem?.CancelWarp();
    }

    /// <summary>
    /// Check if currently warping toward target.
    /// </summary>
    public bool IsWarping()
    {
        return _warpSystem?.IsWarping ?? false;
    }

    /// <summary>
    /// Get the final warp direction (for hitbox spawning after warp completes).
    /// Returns Vector3.Zero if no warp system or no warp occurred.
    /// </summary>
    public Vector3 GetFinalWarpDirection()
    {
        return _warpSystem?.GetFinalWarpDirection() ?? Vector3.Zero;
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private List<SpellResolver.EntityData> BuildEntityList()
    {
        var entities = new List<SpellResolver.EntityData>();
        if (_simulation == null) return entities;
        var capsules = _simulation.GetHurtboxCapsules();
        foreach (var (sx, sy, sz, ex, ey, ez, radius, isCap) in capsules)
        {
            entities.Add(new SpellResolver.EntityData
            {
                PosX = sx,
                PosY = sy,
                PosZ = sz,
                Radius = radius,
                Shape = isCap ? HitboxShape.Capsule : HitboxShape.Sphere,
                EndX = ex,
                EndY = ey,
                EndZ = ez,
                Active = true
            });
        }
        return entities;
    }
}
