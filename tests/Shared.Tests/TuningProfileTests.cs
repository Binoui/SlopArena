using System;
using System.Linq;
using Xunit;
using SlopArena.Shared.AI;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Issue #149 — tuning profiles: applying a named profile sets the sim's KB knobs exactly;
/// and seed-reuse determinism: the same seed under the same profile reproduces the match
/// bit-for-bit (so an A/B telemetry diff isolates the tuning change), while a different
/// profile moves the telemetry.
/// </summary>
public class TuningProfileTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;

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
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = -30f, OriginZ = -30f },
        };
    }

    private static MatchRecord Run(int seed, int maxTicks = 2000)
        => SelfPlayMatch.Run(Def, KillArena(), seed, TestHelpers.LoadBakedData(Def), maxTicks);

    /// <summary>Run under a profile and always restore the shipped tuning — the sim's KB knobs
    /// are process-global statics shared with the whole suite, so a test must never leave them
    /// on a lab profile (every other test asserts against the shipped values).</summary>
    private static void WithProfile(string name, Action body)
    {
        TuningProfiles.Apply(name);
        try { body(); }
        finally { TuningProfiles.Apply("base"); }
    }

    [Fact]
    public void ApplyBase_SetsShippedDefaults()
    {
        TuningProfiles.Apply("base");
        Assert.Equal(0.7f, Simulation.HitstunStunCoefficient);
        Assert.Equal(0.11f, Simulation.KbScaleFactor);
        Assert.Equal(20f, Simulation.HitstunMagBonus);
    }

    [Fact]
    public void ApplyKnownProfile_SetsExpectedKnobs()
    {
        WithProfile("stun16kv11", () =>
        {
            Assert.Equal(0.8f, Simulation.HitstunStunCoefficient);
            Assert.Equal(0.11f, Simulation.KbScaleFactor);
            Assert.Equal(0f, Simulation.HitstunMagBonus);
        });
        WithProfile("old", () =>
        {
            Assert.Equal(0.5f, Simulation.HitstunStunCoefficient);
            Assert.Equal(0.14f, Simulation.KbScaleFactor);
            Assert.Equal(0f, Simulation.HitstunMagBonus);
        });
        WithProfile("floor30", () =>
        {
            Assert.Equal(0.5f, Simulation.HitstunStunCoefficient);
            Assert.Equal(0.14f, Simulation.KbScaleFactor);
            Assert.Equal(30f, Simulation.HitstunMagBonus);
        });
        Assert.Equal(0.7f, Simulation.HitstunStunCoefficient); // restored
    }

    [Fact]
    public void TryApplyUnknown_ReturnsFalse_AndDoesNotMutate()
    {
        TuningProfiles.Apply("base");
        Assert.False(TuningProfiles.TryApply("nope"));
        Assert.Equal(0.7f, Simulation.HitstunStunCoefficient); // untouched
        Assert.Throws<ArgumentException>(() => TuningProfiles.Apply("nope"));
    }

    [Fact]
    public void SameSeed_SameProfile_ReproducesMatchBitForBit()
    {
        WithProfile("base", () =>
        {
            var a = Run(20260817, maxTicks: 1500);
            var b = Run(20260817, maxTicks: 1500);

            Assert.Equal(a.DurationTicks, b.DurationTicks);
            Assert.Equal(a.WinnerEntityId, b.WinnerEntityId);
            Assert.Equal(a.Entity1Deaths, b.Entity1Deaths);
            Assert.Equal(a.Entity2Deaths, b.Entity2Deaths);
            Assert.Equal(a.Swings.Count, b.Swings.Count);
            Assert.Equal(a.Hits.Count, b.Hits.Count);
            Assert.Equal(a.Combos.Select(c => c.Hits).ToArray(), b.Combos.Select(c => c.Hits).ToArray());
        });
    }

    [Fact]
    public void SameSeed_DifferentProfile_MovesTelemetry()
    {
        // The A/B mechanism: fixed seed on both sides isolates the tuning change. The sim is
        // fully deterministic, so this assertion is stable — it either always passes or the
        // stun ratio change is invisible to the bots (a real finding for the tuning loop).
        WithProfile("base", () =>
        {
            var baseRecords = Enumerable.Range(0, 3).Select(i => Run(9000 + i, maxTicks: 1500)).ToList();

            int BaseSwings() => baseRecords.Sum(r => r.Swings.Count);
            int BaseHits() => baseRecords.Sum(r => r.Swings.Count(s => s.Connected));
            int BaseDamage() => baseRecords.Sum(r => (int)Math.Round(r.Hits.Sum(h => h.Damage)));

            WithProfile("stun16kv11", () =>
            {
                var candRecords = Enumerable.Range(0, 3).Select(i => Run(9000 + i, maxTicks: 1500)).ToList();
                int CandSwings() => candRecords.Sum(r => r.Swings.Count);
                int CandHits() => candRecords.Sum(r => r.Swings.Count(s => s.Connected));
                int CandDamage() => candRecords.Sum(r => (int)Math.Round(r.Hits.Sum(h => h.Damage)));

                Assert.True(
                    BaseSwings() != CandSwings() || BaseHits() != CandHits() || BaseDamage() != CandDamage(),
                    "same seed + different hitstun profile must change at least one telemetry stat " +
                    $"(base: {BaseSwings()} swings/{BaseHits()} hits/{BaseDamage()} dmg; cand: {CandSwings()}/{CandHits()}/{CandDamage()})");
            });
        });
    }
}
