using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for the ledge hang (ADR-0020): an off-grid entity within grab range of a stage
/// edge enters the occupied <see cref="ActionState.LedgeHang"/> state — not the old
/// auto-pop. Three escapes leave it: jump, W (stand onto the stage), S (drop). A second
/// entity cannot grab an already-hung ledge (occupancy).
/// </summary>
public class LedgeSnapTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPx = TestHelpers.MankiGroundPY; // 0.75 (capsuleHalf)

    [Fact]
    public void FallsOffEdge_EntersLedgeHang()
    {
        // Manki at edge (X=199.5 is off the 200-wide heightmap), airborne, falling
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.EdgeState(posX: 199.5f);
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = sim.GetState(1);
        Assert.False(t0.IsGrounded);

        var after = TestHelpers.TickDefault(sim, 1);

        Assert.Equal(ActionState.LedgeHang, after.State);
        Assert.False(after.IsGrounded);
        Assert.Equal(0f, after.VY);
        Assert.True(after.InvincibilityTicks > 0);
    }

    [Fact]
    public void FallingFarBelowEdge_DoesNotGrab()
    {
        // Manki far below the stage surface — too deep for ledge grab
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.EdgeState(posX: 199.5f, py: -4.35f, vy: -10f);
        TestHelpers.RegisterPlayer(sim, Def, state);

        var beforePy = sim.GetState(1).PY;
        var after = TestHelpers.TickDefault(sim, 1);

        // Keeps falling — no grab
        Assert.False(after.IsGrounded);
        Assert.True(after.PY < beforePy - 0.1f);
    }

    [Fact]
    public void HitstunDuringLedgeFall_DoesNotGrab()
    {
        // At edge, airborne, in hitstun with knockback velocity
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.EdgeState(posX: 199.5f);
        state.State = ActionState.Hitstun;
        state.HitstunTicks = 10;
        state.KVY = -5f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickDefault(sim, 1);

        // Hitstun takes priority — no grab
        Assert.False(after.IsGrounded);
    }

    [Fact]
    public void KnockbackWithoutHitstun_DoesNotGrab()
    {
        // At edge, airborne, knockback active with no hitstun (rare edge case)
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.EdgeState(posX: 199.5f);
        state.KVX = 10f;
        state.KVY = 5f;
        state.KVZ = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickDefault(sim, 1);

        // Knockback path runs first — no grab
        Assert.False(after.IsGrounded);
    }

    [Fact]
    public void OverPlatform_DoesNotGrab()
    {
        // Manki over the platform (center of arena), not at edge
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState(x: 10f, z: 10f);
        state.PY = GroundPx + 0.1f; // just above ground
        state.VY = -5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickDefault(sim, 1);

        // Normal ground collision (not a grab)
        Assert.True(after.IsGrounded);
        TestHelpers.AssertNear(GroundPx, after.PY, 0.01f);
        TestHelpers.AssertNear(0f, after.VY, 0.01f);
    }

    [Fact]
    public void LedgeHang_Jump_EscapesToJumpSquat()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        TestHelpers.RegisterPlayer(sim, Def, TestHelpers.EdgeState(posX: 199.5f));
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.LedgeHang, sim.GetState(1).State);

        var jumped = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(ActionState.JumpSquat, jumped.State);
        Assert.Equal(1u, jumped.JumpsLeft); // one jump consumed
    }

    [Fact]
    public void LedgeHang_TowardStage_StandsUp()
    {
        // The +X edge's inward normal points -X, so "toward the stage" is MoveX=-1.
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        TestHelpers.RegisterPlayer(sim, Def, TestHelpers.EdgeState(posX: 199.5f));
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.LedgeHang, sim.GetState(1).State);

        var stood = TestHelpers.TickN(sim, TestHelpers.Input(moveX: -1f), 1);
        Assert.True(stood.IsGrounded);
        Assert.Equal(ActionState.Run, stood.State);
    }

    [Fact]
    public void LedgeHang_AwayDrops_ThenNoRegrabWhileLocked()
    {
        // Away from the stage (MoveX=+1) = S-drop.
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        TestHelpers.RegisterPlayer(sim, Def, TestHelpers.EdgeState(posX: 199.5f));
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.LedgeHang, sim.GetState(1).State);

        var dropped = TestHelpers.TickN(sim, TestHelpers.Input(moveX: 1f), 1);
        Assert.Equal(ActionState.Idle, dropped.State);
        Assert.False(dropped.IsGrounded);
        Assert.True(dropped.VY < 0f);
        Assert.True(dropped.LedgeRegrabLockTicks > 0);

        // No re-grab while the lock is still live.
        for (int i = 0; i < 5; i++)
        {
            TestHelpers.TickDefault(sim, 1);
            Assert.NotEqual(ActionState.LedgeHang, sim.GetState(1).State);
        }
    }

    [Fact]
    public void OccupiedLedge_SecondEntityFallsPast()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        // Entity 1 grabs the edge.
        sim.RegisterEntity(1, Def, TestHelpers.EdgeState(posX: 199.5f));
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.LedgeHang, sim.GetState(1).State);

        // Entity 2 approaches the SAME edge — must fall past (occupied).
        sim.RegisterEntity(2, Def, TestHelpers.EdgeState(posX: 199.5f));
        TestHelpers.TickDefault(sim, 1);

        var e2 = sim.GetState(2);
        Assert.NotEqual(ActionState.LedgeHang, e2.State);
        Assert.False(e2.IsGrounded);
    }

    [Fact]
    public void LedgeHang_NoInput_StaysHungIndefinitely()
    {
        // Regression: the hang is not stable — gravity was applied during LedgeHang, so once
        // the 30-tick float window (AirFloatGravity=0) expired the character accelerated down
        // through the 2.5m FindLedge tolerance and fell off on its own (~tick 53, no input).
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, Def, TestHelpers.EdgeState(posX: 199.5f));
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.LedgeHang, sim.GetState(1).State);
        float rimY = TestHelpers.MankiGroundPY; // surface(0) + capsuleHalf

        // Long enough to blow far past the 30-tick float window (was ~53 ticks to fall out).
        var s = TestHelpers.TickDefault(sim, 300);
        Assert.Equal(ActionState.LedgeHang, s.State);
        Assert.False(s.IsGrounded);
        TestHelpers.AssertNear(rimY, s.PY, 0.01f);
        Assert.Equal(0f, s.VY);
    }

    [Fact]
    public void LedgeHang_SDrop_StaysDroppedThroughLockExpiry()
    {
        // Regression: the S-drop inherited the float window (gravity 0), so it only fell
        // ~1.5m inside the 30-tick regrab lock — still within the 2.5m grab tolerance → the
        // ledge auto re-grabbed at lock expiry. Now the drop ends the float window and falls
        // past the tolerance, so it must NOT re-grab even with no further input.
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, Def, TestHelpers.EdgeState(posX: 199.5f));
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.LedgeHang, sim.GetState(1).State);

        // Away from the stage (MoveX=+1 at the +X edge) = S-drop.
        var dropped = TestHelpers.TickN(sim, TestHelpers.Input(moveX: 1f), 1);
        Assert.Equal(ActionState.Idle, dropped.State);
        Assert.True(dropped.LedgeRegrabLockTicks > 0);

        // 40 ticks: past the 30-tick lock expiry and deep past the 2.5m grab tolerance
        // (rim 0.75 → PY≈-10 with Manki's 35 m/s² gravity), still falling — must NOT
        // have re-grabbed. (Stops before the off-grid fall hits KillHeight=-20 / respawn.)
        var s = TestHelpers.TickDefault(sim, 40);
        Assert.True(
            s.State != ActionState.LedgeHang && !s.IsGrounded && s.VY < 0f && s.PY < -5f,
            $"after lock: st={s.State} grounded={s.IsGrounded} PY={s.PY:F3} VY={s.VY:F3} lock={s.LedgeRegrabLockTicks}");
    }
}
