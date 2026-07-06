using Xunit;
using System.Collections.Generic;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests that velocity is correctly zeroed when transitioning from Attacking → Idle.
/// Verifies both ServerAbility.EndAbility path (StageChainAbility) and data-driven expiry.
/// Also tests dash state transitions for regressions.
/// </summary>
public class AttackToIdleVelocityTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPy = TestHelpers.MankiGroundPY;

    private static readonly AttackStage Stage1 = Def.LMB!.Stages[0];
    private static readonly AttackStage Stage2 = Def.LMB!.Stages[1];
    private static readonly AttackStage Stage3 = Def.LMB!.Stages[2];

    // ── LMB1 residual velocity ──

    [Fact]
    public void Lmb1_EndsWithZeroVelocity()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Single LMB press → stage 1 runs full duration, expires to Idle
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), Stage1.DurationTicks + 5);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);

        // Both VX and VZ must be zero — EndAbility zeros horizontal velocity
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }

    [Fact]
    public void Lmb1_ZeroVelocity_BeforeAndAfterIdleTransition()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tick through LMB1 — ability ends on the tick where _stageTicks == DurationTicks
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        // Run up to DurationTicks-2 — _stageTicks reaches DurationTicks-1, still Attacking
        for (int i = 1; i < Stage1.DurationTicks - 1; i++)
            sim.Tick(new() { { 1, default } });

        var beforeLast = sim.GetState(1);
        // Still Attacking, lunge velocity still present
        Assert.Equal(ActionState.Attacking, beforeLast.State);
        float speed = System.MathF.Sqrt(beforeLast.VX * beforeLast.VX + beforeLast.VZ * beforeLast.VZ);
        Assert.True(speed > 0f, "Expected non-zero velocity during attack");

        // One more tick → EndAbility fires, state transitions to Idle
        sim.Tick(new() { { 1, default } });
        var afterExpiry = sim.GetState(1);

        Assert.Equal(ActionState.Idle, afterExpiry.State);
        Assert.Equal(0f, afterExpiry.VX);
        Assert.Equal(0f, afterExpiry.VZ);
    }

    // ── LMB full combo residual velocity ──

    [Fact]
    public void LmbFullCombo_EndsWithZeroVelocity()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Chain through all 3 stages
        ChainToStage3(sim);

        // Let stage 3 expire fully
        for (int i = 0; i < Stage3.DurationTicks + 10; i++)
            sim.Tick(new() { { 1, default } });

        var final = sim.GetState(1);
        Assert.Equal(ActionState.Idle, final.State);
        Assert.Equal(0f, final.VX);
        Assert.Equal(0f, final.VZ);
    }

    // ── Attack state persists through full duration ──

    [Fact]
    public void Lmb1_StaysAttacking_ForFullDuration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Every tick during stage 1: state must be Attacking
        for (int i = 1; i < Stage1.DurationTicks; i++)
        {
            var s = sim.GetState(1);
            Assert.Equal(ActionState.Attacking, s.State);
            Assert.Equal((byte)1, s.AttackSlot);
            sim.Tick(new() { { 1, default } });
        }

        // After stage duration + 1: should be Idle
        sim.Tick(new() { { 1, default } });
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
    }

    [Fact]
    public void Lmb1_HurtboxSpawns_WhileStateIsAttacking()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        int triggerTick = Stage1.HitboxEvents[0].TriggerTick; // 12 for Manki

        // Run up to trigger tick
        for (int i = 1; i < triggerTick; i++)
            sim.Tick(new() { { 1, default } });

        // Hitbox spawns this tick — state must still be Attacking
        sim.Tick(new() { { 1, default } });
        var atHitbox = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, atHitbox.State);
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    // ── Dash transition tests ──

    [Fact]
    public void Dash_FromIdle_TransitionsToIdle_AfterDuration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(dash: true, moveY: 1f) } });
        var t0 = sim.GetState(1);
        Assert.Equal(ActionState.Dashing, t0.State);

        ushort duration = t0.DashDurationTicks;
        Assert.True(duration > 0);

        // Tick through full dash
        for (int i = 1; i < duration; i++)
        {
            TestHelpers.TickDefault(sim, 1);
            Assert.Equal(ActionState.Dashing, sim.GetState(1).State);
        }

        // One more tick → dash expires
        TestHelpers.TickDefault(sim, 1);
        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((ushort)0, ended.DashDurationTicks);
    }

    [Fact]
    public void Dash_DoesNotPersist_PastDuration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        var duration = t0.DashDurationTicks;

        // Tick well past duration
        for (int i = 0; i < duration + 30; i++)
            TestHelpers.TickDefault(sim, 1);

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.Equal((ushort)0, s.DashDurationTicks);
        Assert.Equal((ushort)0, s.InvincibilityTicks);
    }

    // ── LMB → Dash sequence: dash after attack completes ──

    [Fact]
    public void Lmb1ThenDash_DashCompletesNormally()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // LMB1 attack, let it complete to Idle
        var afterLmb = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), Stage1.DurationTicks + 5);
        Assert.Equal(ActionState.Idle, afterLmb.State);
        Assert.Equal(0f, afterLmb.VX);
        Assert.Equal(0f, afterLmb.VZ);

        // Now dash
        sim.Tick(new() { { 1, TestHelpers.Input(dash: true, moveY: 1f) } });
        var dashStart = sim.GetState(1);
        Assert.Equal(ActionState.Dashing, dashStart.State);

        ushort duration = dashStart.DashDurationTicks;

        // Tick through dash
        for (int i = 1; i < duration; i++)
        {
            TestHelpers.TickDefault(sim, 1);
            Assert.Equal(ActionState.Dashing, sim.GetState(1).State);
        }

        // Dash should expire
        TestHelpers.TickDefault(sim, 1);
        var dashEnd = sim.GetState(1);
        Assert.Equal(ActionState.Idle, dashEnd.State);

        // Velocity should be zero after dash
        float speed = System.MathF.Sqrt(dashEnd.VX * dashEnd.VX + dashEnd.VZ * dashEnd.VZ);
        Assert.True(speed < 0.1f, $"Expected near-zero velocity after dash, got {speed:F4}");
    }

    // ── Airborne attack velocity ──

    [Fact]
    public void AirLmb_EndsWithZeroHorizontalVelocity()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy + 5f; // airborne
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var airLmbSpec = Def.AirLMB!;
        var airStage1 = airLmbSpec.Stages[0];

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), airStage1.DurationTicks + 5);

        Assert.Equal(ActionState.Idle, after.State);

        // AirLmb should also zero horizontal velocity after completion
        // (VY is preserved for gravity)
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }

    // ── Velocity dead zone during attacking (regression guard) ──

    [Fact]
    public void AttackState_DoesNotApplyDeadZone_ButFrictionDecays()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Start LMB1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Tick past lunge duration to mid-attack
        int midTick = 20;
        for (int i = 1; i < midTick; i++)
            sim.Tick(new() { { 1, default } });

        var mid = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, mid.State);

        // Velocity should be decaying but still positive (Manki LMB1 = 40 ticks, lunge=10)
        // At tick 20 (10 ticks after lunge ends): VZ ≈ 5.66 * 0.767^10 ≈ 0.39
        // Should be > dead zone (0.015) but < initial lunge (5.66)
        float speed = System.MathF.Sqrt(mid.VX * mid.VX + mid.VZ * mid.VZ);
        Assert.True(speed > 0.01f, $"Expected decaying velocity > dead zone mid-attack, got {speed:F4}");
        Assert.True(speed < 5f, $"Expected velocity below initial lunge, got {speed:F4}");
    }

    // ── Helpers ──

    private static void ChainToStage2(ServerSimulation sim)
    {
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        for (int i = 2; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });
    }

    private static void ChainToStage3(ServerSimulation sim)
    {
        ChainToStage2(sim);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        for (int i = 1; i < Stage2.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });
    }
}
