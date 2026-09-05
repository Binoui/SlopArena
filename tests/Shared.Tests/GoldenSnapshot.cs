using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Golden file snapshot for one entity.
/// Only includes gameplay-relevant fields — skips transient/internal state
/// that would produce noisy diffs (input state, warp data, facing angles, etc.).
/// </summary>
internal record EntitySnapshot
{
    public int State { get; init; }
    /// <summary>
    /// Facing yaw in radians (ADR-0017/0018: sticky air facing, LMB snap, target-lock
    /// tracking). Included so the facing-behavior scenarios (drift-no-reface,
    /// snap-then-normal, snap-rejected, lock tracking) are actually pinned. Existing
    /// goldens predate the field; they gain it on the next regeneration — the values
    /// are the pinned behavior going forward.
    /// </summary>
    public float FacingYaw { get; init; }
    public float PX { get; init; }
    public float PY { get; init; }
    public float PZ { get; init; }
    public float VX { get; init; }
    public float VY { get; init; }
    public float VZ { get; init; }
    public ushort DamagePercent { get; init; }
    public byte Deaths { get; init; }
    /// <summary>Persistent target lock (ADR-0018). Default false, omitted from JSON when off.</summary>
    public bool LockOn { get; init; }
    public byte ComboStage { get; init; }
    public byte AttackSlot { get; init; }
    public ushort HitstunTicks { get; init; }
    public ushort HitstopTicks { get; init; }
    public ushort AirTimeTicks { get; init; }
    public ushort ChargeTicks { get; init; }
    public ushort Cooldown0 { get; init; }
    public ushort Cooldown1 { get; init; }
    public ushort Cooldown2 { get; init; }
    public ushort Cooldown3 { get; init; }
    public ushort Cooldown4 { get; init; }
    public ushort Cooldown5 { get; init; }
    public byte JumpsLeft { get; init; }
    public bool IsGrounded { get; init; }
    public ushort InvincibilityTicks { get; init; }
    public ushort DashDurationTicks { get; init; }

    public static EntitySnapshot FromState(CharacterState s) => new()
    {
        State = (int)s.State,
        FacingYaw = s.FacingYaw,
        PX = s.PX, PY = s.PY, PZ = s.PZ,
        VX = s.VX, VY = s.VY, VZ = s.VZ,
        DamagePercent = s.DamagePercent,
        Deaths = s.Deaths,
        LockOn = s.LockOn,
        ComboStage = s.ComboStage,
        AttackSlot = s.AttackSlot,
        HitstunTicks = s.HitstunTicks,
        HitstopTicks = s.HitstopTicks,
        AirTimeTicks = s.AirTimeTicks,
        ChargeTicks = s.ChargeTicks,
        Cooldown0 = s.Cooldown0,
        Cooldown1 = s.Cooldown1,
        Cooldown2 = s.Cooldown2,
        Cooldown3 = s.Cooldown3,
        Cooldown4 = s.Cooldown4,
        Cooldown5 = s.Cooldown5,
        JumpsLeft = s.JumpsLeft,
        IsGrounded = s.IsGrounded,
        InvincibilityTicks = s.InvincibilityTicks,
        DashDurationTicks = s.DashDurationTicks,
    };
}

/// <summary>
/// Top-level golden snapshot for a scenario.
/// Contains tick count + state at snapshot tick + final state.
/// </summary>
internal record StateSnapshot
{
    public string Scenario { get; init; } = "";
    public int TickCount { get; init; }
    /// <summary>State captured at SnapshotTick (mid-ability, hitbox active).</summary>
    public EntitySnapshot PlayerSnap { get; init; } = null!;
    public EntitySnapshot? NpcSnap { get; init; }
    /// <summary>State at final tick (ability completed, settled).</summary>
    public EntitySnapshot PlayerFinal { get; init; } = null!;
    public EntitySnapshot? NpcFinal { get; init; }
}

/// <summary>
/// Serialize/deserialize golden snapshots and manage golden files.
/// </summary>
internal static class GoldenSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>
    /// Path to the golden files directory, relative to repo root.
    /// </summary>
    public static string GoldenDir => Path.Combine(
        RepoRoot, "tests", "Shared.Tests", "Golden");

    private static string RepoRoot => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Serialize a snapshot to JSON string.</summary>
    public static string Serialize(StateSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, JsonOptions);

    /// <summary>
    /// Deserialize a snapshot from JSON string.
    /// </summary>
    public static StateSnapshot Deserialize(string json)
        => JsonSerializer.Deserialize<StateSnapshot>(json, JsonOptions)
           ?? throw new InvalidOperationException("Failed to deserialize golden snapshot");

    /// <summary>
    /// Get the golden file path for a named scenario.
    /// </summary>
    public static string GoldenPath(string scenarioName)
    {
        string safeName = scenarioName.Replace(" ", "_").Replace("/", "_");
        return Path.Combine(GoldenDir, $"{safeName}.json");
    }

    /// <summary>
    /// Get the golden file path for a scenario.
    /// </summary>
    public static string GoldenPath(KitScenario scenario) => GoldenPath(scenario.Name);
}
