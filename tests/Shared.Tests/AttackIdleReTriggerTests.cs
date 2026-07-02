using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for two bugs in the attack → idle transition:
/// 1. Data-driven attacks re-trigger when button is held (PreTickAbilities doesn't consume input)
/// 2. AerosolFlame/Overclock use AttackElapsedTicks >= AnimLockTicks (halved duration)
/// </summary>
public class AttackIdleReTriggerTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;
    private static readonly float GroundPy = TestHelpers.MankiGroundPY;

    // ══════════════════════════════════════════════════════════════
    //  Bug 1: Data-driven held-input re-trigger
    //  Manki E (slot 4), AirLMB (slot 1 airborne), AirRMB (slot 2 airborne)
    //  have no ServerAbility — data-driven fallback.
    //  When button is HELD, PreTickAbilities doesn't consume the input,
    //  and SimulateTick re-triggers on the expiry tick.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void MankiE_HeldButton_DoesNotReTrigger()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var stageDuration = MankiDef.E!.Stages[0].DurationTicks;

        // Feed continuous E press for 3 durations + margin
        // Should see Idle between attack cycles
        bool everIdle = false;
        for (int i = 0; i < stageDuration * 3 + 10; i++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
            var s = sim.GetState(1);
            if (s.State == ActionState.Idle)
                everIdle = true;
        }

        Assert.True(everIdle,
            "State should have been Idle at some point — held input re-triggers");
    }

    [Fact]
    public void MankiAirLMB_HeldButton_DoesNotReTrigger()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var stageDuration = MankiDef.AirLMB!.Stages[0].DurationTicks;

        bool everIdle = false;
        for (int i = 0; i < stageDuration * 3 + 10; i++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
            var s = sim.GetState(1);
            if (s.State == ActionState.Idle)
                everIdle = true;
        }

        Assert.True(everIdle,
            "AirLMB held input should not re-trigger");
    }

    [Fact]
    public void MankiAirRMB_HeldButton_DoesNotReTrigger()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var stageDuration = MankiDef.AirRMB!.Stages[0].DurationTicks;

        bool everIdle = false;
        for (int i = 0; i < stageDuration * 3 + 10; i++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 2) } });
            var s = sim.GetState(1);
            if (s.State == ActionState.Idle)
                everIdle = true;
        }

        Assert.True(everIdle,
            "AirRMB held input should not re-trigger");
    }

    // ══════════════════════════════════════════════════════════════
    //  Bug 2: Duration halved in AerosolFlame and Overclock
    //  Both used `AttackElapsedTicks >= AnimLockTicks` (increasing vs
    //  decreasing counter) halving the animation duration.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void MankiRMB_Normal_DurationMatchesSpec()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        ushort expectedDuration = (ushort)MankiDef.RMB!.Params!["normal_duration"]; // 58

        // Tick 0: activate RMB
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 2) } });

        // Feed default input until Idle
        int idleTick = -1;
        for (int i = 1; i <= expectedDuration + 20; i++)
        {
            sim.Tick(new() { { 1, default } });
            var s = sim.GetState(1);
            if (s.State == ActionState.Idle)
            {
                idleTick = i;
                break;
            }
        }

        // Should idle near expectedDuration (allow ±2 for tick boundary)
        Assert.True(idleTick >= expectedDuration - 2,
            $"Normal RMB should last ~{expectedDuration} ticks, but Idle at tick {idleTick}");
    }

    [Fact]
    public void MankiOverclock_InjectionDurationMatchesSpec()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        const int expectedTicks = 30; // injection animation lock

        // Tick 0: activate F
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });

        int idleTick = -1;
        for (int i = 1; i <= expectedTicks + 20; i++)
        {
            sim.Tick(new() { { 1, default } });
            var s = sim.GetState(1);
            if (s.State == ActionState.Idle)
            {
                idleTick = i;
                break;
            }
        }

        Assert.True(idleTick >= expectedTicks - 2,
            $"Overclock injection should last ~{expectedTicks} ticks, but Idle at tick {idleTick}");
    }
}
