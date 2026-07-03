using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for ledge snap (auto-grab when near stage edge).
/// Characters falling off the stage edge snap back to the surface
/// with a small upward hop, unless in hitstun or active knockback.
/// </summary>
public class LedgeSnapTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPx = TestHelpers.MankiGroundPY; // 0.65

    [Fact]
    public void FallsOffEdge_LedgeSnapsToSurface()
    {
        // Manki at edge (X=199.5 is off the 200-wide heightmap), airborne, falling
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.EdgeState(posX: 199.5f);
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = sim.GetState(1);

        // Initially off-grid and falling
        Assert.False(t0.IsGrounded);

        var after = TestHelpers.TickDefault(sim, 1);

        // Snapped to ground with upward boost
        Assert.True(after.IsGrounded);
        TestHelpers.AssertNear(GroundPx, after.PY, 0.01f);
        Assert.True(after.VY > 0);
    }

    [Fact]
    public void FallingFarBelowEdge_DoesNotSnap()
    {
        // Manki far below the stage surface — too deep for ledge grab
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.EdgeState(posX: 199.5f, py: -4.35f, vy: -10f);
        TestHelpers.RegisterPlayer(sim, Def, state);

        var beforePy = sim.GetState(1).PY;
        var after = TestHelpers.TickDefault(sim, 1);

        // Keeps falling — no snap
        Assert.False(after.IsGrounded);
        Assert.True(after.PY < beforePy - 0.1f);
    }

    [Fact]
    public void HitstunDuringLedgeFall_DoesNotSnap()
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

        // Hitstun takes priority — no ledge snap
        Assert.False(after.IsGrounded);
    }

    [Fact]
    public void KnockbackWithoutHitstun_DoesNotSnap()
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

        // Knockback path runs first — returns before ledge snap
        Assert.False(after.IsGrounded);
    }

    [Fact]
    public void OverPlatform_DoesNotLedgeSnap()
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

        // Normal ground collision (not ledge snap — no VY boost)
        Assert.True(after.IsGrounded);
        TestHelpers.AssertNear(GroundPx, after.PY, 0.01f);
        TestHelpers.AssertNear(0f, after.VY, 0.01f);
    }

    [Fact]
    public void FallsOffEdgeWithAirDodge_ClearsAirDodgeOnSnap()
    {
        // At edge, airborne, air dodging
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.EdgeState(posX: 199.5f);
        state.State = ActionState.AirDodging;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickDefault(sim, 1);

        // Ledge snap clears air dodge state
        Assert.True(after.IsGrounded);
        Assert.Equal(ActionState.Idle, after.State);
    }
}
