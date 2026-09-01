using System;
using Xunit;
using SlopArena.Shared.AI;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tuning profile isolation and deterministic self-play contracts.
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

    /// <summary>Run under a profile and restore the base profile afterwards.</summary>
    private static void WithProfile(string name, Action body)
    {
        TuningProfiles.Apply(name);
        try { body(); }
        finally { TuningProfiles.Apply("base"); }
    }


    [Fact]
    public void TryApplyUnknown_ReturnsFalse_AndDoesNotMutate()
    {
        TuningProfiles.Apply("base");
        float before = Simulation.HitstunStunCoefficient;
        Assert.False(TuningProfiles.TryApply("nope"));
        Assert.Equal(before, Simulation.HitstunStunCoefficient);
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

}
