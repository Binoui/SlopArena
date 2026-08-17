using System.Collections.Generic;

namespace SlopArena.Shared.AI;

/// <summary>One attack swing by one entity: which slot, the active window, and whether it connected.</summary>
public sealed class SwingRecord
{
    public ulong Attacker;
    public ulong Target;
    public byte ActiveSlot;
    /// <summary>True when the attacker was airborne at the swing start (distinguishes g1 vs a1).</summary>
    public bool Air;
    public int StartTick;
    /// <summary>Active window length in ticks (max trigger+duration of the move's first-stage hitboxes).</summary>
    public int WindowTicks;
    public bool Connected;
    /// <summary>The opponent's position relative to the attacker, in the attacker's facing frame
    /// (side / forward / height, metres), sampled at swing start. Meaningful when <see cref="Connected"/>
    /// is false (a whiff) — "where did I swing and not connect".</summary>
    public float RelSide, RelForward, RelHeight;
}

/// <summary>A landed hit (from the resolver's LastTickHits).</summary>
public sealed class HitEvent
{
    public ulong Attacker;
    public ulong Target;
    public float Damage;
    public int Tick;
}

/// <summary>A run of consecutive same-(attacker,target) hits counted as one combo.</summary>
public sealed class ComboLink
{
    public ulong Attacker;
    public ulong Target;
    public int Hits;
    public int StartTick;
    public int EndTick;
}

/// <summary>Per-tick entity position sample (provenance for the stats; the spatial maps use swings).</summary>
public sealed class TickSample
{
    public int Tick;
    public ulong EntityId;
    public float PX, PY, PZ;
}

/// <summary>Complete telemetry for one self-play match — the lossless JSON source.</summary>
public sealed class MatchRecord
{
    public int Seed;
    public int DurationTicks;
    public ulong WinnerEntityId;
    public bool SharedVictory;
    public bool TimedOut;
    public int Entity1Deaths;
    public int Entity2Deaths;
    public ushort Entity1Damage;
    public ushort Entity2Damage;
    public List<SwingRecord> Swings = new();
    public List<HitEvent> Hits = new();
    public List<ComboLink> Combos = new();
    public List<TickSample> Samples = new();
}
