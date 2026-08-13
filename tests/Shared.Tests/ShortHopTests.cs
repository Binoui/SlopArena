using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Short-hop tests (issue #116 / #106, ADR-0016 → ADR-0020): releasing the jump key within
/// <see cref="Simulation.ShortHopWindowTicks"/> of the press produces a reduced jump
/// (<c>MovementStats.ShortHopForce</c>); holding past the window produces the full jump
/// (<c>JumpForce</c>). The decision runs at JumpSquat expiry, deferring the force one tick at
/// a time while the player is still holding inside the window (so a release just past squat
/// expiry still counts as a short hop). Air double jumps always use the air-jump force
/// (<c>JumpForce × AirJumpVMultiplier</c>) — no short hop in the air.
/// </summary>
public class ShortHopTests
{
    // Classic def: no FloatWindow so post-jump gravity is full from the first air tick.
    private static readonly CharacterDefinition Def = CreateClassicDef();
    private static readonly MovementStats Move = Def.Movement;
    private static readonly float GroundPy = TestHelpers.MankiGroundPY;
    private static readonly float GravPerTick = Move.Gravity * Simulation.TickDt;
    /// <summary>The reduced jump force — a per-character m/s value, not a JumpForce fraction.</summary>
    private static readonly float ShortHopForce = Move.ShortHopForce;

    private static CharacterDefinition CreateClassicDef()
    {
        var mov = TestHelpers.MankiDef.Movement;
        mov.FloatWindowTicks = 0;
        return TestHelpers.CloneDef(TestHelpers.MankiDef, mov);
    }

    private static ServerSimulation SimWithGroundedPlayer()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);
        return sim;
    }

    [Fact]
    public void TapRelease_ShortHop_ReducedJumpVelocity()
    {
        // Press for 1 tick then release: at squat expiry JumpHeldTicks = 0 ≤ window → short hop.
        // FightGuy's ShortHopForce (7.2) clears the ground snap; Manki's 6.0 sits below the
        // snap threshold (ADR-0020 data note) and re-grounds instead of hopping.
        var fg = TestHelpers.FightGuyDef;
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(fg);
        TestHelpers.RegisterPlayer(sim, fg, state);

        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), fg.Movement.JumpSquatTicks + 1);
        var s = sim.GetState(1);

        Assert.False(s.IsGrounded);
        Assert.Equal(ActionState.Idle, s.State);
        float fgGrav = fg.Movement.Gravity * Simulation.TickDt;
        TestHelpers.AssertNear(fg.Movement.ShortHopForce - fgGrav, s.VY, 0.01f);
    }

    [Fact]
    public void HoldThroughSquat_FullJump()
    {
        // Holding the jump key (JumpHeld) past the window = full jump force. The Jump
        // edge is one tick; the hold continues through the squat (holding the edge itself
        // would re-trigger jump detection on the fire tick and consume a second jump).
        var sim = SimWithGroundedPlayer();
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), Move.JumpSquatTicks);
        var s = sim.GetState(1);

        Assert.False(s.IsGrounded);
        TestHelpers.AssertNear(Move.JumpForce - GravPerTick, s.VY, 0.01f);
    }

    [Fact]
    public void ShortHop_PeakHeight_LowerThanFullHop()
    {
        // AC: "short-hop vs full-hop trajectories differ as specified" — the tap jump must
        // peak lower than the held jump.
        var tapSim = SimWithGroundedPlayer();
        var fullSim = SimWithGroundedPlayer();

        TestHelpers.TickN(tapSim, TestHelpers.Input(jump: true, jumpHeld: true), Move.JumpSquatTicks + 1);
        TestHelpers.TickN(fullSim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        TestHelpers.TickHold(fullSim, TestHelpers.Input(jumpHeld: true), Move.JumpSquatTicks);

        float tapPeak = 0f, fullPeak = 0f;
        for (int i = 0; i < 90; i++)
        {
            TestHelpers.TickDefault(tapSim, 1);
            TestHelpers.TickDefault(fullSim, 1);
            tapPeak = MathF.Max(tapPeak, tapSim.GetState(1).PY);
            fullPeak = MathF.Max(fullPeak, fullSim.GetState(1).PY);
        }

        Assert.True(tapPeak < fullPeak,
            $"short hop must peak lower: tap={tapPeak:F3} vs full={fullPeak:F3}");
        Assert.True(fullPeak > GroundPy + 0.5f, $"full hop should be a real jump: {fullPeak:F3}");
    }

    [Fact]
    public void ReleaseInsideWindow_AfterSquatExpiry_ShortHop()
    {
        // Deferral case: squat (4) < window (5). The player is still holding at squat expiry,
        // so the force is deferred one tick; releasing within the window still short-hops.
        var fg = TestHelpers.FightGuyDef; // JumpSquatTicks = 4
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(fg);
        TestHelpers.RegisterPlayer(sim, fg, state);

        // Hold ticks 0-4 (5 held ticks = window), release on tick 5.
        TestHelpers.TickHold(sim, TestHelpers.Input(jump: true, jumpHeld: true), 5);
        var deferred = sim.GetState(1);
        Assert.Equal(ActionState.JumpSquat, deferred.State); // decision pending at expiry

        TestHelpers.TickN(sim, TestHelpers.Input(), 1);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.False(s.IsGrounded);
        float fgGrav = fg.Movement.Gravity * Simulation.TickDt;
        TestHelpers.AssertNear(fg.Movement.ShortHopForce - fgGrav, s.VY, 0.01f);
    }

    [Fact]
    public void HoldPastWindow_AtSquatExpiry_FullJump()
    {
        // Same squat<window setup: holding into tick 5 (6 held ticks > window) = full jump,
        // even though the release comes later.
        var fg = TestHelpers.FightGuyDef;
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(fg);
        TestHelpers.RegisterPlayer(sim, fg, state);

        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), 5);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.False(s.IsGrounded);
        float fgGrav = fg.Movement.Gravity * Simulation.TickDt;
        TestHelpers.AssertNear(fg.Movement.JumpForce - fgGrav, s.VY, 0.01f);
    }

    [Fact]
    public void AirDoubleJump_UsesAirJumpForce()
    {
        // Air jumps apply the (weaker) air-jump force on the press tick — no short hop in the
        // air (issue #116), but ADR-0020 scales it by AirJumpVMultiplier.
        var sim = SimWithGroundedPlayer();
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), Move.JumpSquatTicks);

        var doubled = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0u, doubled.JumpsLeft);
        TestHelpers.AssertNear(Move.JumpForce * Move.AirJumpVMultiplier - GravPerTick, doubled.VY, 0.01f);
    }
}
