using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for state machine transitions (ActionState changes) and movement physics.
/// </summary>
public class PhysicsTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly MovementStats Move = Def.Movement;
    private static readonly float GroundPx = TestHelpers.MankiGroundPY;
    private static readonly float GravPerTick = Move.Gravity * Simulation.TickDt;

    // ── Jump ──

    [Fact]
    public void GroundJump_EnterJumpSquatThenJump()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tick with jump input
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(ActionState.JumpSquat, t0.State);
        Assert.Equal(Move.JumpSquatTicks, (int)t0.StateTicks);
        Assert.Equal(1u, t0.JumpsLeft);

        // The rest of the squat ticks
        for (int i = 1; i < Move.JumpSquatTicks; i++)
        {
            var s = TestHelpers.TickDefault(sim, 1);
            Assert.Equal(ActionState.JumpSquat, s.State);
        }

        // Squat expires → jump fires, then gravity applies same tick
        var tJump = TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.Idle, tJump.State);
        Assert.False(tJump.IsGrounded);
        TestHelpers.AssertNear(Move.JumpForce - GravPerTick, tJump.VY, 0.01f);
    }

    [Fact]
    public void DoubleJump_ConsumesJumpsLeft()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Squat to get airborne
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        for (int i = 0; i < Move.JumpSquatTicks; i++)
            TestHelpers.TickDefault(sim, 1);
        var afterJump = sim.GetState(1);
        Assert.False(afterJump.IsGrounded);
        Assert.Equal(1u, afterJump.JumpsLeft);

        // Double jump in air
        var doubled = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0u, doubled.JumpsLeft);
        TestHelpers.AssertNear(Move.JumpForce - GravPerTick, doubled.VY, 0.01f);
    }

    [Fact]
    public void JumpBlocked_NoJumpsLeft()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.VY = 5f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0u, after.JumpsLeft);
        // VY decays by gravity (no new jump force)
        TestHelpers.AssertNear(5f - GravPerTick, after.VY, 0.01f);
    }

    [Fact]
    public void JumpBlocked_DuringHitstun()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.HitstunTicks = 5;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0f, after.VY);
        Assert.Equal(4, (int)after.HitstunTicks);
    }

    // ── Dash ──

    [Fact]
    public void GroundDash_TransitionsToIdle()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true), 1);
        Assert.Equal(ActionState.Dashing, t0.State);

        // Tick enough frames for the dash to complete and settle
        for (int i = 0; i < 25; i++)
            TestHelpers.TickDefault(sim, 1);

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal(0, (int)after.DashDurationTicks);
    }

    [Fact]
    public void DashCooldown_BlocksRedash()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Start dash
        TestHelpers.TickN(sim, TestHelpers.Input(dash: true), 1);
        for (int i = 0; i < 25; i++)
            TestHelpers.TickDefault(sim, 1);

        // Re-dash attempt blocked by cooldown
        var blocked = TestHelpers.TickN(sim, TestHelpers.Input(dash: true), 1);
        Assert.Equal(ActionState.Idle, blocked.State);
        Assert.True(blocked.DashCooldownTicks > 0,
            $"Expected DashCooldownTicks>0 but got {blocked.DashCooldownTicks}");

        // Wait for cooldown
        for (int i = 0; i < Move.DashCooldownTicks + 5; i++)
            TestHelpers.TickDefault(sim, 1);

        // Re-dash works
        var reDash = TestHelpers.TickN(sim, TestHelpers.Input(dash: true), 1);
        Assert.Equal(ActionState.Dashing, reDash.State);
    }

    // ── Landing ──

    [Fact]
    public void Land_ResetsJumpsAndAirDodges()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.AirDodgesLeft = 0;
        state.VY = -35f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        for (int i = 0; i < 120; i++)
            TestHelpers.TickDefault(sim, 1);

        var landed = sim.GetState(1);
        Assert.True(landed.IsGrounded);
        Assert.Equal(2u, landed.JumpsLeft);
        Assert.Equal(1u, landed.AirDodgesLeft);
    }

    // ── Walk / Sprint / Friction ──

    [Fact]
    public void WalkForward_MovesPosition()
    {
        var arena = TestHelpers.TestArena();
        var state = TestHelpers.PlayerState();
        var sim = TestHelpers.MakeSim(arena);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Feed move input every tick for 60 ticks
        CharacterState final = default;
        for (int i = 0; i < 60; i++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
            final = sim.GetState(1);
        }

        // VZ = SprintSpeed (12) during sprint portion, WalkSpeed (9) during non-sprint
        // After 60 ticks total: at least WalkSpeed * 1s ~ 9m, at most SprintSpeed * 1s ~ 12m
        TestHelpers.AssertNear(10.5f, final.PZ, 2.0f);
    }

    [Fact]
    public void Sprint_MovesFasterThanWalk()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });

        var s = sim.GetState(1);
        Assert.True(s.IsSprinting);
        Assert.True(s.VZ > Move.WalkSpeed,
            $"Expected VZ > {Move.WalkSpeed} but got {s.VZ:F2}");
    }

    [Fact]
    public void Friction_DecaysVelocityOnRelease()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Walk for 1 tick (VZ=WalkSpeed), then coast
        TestHelpers.TickN(sim, TestHelpers.Input(moveY: 1f), 1);
        var afterWalk = sim.GetState(1);
        Assert.True(afterWalk.VZ > 0f);

        float prevVz = afterWalk.VZ;
        for (int i = 0; i < 10; i++)
        {
            var s = TestHelpers.TickDefault(sim, 1);
            Assert.True(s.VZ < prevVz || Math.Abs(s.VZ) < 0.001f,
                $"Tick {i}: VZ should decay from {prevVz:F4} but got {s.VZ:F4}");
            prevVz = s.VZ;
        }
    }

    // ── Data-driven attack expiry ──

    [Fact]
    public void DataDrivenAttack_EndsAfterDurationTicks()
    {
        // Manki E (slot 4) has no ServerAbility → data-driven: DurationTicks=20
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, s.State);

        // Tick past duration
        // After AttackElapsedTicks >= 20, data-driven expiry fires
        for (int i = 0; i < 25; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((byte)0, ended.AttackSlot);
    }

    // ── Hitstun ──

    [Fact]
    public void Hitstun_AppliesKnockbackThenExpires()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.KVX = 10f;
        state.KVY = 5f;
        state.HitstunTicks = 12;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t1 = TestHelpers.TickDefault(sim, 1);
        // ProcessKnockback applies KV→V, decays KV per tick
        Assert.True(t1.KVX != 0 || t1.KVY != 0);
        Assert.Equal(11, (int)t1.HitstunTicks);

        // Tick through hitstun
        for (int i = 0; i < 20; i++)
            TestHelpers.TickDefault(sim, 1);

        var after = sim.GetState(1);
        Assert.Equal(0, (int)after.HitstunTicks);
        Assert.Equal(ActionState.Idle, after.State);
    }
}
