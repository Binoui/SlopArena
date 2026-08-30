using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for two bugs in the attack → idle transition:
/// 1. Held-input re-trigger (PreTickAbilities consumes input, but held buttons need guard)
/// 2. AerosolFlame/Overclock use AttackElapsedTicks >= AnimLockTicks (halved duration)
/// </summary>
public class AttackIdleReTriggerTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;
    private static readonly float GroundPy = TestHelpers.MankiGroundPY;

    // ══════════════════════════════════════════════════════════════
    //  Bug 1: Held-input re-trigger
    //  All abilities are ServerAbility. PreTickAbilities activates them on
    //  the first tick with ActiveSlot. When the button is HELD, the guard
    //  `if (_activeAbilities.ContainsKey(id)) continue;` prevents re-trigger.
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


    // ══════════════════════════════════════════════════════════════
    //  Bug 2: Duration halved in AerosolFlame and Overclock
    //  Both used `AttackElapsedTicks >= AnimLockTicks` (increasing vs
    //  decreasing counter) halving the animation duration.
    // ══════════════════════════════════════════════════════════════

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
