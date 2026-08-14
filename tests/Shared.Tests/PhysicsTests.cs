using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for state machine transitions (ActionState changes) and movement physics.
/// </summary>
public class PhysicsTests
{
    // Classic definition with FloatWindowTicks=0 for backward-compatible gravity tests
    private static readonly CharacterDefinition Def = CreateClassicDef();
    private static readonly MovementStats Move = Def.Movement;
    private static readonly float GroundPx = TestHelpers.MankiGroundPY;
    private static readonly float GravPerTick = Move.Gravity * Simulation.TickDt;

    private static CharacterDefinition CreateClassicDef()
    {
        var mov = TestHelpers.MankiDef.Movement;
        mov.FloatWindowTicks = 0;
        return TestHelpers.CloneDef(TestHelpers.MankiDef, mov);
    }
    // ── Jump ──

    [Fact]
    public void GroundJump_EnterJumpSquatThenJump()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tick with jump input (held — a held jump is the full jump; taps short-hop, issue #116)
        var t0 = TestHelpers.TickHold(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        Assert.Equal(ActionState.JumpSquat, t0.State);
        Assert.Equal(Move.JumpSquatTicks, (int)t0.StateTicks);
        Assert.Equal(1u, t0.JumpsLeft);

        // The rest of the squat ticks (still holding)
        for (int i = 1; i < Move.JumpSquatTicks; i++)
        {
            var s = TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), 1);
            Assert.Equal(ActionState.JumpSquat, s.State);
        }

        // Squat expires → jump fires, then gravity applies same tick
        var tJump = TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), 1);
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

        // Squat to get airborne (held = full ground jump; the jump EDGE is 1 tick, the
        // hold continues — holding the edge would re-trigger as a double jump on fire).
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), Move.JumpSquatTicks);
        var afterJump = sim.GetState(1);
        Assert.False(afterJump.IsGrounded);
        Assert.Equal(1u, afterJump.JumpsLeft);

        // Double jump in air (air jumps are always full — no short hop in the air, issue #116)
        var doubled = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0u, doubled.JumpsLeft);
        TestHelpers.AssertNear(Move.JumpForce * Move.AirJumpVMultiplier - GravPerTick, doubled.VY, 0.01f);
    }

    [Fact]
    public void GroundJump_PreservesHorizontalMomentum()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.VX = Move.RunSpeed; // running at run speed
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Enter JumpSquat — VX preserved (not zeroed)
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        Assert.Equal(ActionState.JumpSquat, t0.State);
        Assert.Equal(Move.RunSpeed, t0.VX);

        // Remainder of JumpSquat — VX stays at run speed (no friction during squat)
        for (int i = 1; i < Move.JumpSquatTicks; i++)
        {
            var s = TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), 1);
            Assert.Equal(ActionState.JumpSquat, s.State);
            Assert.Equal(Move.RunSpeed, s.VX);
        }

        // Squat expires → full hop airborne, momentum preserved (air friction reduces slightly)
        var tJump = TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), 1);
        Assert.False(tJump.IsGrounded);
        Assert.True(tJump.VX > 0f, $"Expected VX > 0 after jump, got {tJump.VX:F3}");
    }

    [Fact]
    public void JumpBlocked_NoJumpsLeft()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f; // well above ground snap window (0.75), so gravity is the only VY modifier
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

    // ── Run / Friction ──

    [Fact]
    public void RunForward_MovesPosition()
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

        // Single Run tier: VZ accelerates to RunSpeed and holds there.
        Assert.Equal(ActionState.Run, final.State);
        TestHelpers.AssertNear(Move.RunSpeed, final.VZ, 0.1f);
        Assert.True(final.PZ > 5f, $"Expected meaningful forward progress, got PZ={final.PZ:F2}");
    }

    [Fact]
    public void Run_AcceleratesToRunSpeed()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Run, s.State);
        TestHelpers.AssertNear(Move.RunSpeed, s.VZ, 0.1f);
    }

    [Fact]
    public void RushReversal_FlipsInstantlyWithinRushWindow()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Move +Z for 5 ticks — still inside the Rush window (RushTicks = 10). Velocity
        // is already at cruise (instant kick-off), not ramping.
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        var before = sim.GetState(1);
        Assert.Equal(ActionState.Run, before.State);
        TestHelpers.AssertNear(Move.RunSpeed, before.VZ, 0.1f);

        // Reverse within the Rush window: instant full-speed flip (no friction).
        sim.Tick(new() { { 1, TestHelpers.Input(moveY: -1f) } });
        var after = sim.GetState(1);
        TestHelpers.AssertNear(-Move.RunSpeed, after.VZ, 0.1f);
    }

    [Fact]
    public void RunReversal_AfterRushWindowUsesFrictionNotInstantFlip()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold +Z past the Rush window (RushTicks = 10) into Run proper.
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        var before = sim.GetState(1);
        TestHelpers.AssertNear(Move.RunSpeed, before.VZ, 0.1f);

        // Reverse at Run: a Turnaround (friction), NOT an instant flip — VZ stays
        // positive and merely decays.
        sim.Tick(new() { { 1, TestHelpers.Input(moveY: -1f) } });
        var after = sim.GetState(1);
        Assert.True(after.VZ > 0f && after.VZ < before.VZ,
            $"Run reversal must friction (stay +Z, decay), got VZ={after.VZ:F3}");
    }

    [Fact]
    public void Turnaround_DeceleratesHardShortSkid()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Run +Z past the Rush window into Run proper.
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        TestHelpers.AssertNear(Move.RunSpeed, sim.GetState(1).VZ, 0.1f);

        // Reverse: the Turnaround pivot decelerates hard (~TurnaroundFriction), stopping
        // in ~10-12 ticks — a short skid, not the old ~90-tick coast (ice slide).
        for (int i = 0; i < 12; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: -1f) } });
        var s = sim.GetState(1);
        Assert.True(MathF.Abs(s.VZ) < 1.0f,
            $"Turnaround should nearly stop within 12 ticks, got VZ={s.VZ:F3}");
    }

    [Fact]
    public void RushRelease_StopsInstantly()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // A short press opens the Rush window; releasing inside it stops dead (no drift).
        TestHelpers.TickHold(sim, TestHelpers.Input(moveY: 1f), 3);
        Assert.True(sim.GetState(1).VZ > 0f, "should be moving");

        sim.Tick(new() { { 1, default(InputState) } });
        Assert.Equal(0f, sim.GetState(1).VZ);
        Assert.Equal(0f, sim.GetState(1).VX);
    }

    [Fact]
    public void RunRelease_BrakesToStop()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold +Z past the Rush window into Run, then release: brake to a stop (not instant).
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        var before = sim.GetState(1);
        TestHelpers.AssertNear(Move.RunSpeed, before.VZ, 0.1f);

        sim.Tick(new() { { 1, default(InputState) } });
        var after = sim.GetState(1);
        Assert.True(after.VZ > 0f && after.VZ < before.VZ,
            $"Run release should brake (not stop dead), got VZ={after.VZ:F3}");
    }

    [Fact]
    public void RunPerpendicularRedirect_ClearsOldAxis()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Run right past the Rush window (Run mode), then press forward (+Z): the
        // rightward velocity must be cleared instantly — no diagonal drag.
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveX: 1f) } });
        TestHelpers.AssertNear(Move.RunSpeed, sim.GetState(1).VX, 0.1f);

        sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        TestHelpers.AssertNear(Move.RunSpeed, after.VZ, 0.1f);
    }

    [Fact]
    public void RushDance_WasdCycleStaysInRushThenReversesCrisply()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // W→A→S→D is a chain of 90° redirects. Each must restart the Rush window
        // (previously it burned down and dropped the fighter into Run, where a
        // reversal skids as a Turnaround instead of flipping instantly).
        var dirs = new (float x, float z)[] { (0f, 1f), (-1f, 0f), (0f, -1f), (1f, 0f) };
        for (int i = 0; i < 12; i++)
        {
            var (x, z) = dirs[i % 4];
            sim.Tick(new() { { 1, TestHelpers.Input(moveX: x, moveY: z) } });
            Assert.True(sim.GetState(1).RushTicks > 0,
                $"tick {i}: fell out of Rush (RushTicks={sim.GetState(1).RushTicks})");
        }

        // Last dir was D (+X); reverse to A (−X) — must be an instant full-speed flip.
        sim.Tick(new() { { 1, TestHelpers.Input(moveX: -1f) } });
        var s = sim.GetState(1);
        TestHelpers.AssertNear(-Move.RunSpeed, s.VX, 0.01f);
        Assert.Equal(0f, s.VZ);
    }

    [Fact]
    public void AbilityActivation_RefreshesRushWindow_AndFreezesThroughTheMove()
    {
        // ADR-0020: activating an ability refills the Rush dash-dance window, and the
        // countdown only drains while purely running (Simulation.TickTimers gates it on
        // the Run state). A poke mid-footsie keeps the instant-reversal privilege;
        // Run's slow Turnaround appears only after holding one direction a long time.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var def = TestHelpers.FightGuyDef;
        var state = TestHelpers.PlayerState(50f, 50f) with { PY = TestHelpers.GroundPY(def) };
        TestHelpers.RegisterPlayer(sim, def, state);

        // Run +Z past the Rush window (RushTicks = 10) into Run proper.
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        Assert.Equal((ushort)0, sim.GetState(1).RushTicks);

        // Poke (Slot1 Low Kick) while running: the activation refills the window.
        sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f, activeSlot: AbilitySlots.Slot1) } });
        Assert.Equal(def.Movement.RushTicks, sim.GetState(1).RushTicks);

        // The countdown is frozen for the whole move, so the fighter exits in Rush —
        // the window is full on the tick control returns to Idle.
        int guard = 0;
        while (sim.GetState(1).State == ActionState.Attacking && guard++ < 120)
            sim.Tick(new() { { 1, default } });
        var after = sim.GetState(1);
        Assert.NotEqual(ActionState.Attacking, after.State);
        Assert.Equal(def.Movement.RushTicks, after.RushTicks);
    }

    [Fact]
    public void RunDiagonalStraighten_ClearsReleasedAxis()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold up-right diagonal past the Rush window into Run.
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveX: 0.707f, moveY: 0.707f) } });

        // Release W (keep D): the released Z axis must clear instantly.
        sim.Tick(new() { { 1, TestHelpers.Input(moveX: 1f) } });
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VZ);
        TestHelpers.AssertNear(Move.RunSpeed, after.VX, 0.1f);
    }

    // ── ServerAbility attack lifecycle (hitstun after hit) ──


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
    // ── Float-window gravity ──
    // FallRampChar: Manki definition with FloatWindowTicks=10 for float-window gravity tests
    private static readonly CharacterDefinition FallRampDef = CreateFallRampDef();
    private static readonly float FallRampFloatPerTick = FallRampDef.Movement.AirFloatGravity * Simulation.TickDt;
    private static readonly float FallRampFullPerTick = 35f * Simulation.TickDt;   // Gravity=35
    
    private static CharacterDefinition CreateFallRampDef()
    {
        var mov = TestHelpers.MankiDef.Movement;
        mov.AirFloatGravity = 6f;
        mov.FloatWindowTicks = 10;
        return TestHelpers.CloneDef(TestHelpers.MankiDef, mov);
    }

    [Fact]
    public void FallRamp_AirTimeIncrementsEachTick()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);

        // AirTime starts at 0 (default)
        Assert.Equal(0, (int)sim.GetState(1).AirTimeTicks);

        var t1 = TestHelpers.TickDefault(sim, 1);
        Assert.Equal(1, (int)t1.AirTimeTicks);

        var t2 = TestHelpers.TickDefault(sim, 1);
        Assert.Equal(2, (int)t2.AirTimeTicks);
    }

    [Fact]
    public void FallRamp_AirTimeResetsOnLanding()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);

        // Fall for 30 ticks (accumulates AirTime)
        for (int i = 0; i < 30; i++)
            TestHelpers.TickDefault(sim, 1);

        var before = sim.GetState(1);
        Assert.True(before.IsGrounded, "Should have landed after 30 ticks");
        Assert.Equal(0, (int)before.AirTimeTicks);
    }
    
    [Fact]
    public void FallRamp_FloatWindowUsesReducedGravity()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);
        
        // Tick 1: still in FloatWindow (FloatWindowTicks=10)
        var t1 = TestHelpers.TickDefault(sim, 1);
        float expectedVY = 0f - FallRampFloatPerTick;
        TestHelpers.AssertNear(expectedVY, t1.VY, 0.001f);
        
        // Tick 2: still in FloatWindow
        var t2 = TestHelpers.TickDefault(sim, 1);
        expectedVY -= FallRampFloatPerTick;
        TestHelpers.AssertNear(expectedVY, t2.VY, 0.001f);
    }
    
    [Fact]
    public void AfterFloatWindow_UsesFullGravity()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 8f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);

        // Tick through the FloatWindow (10 ticks) — full gravity past it.
        for (int i = 0; i < 10; i++)
            TestHelpers.TickDefault(sim, 1);

        float vyBefore = sim.GetState(1).VY;
        TestHelpers.TickDefault(sim, 1);
        float vyDelta = sim.GetState(1).VY - vyBefore;

        TestHelpers.AssertNear(-FallRampFullPerTick, vyDelta, 0.01f);
    }
    
    [Fact]
    public void FallRamp_NoRampWhenGrounded()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 0f + FallRampDef.CapsuleHeight * 0.5f; // grounded
        state.IsGrounded = true;
        state.JumpsLeft = 2;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);
        
        // Grounded: AirTime should be 0, gravity should not apply (grounded has its own path)
        TestHelpers.TickDefault(sim, 5);
        var s = sim.GetState(1);
        Assert.True(s.IsGrounded);
        Assert.Equal(0, (int)s.AirTimeTicks);
    }
    
    [Fact]
    public void FallRamp_AirTimeResetsOnDoubleJump()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        state.JumpsLeft = 2;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);
        
        // Fall for 15 ticks to accumulate AirTime
        for (int i = 0; i < 15; i++)
            TestHelpers.TickDefault(sim, 1);
        Assert.True(sim.GetState(1).AirTimeTicks > 0, "Should have accumulated AirTime");
        
        // Double jump — AirTime set to FloatWindowTicks, then gravity increments by 1.
        // FallRampDef: FloatWindowTicks=10 → AirTime = 10 + 1 = 11.
        var afterJump = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(FallRampDef.Movement.FloatWindowTicks + 1, (int)afterJump.AirTimeTicks);
    }

        // ── Jump arc timing ──

    [Fact]
    public void FightGuy_JumpArcTiming()
    {
        var def = TestHelpers.FightGuyDef;
        var mov = def.Movement;
        float groundPy = TestHelpers.GroundPY(def, 0f);
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = groundPy;
        TestHelpers.RegisterPlayer(sim, def, state);

        // Step 1: JumpSquat duration
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(ActionState.JumpSquat, t0.State);
        Assert.Equal(mov.JumpSquatTicks, (int)t0.StateTicks);
        int squatTicks = mov.JumpSquatTicks;

        // Complete squat
        for (int i = 1; i < squatTicks; i++)
            TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.JumpSquat, sim.GetState(1).State);

        // Step 2: Jump fires (squat expires)
        TestHelpers.TickDefault(sim, 1); // squat expiry → VY applied
        var afterSquat = sim.GetState(1);
        Assert.False(afterSquat.IsGrounded);
        int ascentTicks = 0;

        // Step 3: Count ascent ticks until VY ≤ 0
        while (sim.GetState(1).VY > 0f)
        {
            TestHelpers.TickDefault(sim, 1);
            ascentTicks++;
        }
        var atPeak = sim.GetState(1);

        // Step 4: Count descent ticks until grounded
        int descentTicks = 0;
        while (!sim.GetState(1).IsGrounded)
        {
            TestHelpers.TickDefault(sim, 1);
            descentTicks++;
        }

        // Totals
        int totalAirTicks = ascentTicks + descentTicks;
        int totalFromPress = squatTicks + totalAirTicks;
        float secSquat = squatTicks / 60f;
        float secAscent = ascentTicks / 60f;
        float secDescent = descentTicks / 60f;
        float secTotal = totalFromPress / 60f;

        System.Console.WriteLine("=== FightGuy Jump Arc Timing ===");
        System.Console.WriteLine($"JumpSquat: {squatTicks} ticks ({secSquat:F3}s)");
        System.Console.WriteLine($"Ascent (VY>0): {ascentTicks} ticks ({secAscent:F3}s)");
        System.Console.WriteLine($"Descent (to ground): {descentTicks} ticks ({secDescent:F3}s)");
        System.Console.WriteLine($"Total (from press): {totalFromPress} ticks ({secTotal:F3}s)");
        System.Console.WriteLine($"Air ticks only: {totalAirTicks}");
        System.Console.WriteLine();
        System.Console.WriteLine("Animation: 24 frames @ 30fps = 0.800s (48 ticks @ 60)");
        System.Console.WriteLine("  Ascent: 15 frames = 0.500s (30 ticks)");
        System.Console.WriteLine("  Descent: 9 frames = 0.300s (18 ticks)");
        System.Console.WriteLine();
        System.Console.WriteLine($"Game ascent vs clip ascent: {secAscent*60f:F0} vs 30 anim-ticks");
        System.Console.WriteLine($"Game total vs clip: {totalAirTicks} vs 48 anim-ticks");
        int clipOvershoot = totalAirTicks - 48;
        System.Console.WriteLine($"Clip overshoot (crouch frames shown mid-air): {clipOvershoot} ticks = {clipOvershoot/2f:F1} anim frames");
    }
}
