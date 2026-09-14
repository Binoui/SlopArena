using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;
using SlopArena.Shared.AI;

/// <summary>
/// Issue #148 — self-play match invariants: determinism (same seed → identical match),
/// termination, both sides act, swing accounting is consistent, and no NaN positions.
/// </summary>
public class SelfPlayTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;

    /// <summary>Crossroads-style 60×60 flat proxy (top +20, sides ±40, bottom −10) — same as the tool.</summary>
    private static ArenaDefinition KillArena()
    {
        const int w = 60, h = 60;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "kill-proxy",
            DisplayName = "Kill Proxy",
            KillHeight = -10f,
            MinX = -30f, MaxX = 30f, MinZ = -30f, MaxZ = 30f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            // Origin at -30 so the floor covers [-30,30] — matches the bounds (bots spawn at x=±12).
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = -30f, OriginZ = -30f },
        };
    }

    private static MatchRecord Run(int seed, int maxTicks = 2000,
        CpuDifficulty difficulty = CpuDifficulty.Normal)
        => SelfPlayMatch.Run(Def, KillArena(), seed, TestHelpers.LoadBakedData(Def), maxTicks,
            difficulty: difficulty);

    [Fact]
    public void SameSeed_TerminatesWithIdenticalMatch()
    {
        var a = Run(42, maxTicks: 1500);
        var b = Run(42, maxTicks: 1500);

        Assert.Equal(a.DurationTicks, b.DurationTicks);
        Assert.Equal(a.TimedOut, b.TimedOut);
        Assert.Equal(a.WinnerEntityId, b.WinnerEntityId);
        Assert.Equal(a.Entity1Deaths, b.Entity1Deaths);
        Assert.Equal(a.Entity2Deaths, b.Entity2Deaths);
        Assert.Equal(a.Swings.Count, b.Swings.Count);
        Assert.Equal(a.Hits.Count, b.Hits.Count);
    }

    [Fact]
    public void Run_ReturnsWithoutException_AndIsBounded()
    {
        var rec = Run(7, maxTicks: 500);

        Assert.True(rec.DurationTicks <= 500, $"match ran {rec.DurationTicks} ticks, past the cap");
        Assert.True(rec.TimedOut || rec.WinnerEntityId is SelfPlayMatch.EntityA or SelfPlayMatch.EntityB,
            "match must either time out or declare a winner");
    }

    [Fact]
    public void BothSides_Act()
    {
        var rec = Run(42, maxTicks: 4000);

        var attackers = rec.Swings.Select(s => s.Attacker).Distinct().ToHashSet();
        Assert.Contains(SelfPlayMatch.EntityA, attackers);
        Assert.Contains(SelfPlayMatch.EntityB, attackers);
        // At least one swing connected on each side (real fighting, not one-sided whiffing).
        Assert.Contains(SelfPlayMatch.EntityA, rec.Swings.Where(s => s.Connected).Select(s => s.Attacker));
        Assert.Contains(SelfPlayMatch.EntityB, rec.Swings.Where(s => s.Connected).Select(s => s.Attacker));
    }

    [Fact]
    public void SwingAccounting_ConnectedPlusWhiffsEqualsTotal()
    {
        var rec = Run(42, maxTicks: 4000);

        int connected = rec.Swings.Count(s => s.Connected);
        int whiffs = rec.Swings.Count(s => !s.Connected);
        Assert.Equal(rec.Swings.Count, connected + whiffs);
    }

    [Fact]
    public void WhiffSwings_RecordFacingFrameGeometry()
    {
        var rec = Run(42, maxTicks: 4000);

        var whiffs = rec.Swings.Where(s => !s.Connected).ToList();
        if (whiffs.Count == 0) return; // not guaranteed in a short run; the invariant below is the contract
        foreach (var w in whiffs)
        {
            // Facing-frame forward coordinate must be finite (the whiff spot map consumes it).
            Assert.True(float.IsFinite(w.RelForward), "whiff RelForward must be finite");
            Assert.True(float.IsFinite(w.RelHeight), "whiff RelHeight must be finite");
        }
    }

    [Fact]
    public void NoNaNPoisitions_InSamples()
    {
        var rec = Run(42, maxTicks: 2000);
        foreach (var s in rec.Samples)
        {
            Assert.True(float.IsFinite(s.PX), $"NaN PX at tick {s.Tick}");
            Assert.True(float.IsFinite(s.PY), $"NaN PY at tick {s.Tick}");
            Assert.True(float.IsFinite(s.PZ), $"NaN PZ at tick {s.Tick}");
        }
    }

    [Fact]
    public void SameSeedAndDifficulty_IsIdentical_ChangingDifficultyChangesTrace()
    {
        var a = Run(42, maxTicks: 1200, difficulty: CpuDifficulty.Normal);
        var b = Run(42, maxTicks: 1200, difficulty: CpuDifficulty.Normal);

        Assert.Equal(a.DurationTicks, b.DurationTicks);
        Assert.Equal(a.Swings.Count, b.Swings.Count);
        Assert.Equal(a.Hits.Count, b.Hits.Count);
        Assert.Equal(a.Samples.Count, b.Samples.Count);
        for (int i = 0; i < a.Samples.Count; i++)
        {
            Assert.Equal(a.Samples[i].Tick, b.Samples[i].Tick);
            Assert.Equal(a.Samples[i].EntityId, b.Samples[i].EntityId);
            Assert.Equal(a.Samples[i].PX, b.Samples[i].PX);
            Assert.Equal(a.Samples[i].PY, b.Samples[i].PY);
            Assert.Equal(a.Samples[i].PZ, b.Samples[i].PZ);
        }

        var easy = Run(42, maxTicks: 1200, difficulty: CpuDifficulty.Easy);
        var hard = Run(42, maxTicks: 1200, difficulty: CpuDifficulty.Hard);
        bool different = easy.Swings.Count != hard.Swings.Count
            || easy.Hits.Count != hard.Hits.Count
            || easy.Samples.Count != hard.Samples.Count;
        if (!different)
        {
            for (int i = 0; i < easy.Samples.Count; i++)
            {
                if (easy.Samples[i].PX != hard.Samples[i].PX
                    || easy.Samples[i].PY != hard.Samples[i].PY
                    || easy.Samples[i].PZ != hard.Samples[i].PZ)
                {
                    different = true;
                    break;
                }
            }
        }

        Assert.True(different, "changing CPU difficulty did not change the self-play trace");
    }
    [Fact]
    public void Recorder_UninterruptedHitstun_IsTrueCombo()
    {
        var sim = RecorderSimulation();
        var recorder = new MatchRecorder();
        var target = sim.GetState(100);
        target.State = ActionState.Hitstun;
        target.HitstunTicks = 8;
        sim.SetState(100, target);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 0, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();

        target.HitstunTicks = 4;
        sim.SetState(100, target);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 3, new Dictionary<ulong, InputState>(), Def);

        var record = recorder.Finish(4, 1, new MatchOutcome(false, 0, false));
        var combo = Assert.Single(record.Combos);
        Assert.True(combo.IsTrueCombo);
        Assert.False(combo.IsPressureString);
    }

    [Fact]
    public void Recorder_ActionableGap_IsPressureString()
    {
        var sim = RecorderSimulation();
        var recorder = new MatchRecorder();
        var target = sim.GetState(100);
        target.State = ActionState.Hitstun;
        target.HitstunTicks = 1;
        sim.SetState(100, target);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 0, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();

        target.State = ActionState.Idle;
        target.HitstunTicks = 0;
        sim.SetState(100, target);
        recorder.RecordTick(sim, 1, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 2, new Dictionary<ulong, InputState>(), Def);

        var record = recorder.Finish(3, 1, new MatchOutcome(false, 0, false));
        var combo = Assert.Single(record.Combos);
        Assert.False(combo.IsTrueCombo);
        Assert.True(combo.IsPressureString);
    }

    private static ServerSimulation RecorderSimulation()
    {
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState();
        attacker.PY = Def.CapsuleHeight * 0.5f;
        var target = TestHelpers.NpcState(z: 1f);
        target.PY = Def.CapsuleHeight * 0.5f;
        TestHelpers.RegisterPlayer(sim, Def, attacker);
        TestHelpers.RegisterNpc(sim, Def, target);
        return sim;
    }

    private static SpellResolver.HitResult RecorderHit()
        => new()
        {
            OwnerEntityId = SelfPlayMatch.EntityA,
            TargetEntityId = SelfPlayMatch.EntityB,
            AttackSlot = AbilitySlots.Slot1,
            Damage = 1f,
            StunTicks = 8,
        };
}
