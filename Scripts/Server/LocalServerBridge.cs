#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Bridges the pure C# ServerSimulation to the Godot scene.
/// Each frame: collects inputs → server tick → applies states to PlayerControllers.
/// Handles events, CombatComponent routing, and debug visualization.
///
/// Replaces LocalSimulation in the pipeline while keeping the Godot-side
/// event wiring (OnEntityHit, OnDealDamage, OnRespawn).
/// </summary>
public partial class LocalServerBridge : Node
{
    private ServerSimulation _server = null!;
    private ArenaDefinition _arenaDef;

    /// <summary>Map entityId → CombatComponent for damage/status events.</summary>
    public Dictionary<ulong, CombatComponent> CombatComponents { get; set; } = new();

    // ── EVENTS (for UI, auto-target, etc.) ──
    public Action<ulong, float, float, float, float>? OnEntityHit;
    public Action<ulong, ulong, float, float, float, float>? OnDealDamage;
    public Action<ulong, StatusType, float, ulong>? OnStatusApply;
    public Func<ulong, StatusType, bool>? OnStatusConsume;
    /// <summary>Fired when an entity goes out of bounds (void death).</summary>
    public Action<ulong>? OnEntityOutOfBounds;

    public LocalServerBridge(ArenaDefinition arenaDef)
    {
        _arenaDef = arenaDef;
        _server = new ServerSimulation(arenaDef);
    }

    /// <summary>Register an entity in the server simulation.</summary>
    public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, byte[]? glbData = null)
    {
        ServerSkeleton? skel = null;
        if (glbData != null)
        {
            try { skel = ServerSkeleton.LoadFromGlb(glbData); }
            catch (Exception ex) { GD.PrintErr($"[Bridge] Failed to load skeleton for {id}: {ex.Message}"); }
        }
        _server.RegisterEntity(id, def, initialState, skel);
        if (skel != null) StoreDef(id, def);
    }

    public void RemoveEntity(ulong id)
    {
        _server.RemoveEntity(id);
        CombatComponents.Remove(id);
    }

    /// <summary>
    /// Run one tick: collect inputs → server tick → apply states → fire events.
    /// Call from MatchManager._Process.
    /// </summary>
    public void Tick(Dictionary<ulong, InputState> inputs)
    {
        // Snapshot states before tick (for detecting void deaths)
        var beforeY = new Dictionary<ulong, float>();
        foreach (var kvp in _server.GetAllStates())
            beforeY[kvp.Key] = kvp.Value.PY;

        // Server tick
        _server.Tick(inputs);

        // Fire events for new damage + void death
        foreach (var kvp in _server.GetAllStates())
        {
            ulong id = kvp.Key;
            var state = kvp.Value;

            // Void death detection
            if (beforeY.TryGetValue(id, out var oldY) && oldY >= _arenaDef.KillHeight && state.PY < _arenaDef.KillHeight)
            {
                OnEntityOutOfBounds?.Invoke(id);
            }
        }
    }

    /// <summary>Get authoritative state for an entity.</summary>
    public CharacterState GetState(ulong id) => _server.GetState(id);

    /// <summary>Get all server states.</summary>
    public Dictionary<ulong, CharacterState> GetAllStates() => _server.GetAllStates();

    /// <summary>Apply a server state to a Godot PlayerController body.</summary>
    public static void ApplyStateToBody(PlayerController body, CharacterState state)
    {
        // Set position directly (authoritative)
        body.GlobalPosition = new Vector3(state.PX, state.PY, state.PZ);

        // Set velocity (already includes knockback from SimulateTick)
        body.Velocity = new Vector3(state.VX, state.VY, state.VZ);

        // Facing
        body.GlobalRotation = new Vector3(0f, state.FacingYaw, 0f);
    }

    /// <summary>
    /// Route a hit to the target entity's CombatComponent (for legacy ability effects).
    /// </summary>
    public void RouteHit(ulong entityId, float damage, float kbX, float kbY, float kbZ)
    {
        if (CombatComponents.TryGetValue(entityId, out var targetCombat))
            targetCombat.TakeDamage(damage, kbX, kbY, kbZ);
        OnEntityHit?.Invoke(entityId, damage, kbX, kbY, kbZ);
    }

    // ── DEBUG ──

    public List<(float sx, float sy, float sz, float ex, float ey, float ez, float radius, bool isCapsule)> GetHurtboxCapsules()
    {
        var result = new List<(float, float, float, float, float, float, float, bool)>();
        var states = _server.GetAllStates();
        var defs = GetDefs();

        foreach (var kvp in states)
        {
            if (!defs.TryGetValue(kvp.Key, out var def)) continue;
            var state = kvp.Value;
            float cos = MathF.Cos(state.FacingYaw);
            float sin = MathF.Sin(state.FacingYaw);
            foreach (var cap in def.HurtboxCapsules)
            {
                float sx = state.PX + cap.Sx * cos - cap.Sz * sin;
                float sy = state.PY + cap.Sy;
                float sz = state.PZ + cap.Sx * sin + cap.Sz * cos;
                float ex = state.PX + cap.Ex * cos - cap.Ez * sin;
                float ey = state.PY + cap.Ey;
                float ez = state.PZ + cap.Ex * sin + cap.Ez * cos;
                bool isCap = (sx != ex || sy != ey || sz != ez);
                result.Add((sx, sy, sz, ex, ey, ez, cap.Radius, isCap));
            }
        }
        return result;
    }

    // Expose defs for debug (internal accessor via reflection or public)
    private Dictionary<ulong, CharacterDefinition> GetDefs()
    {
        // We need to store defs ourselves since ServerSimulation keeps them private.
        // For now, bridge stores them alongside.
        return _defs;
    }

    // Store defs locally for debug access
    private readonly Dictionary<ulong, CharacterDefinition> _defs = new();
    public void StoreDef(ulong id, CharacterDefinition def) => _defs[id] = def;
}
