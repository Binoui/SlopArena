using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.MoveDataReport;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ContactCoverageTests
{
    private static readonly ArenaDefinition Arena = Program.NoRespawn(Program.BuildArena());

    [Theory]
    [InlineData(CharacterClass.FightGuy, 5)]
    [InlineData(CharacterClass.Kistu, 12)]
    public void GroundOne_ReportsRealContactAndDistantMiss(CharacterClass character, int expectedTick)
    {
        var entry = BuiltInContentResolver.Resolve(character);
        Assert.True(CanonicalSlotProjection.TryGet("ground.1", out var slot));
        var context = ContactCoverageReport.PrepareContext(entry, BuiltInContentResolver.Resolve(CharacterClass.FightGuy), 1f, Arena);

        var hit = ContactCoverageReport.RunSample(context, slot, new("passive-low", 0f, 0f, 0));
        var miss = ContactCoverageReport.RunSample(context, slot, new("passive-low", 4f, 6f, 0));
        var oracleHit = RunOracle(entry, BuiltInContentResolver.Resolve(CharacterClass.FightGuy), 0f, 0f, slot);
        var oracleMiss = RunOracle(entry, BuiltInContentResolver.Resolve(CharacterClass.FightGuy), 4f, 6f, slot);

        Assert.Equal("hit", hit.Outcome);
        Assert.NotNull(hit.FirstContact);
        Assert.Equal(expectedTick, hit.FirstContact!.Tick);
        Assert.Equal(oracleHit.Tick, hit.FirstContact.Tick);
        Assert.Equal(oracleHit.Damage, hit.FirstContact.Damage);
        Assert.Equal("miss", miss.Outcome);
        Assert.Equal(-1, oracleMiss.Tick);
    }

    [Fact]
    public void AirOne_DoesNotSubstituteGroundMoveAfterLanding()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        Assert.True(CanonicalSlotProjection.TryGet("air.1", out var slot));
        var context = ContactCoverageReport.PrepareContext(entry, BuiltInContentResolver.Resolve(CharacterClass.FightGuy), 1f, Arena);
        var accepted = ContactCoverageReport.RunSample(context, slot, new("passive-low", 0f, 0f, 0));
        var delayed = ContactCoverageReport.RunSample(context, slot, new("attacker-drift", 4f, 6f, 60));

        Assert.Equal("hit", accepted.Outcome);
        Assert.True(accepted.AirborneAtStart);
        Assert.Equal("unavailable", delayed.Outcome);
        Assert.Equal("wrong-locomotion-state", delayed.Reason);
    }

    [Fact]
    public void CandidateOverride_IsolatedAndScaleOneIsExactControl()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        Assert.True(CanonicalSlotProjection.TryGet("air.1", out var slot));
        var baseline = ContactCoverageReport.PrepareContext(entry, entry, 1f, Arena);
        var candidate = ContactCoverageReport.PrepareContext(entry, entry, .8f, Arena);
        var baselineBefore = ContactCoverageReport.RunSample(baseline, slot, new("attacker-drift", 4f, 6f, 6));
        var candidateResult = ContactCoverageReport.RunSample(candidate, slot, new("attacker-drift", 4f, 6f, 6));
        var baselineAfter = ContactCoverageReport.RunSample(baseline, slot, new("attacker-drift", 4f, 6f, 6));
        var scaleOne = ContactCoverageReport.PrepareContext(entry, entry, 1f, Arena);
        var control = ContactCoverageReport.RunSample(scaleOne, slot, new("attacker-drift", 4f, 6f, 6));

        Assert.Equal(baselineBefore.Outcome, baselineAfter.Outcome);
        Assert.Equal(baselineBefore.PrePressAttacker!.PositionZ, baselineAfter.PrePressAttacker!.PositionZ);
        Assert.Equal(baselineBefore.PrePressAttacker.VelocityZ, baselineAfter.PrePressAttacker.VelocityZ);
        Assert.Equal(baselineBefore.PrePressAttacker.PositionZ, control.PrePressAttacker!.PositionZ);
        Assert.True(candidateResult.PrePressAttacker!.PositionZ < baselineBefore.PrePressAttacker.PositionZ);
        Assert.True(candidateResult.PrePressAttacker.VelocityZ < baselineBefore.PrePressAttacker.VelocityZ);
        Assert.Equal(baselineBefore.PrePressAttacker.PositionY, candidateResult.PrePressAttacker.PositionY);
        Assert.Equal(baselineBefore.PrePressAttacker.VelocityY, candidateResult.PrePressAttacker.VelocityY);
        Assert.False(candidateResult.PrePressAttacker.IsGrounded);
    }

    [Fact]
    public void HitWindows_DoNotBridgeSeparatedRunsOrUnavailableSamples()
    {
        var samples = Enumerable.Range(0, 8).Select(t => new CoverageSampleResultForTest(t, t is 0 or 1 or 4 or 5 ? "hit" : t == 2 ? "unavailable" : "miss")).ToArray();
        var intervals = ContactCoverageReport.FindHitWindows(samples.Select(x => x.Result).ToArray());
        Assert.Equal(2, intervals.Length);
        Assert.Equal((0, 1), (intervals[0].StartPressTick, intervals[0].EndPressTick));
        Assert.Equal((4, 5), (intervals[1].StartPressTick, intervals[1].EndPressTick));
        Assert.Equal(new[] { 2, 2 }, intervals.Select(x => x.SuccessfulPressTicks).ToArray());
    }

    private static (int Tick, float Damage) RunOracle(MatchContentEntry attacker, MatchContentEntry victim, float x, float z, SlotAddress slot)
    {
        var sim = new ServerSimulation(Arena);
        var a = new CharacterState { PX = 100f, PY = attacker.Definition.CapsuleHeight * .5f, PZ = 100f, IsGrounded = true, State = ActionState.Idle, JumpsLeft = attacker.Definition.Movement.MaxJumps, AirDodgesLeft = 1, FacingYaw = 0f };
        var v = new CharacterState { PX = 100f + x, PY = victim.Definition.CapsuleHeight * .5f, PZ = 100f + z, IsGrounded = true, State = ActionState.Idle, JumpsLeft = victim.Definition.Movement.MaxJumps, AirDodgesLeft = 1, FacingYaw = MathF.PI };
        sim.RegisterEntity(1, attacker.Definition, a, attacker.BakedAnimation);
        sim.RegisterEntity(100, victim.Definition, v, victim.BakedAnimation);
        var input = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = Program.SlotByte(int.Parse(slot.InputLabel)) }, [100] = default };
        for (int tick = 0; tick < 100; tick++)
        {
            if (tick > 0) input[1] = default;
            sim.Tick(input);
            var hit = sim.LastTickHits.SingleOrDefault(x => x.OwnerEntityId == 1 && x.TargetEntityId == 100 && x.Damage > 0f);
            if (hit.Damage > 0f) return (tick, hit.Damage);
        }
        return (-1, 0f);
    }

    private sealed class CoverageSampleResultForTest
    {
        internal ContactCoverageReport.CoverageSampleResult Result { get; }
        internal CoverageSampleResultForTest(int tick, string outcome)
        {
            Result = new ContactCoverageReport.CoverageSampleResult
            {
                ScenarioId = "test",
                Profile = "baseline",
                InitialRelativeX = 0,
                InitialRelativeZ = 0,
                PressTick = tick,
                Outcome = outcome,
            };
        }
    }
}
