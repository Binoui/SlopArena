using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Momentum-preserve tests (ADR-0015, issue #115 / #103):
/// - Lunge velocity coasts through the attack and SURVIVES the move end into Idle,
///   where normal friction resumes. Nothing zeroes it mid-move or at EndAbility.
/// (The air-charge momentum-preserve cases rode the now-removed AirRMB.)
/// </summary>
public class MomentumPreserveTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;
    private static readonly float MankiGroundPy = TestHelpers.MankiGroundPY;

    // ── Lunge momentum persists through the move and into Idle ──

    [Fact]
    public void LungeVelocity_CoastsThroughAttack()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        const int lungeTicks = 10;  // lunge_duration param
        const int extraTicks = 20;  // still inside the 40-tick move
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), lungeTicks + extraTicks);

        // No ground friction while Attacking, no post-lunge zero: velocity coasts.
        Assert.True(after.VZ > 6f, $"lunge must coast through the attack: VZ={after.VZ}");
    }

    [Fact]
    public void LungeVelocity_SurvivesMoveEnd_IntoIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // Run the full move (EndAbility fires) plus 2 idle ticks of friction.
        int duration = MankiDef.LMB!.Stages[0].DurationTicks;
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), duration + 2);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.True(after.VZ > 3f,
            $"EndAbility must NOT zero velocity — drift carries into Idle: VZ={after.VZ}");
    }
}
