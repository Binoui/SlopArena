using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for state machine transitions (ActionState changes) and movement physics.
/// </summary>
public class PhysicsTests
{
    // Classic definition with FloatWindowTicks=0, FallRampDuration=0 for backward-compatible gravity tests
    private static readonly CharacterDefinition Def = CreateClassicDef();
    private static readonly MovementStats Move = Def.Movement;
    private static readonly float GroundPx = TestHelpers.MankiGroundPY;
    private static readonly float GravPerTick = Move.Gravity * Simulation.TickDt;

    private static CharacterDefinition CreateClassicDef()
    {
        var mov = TestHelpers.MankiDef.Movement;
        mov.FloatWindowTicks = 0;
        mov.FallRampDuration = 0;
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
        TestHelpers.AssertNear(Move.JumpForce - GravPerTick, doubled.VY, 0.01f);
    }

    [Fact]
    public void GroundJump_PreservesHorizontalMomentum()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.VX = Move.WalkSpeed; // running at walk speed
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Enter JumpSquat — VX preserved (not zeroed)
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(ActionState.JumpSquat, t0.State);
        Assert.Equal(Move.WalkSpeed, t0.VX);

        // Remainder of JumpSquat — VX stays at walk speed (no friction during squat)
        for (int i = 1; i < Move.JumpSquatTicks; i++)
        {
            var s = TestHelpers.TickDefault(sim, 1);
            Assert.Equal(ActionState.JumpSquat, s.State);
            Assert.Equal(Move.WalkSpeed, s.VX);
        }

        // Squat expires → airborne, momentum preserved (air drag reduces slightly)
        var tJump = TestHelpers.TickDefault(sim, 1);
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
    // ── FallRamp (progressive gravity) ──
    
    // FallRampChar: Manki definition with FloatWindowTicks=10, FallRampDuration=20 for testing
    private static readonly CharacterDefinition FallRampDef = CreateFallRampDef();
    private static readonly float FallRampFloatPerTick = FallRampDef.Movement.AirFloatGravity * Simulation.TickDt;
    private static readonly float FallRampFullPerTick = 35f * Simulation.TickDt;   // Gravity=35
    
    private static CharacterDefinition CreateFallRampDef()
    {
        var mov = TestHelpers.MankiDef.Movement;
        mov.AirFloatGravity = 6f;
        mov.FloatWindowTicks = 10;
        mov.FallRampDuration = 20;
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
    public void FallRamp_RampIncreasesGravityProgressively()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 3f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);
        
        // Run through tick 10 (end of FloatWindow, start of ramp)
        for (int i = 0; i < 10; i++)
            TestHelpers.TickDefault(sim, 1);
        
        
        // Track VY deltas during ramp (ticks 11-30 = ramp phase)
        float[] deltas = new float[FallRampDef.Movement.FallRampDuration];
        for (int i = 0; i < deltas.Length; i++)
        {
            float beforeVy = sim.GetState(1).VY;
            TestHelpers.TickDefault(sim, 1);
            float afterVy = sim.GetState(1).VY;
            deltas[i] = afterVy - beforeVy; // negative = falling faster
        }
        
        // Each delta should be >= the previous (gravity increases monotonically)
        for (int i = 1; i < deltas.Length; i++)
        {
            Assert.True(deltas[i] <= deltas[i - 1],
                $"Ramp delta at step {i} ({deltas[i]:F6}) should be <= step {i - 1} ({deltas[i - 1]:F6})");
        }
        // First ramp tick should be close to AirFloatGravity
        TestHelpers.AssertNear(-FallRampFloatPerTick, deltas[0], 0.05f);
    }
    
    [Fact]
    public void FallRamp_FullGravityAfterRamp()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);
        
        // Tick through FloatWindow (10) + Ramp (20) = 30 ticks
        for (int i = 0; i < 10 + 20; i++)
            TestHelpers.TickDefault(sim, 1);
        
        // Now past ramp: full gravity should apply
        float vyBefore = sim.GetState(1).VY;
        TestHelpers.TickDefault(sim, 1);
        float vyDeltaFull = sim.GetState(1).VY - vyBefore;
        
        TestHelpers.AssertNear(-FallRampFullPerTick, vyDeltaFull, 0.01f);
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
        
        // Double jump — AirTime set past FloatWindow+Ramp, then gravity increments by 1
        // FallRampDef: FloatWindowTicks=10, FallRampDuration=20 → AirTime = 10+20+1 = 31
        var afterJump = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(31, (int)afterJump.AirTimeTicks);
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
