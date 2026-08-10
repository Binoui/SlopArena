using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Momentum-preserve tests (ADR-0015, issue #115 / #103):
/// - Air attacks no longer zero falling VY or reset AirTimeTicks (no hover).
/// - Lunge velocity coasts through the attack and SURVIVES the move end into Idle,
///   where normal friction resumes. Nothing zeroes it mid-move or at EndAbility.
/// </summary>
public class MomentumPreserveTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;
    private static readonly float MankiGroundPy = TestHelpers.MankiGroundPY;

    // ── Air attacks ride the trajectory (no hover) ──

    [Fact]
    public void AirAttack_DoesNotResetAirTime_OrCancelFall()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.VY = -5f;           // falling
        state.AirTimeTicks = 12;  // inside Kistu's 35-tick float window
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, TestHelpers.KistuDef, state);

        // Kistu AirRMB tap: no-lunge air attack (LungeForce 0), released before ChargeHoldTicks.
        // Hold 5 ticks, release — the tap slash fires from the fall.
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 5);

        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ComboStage); // attack phase reached (tap release)
        Assert.True(after.VY <= -4.5f,
            $"falling VY must NOT be cancelled or zeroed: {after.VY}");
        Assert.True(after.AirTimeTicks >= 17,
            $"AirTime must NOT reset — no hover: {after.AirTimeTicks}");
    }

    [Fact]
    public void AirAttack_ContinuesFalling_ThroughFullMove()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.VY = -5f;
        state.AirTimeTicks = 40; // past the float window — full gravity
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, TestHelpers.KistuDef, state);

        // Tap air RMB and let most of the 26-tick move play out (before landing at PY 0.75).
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 20);

        var after = sim.GetState(1);
        Assert.True(after.VY < -10f,
            $"the move must fall through, not hover: VY={after.VY}");
        Assert.True(after.PY < 3.5f,
            $"player must lose height through the aerial: PY={after.PY}");
    }

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
