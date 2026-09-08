using System;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class StageCollisionTests
{
    private static CharacterDefinition Def => TestHelpers.MankiDef;

    [Fact]
    public void WalkingIntoTallWall_StopsWithoutClimbing()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), WallX(2f, 0f, 4f));
        var state = Grounded(0f, 0f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 120f;

        Simulation.SimulateTick(ref state, Def, default, arena);

        Assert.InRange(state.PX, 2f - Def.CapsuleRadius - 0.03f, 2f - Def.CapsuleRadius + 0.03f);
        Assert.InRange(state.PY, Def.CapsuleHeight * 0.5f - 0.01f, Def.CapsuleHeight * 0.5f + 0.01f);
    }

    [Fact]
    public void DiagonalWallContact_SlidesAndSettlesAtCorner()
    {
        var arena = Arena(
            Floor(0f, -10f, 10f, -10f, 10f),
            WallX(2f, 0f, 4f), WallZ(2f, 0f, 4f));
        var state = Grounded(0f, 0f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 120f;
        state.VZ = 60f;

        Simulation.SimulateTick(ref state, Def, default, arena);

        Assert.True(state.PX <= 2f - Def.CapsuleRadius + 0.03f);
        Assert.True(state.PZ <= 2f - Def.CapsuleRadius + 0.03f);
        Assert.True(state.PX > 1f && state.PZ > 0.5f);
    }

    [Fact]
    public void FallingOntoRaisedPlatform_LandsAtPlatformTop()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), Floor(2f, -2f, 2f, -2f, 2f));
        var state = Grounded(0f, 0f);
        state.PY = 5f;
        state.IsGrounded = false;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.AirTimeTicks = 30;
        state.VY = -180f;

        for (int i = 0; i < 20; i++)
            Simulation.SimulateTick(ref state, Def, default, arena);

        Assert.True(state.IsGrounded);
        Assert.InRange(state.PY, 2f + Def.CapsuleHeight * 0.5f - 0.02f,
            2f + Def.CapsuleHeight * 0.5f + 0.02f);
    }

    [Fact]
    public void MovingUnderElevatedPlatform_PreservesClearance()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), Floor(2f, -2f, 2f, -2f, 2f));
        var state = Grounded(0f, -4f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 30f;

        Simulation.SimulateTick(ref state, Def, default, arena);

        Assert.True(state.IsGrounded);
        Assert.InRange(state.PY, Def.CapsuleHeight * 0.5f - 0.02f, Def.CapsuleHeight * 0.5f + 0.02f);
        Assert.True(state.PX > 0.2f);
    }

    [Fact]
    public void JumpIntoPlatformUnderside_StopsUpwardAndStaysAirborne()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), Floor(2f, -2f, 2f, -2f, 2f));
        var state = Grounded(0f, 0f);
        state.PY = 1f;
        state.IsGrounded = false;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.AirTimeTicks = 30;
        state.VY = 120f;

        Simulation.SimulateTick(ref state, Def, default, arena);

        Assert.False(state.IsGrounded);
        Assert.Equal(0f, state.VY);
        Assert.InRange(state.PY, 2f - Def.CapsuleHeight * 0.5f - 0.03f,
            2f - Def.CapsuleHeight * 0.5f + 0.03f);
    }

    [Fact]
    public void DashAndKnockback_DoNotTunnelThroughThinWall()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), WallX(2f, 0f, 4f));
        var dash = Grounded(0f, 0f);
        dash.State = ActionState.Dashing;
        dash.DashDurationTicks = 1;
        dash.VX = 240f;
        Simulation.SimulateTick(ref dash, Def, default, arena);

        var knockback = Grounded(0f, 0f);
        knockback.State = ActionState.Idle;
        knockback.IsGrounded = false;
        knockback.KVX = 240f;
        Simulation.SimulateTick(ref knockback, Def, default, arena);

        Assert.True(dash.PX < 2f);
        Assert.True(knockback.PX < 2f);
    }

    [Fact]
    public void WalkingOffFinitePlatform_StartsFalling()
    {
        var arena = Arena(Floor(0f, -2f, 2f, -2f, 2f));
        var state = Grounded(1.8f, 0f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 30f;


        Simulation.SimulateTick(ref state, Def, default, arena);

        Assert.False(state.IsGrounded);
        Assert.True(state.PX > 2f);
        Assert.True(state.LedgeRegrabLockTicks > 0);
    }
    [Fact]
    public void Riftwalk_UsesStageResolverForRecoveryDisplacement()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), WallX(2f, 0f, 4f));
        var state = TestHelpers.PlayerState(0f, 0f);
        state.PY = TestHelpers.NilusDef.CapsuleHeight * 0.5f;
        state.IsGrounded = true;
        state.FacingYaw = MathF.PI * 0.5f;
        var ability = new SlopArena.Shared.Abilities.NilusRiftwalk
        {
            Slot = 3,
            Arena = arena,
        };
        ability.OnStart(ref state, TestHelpers.NilusDef);
        var input = default(InputState);
        ability.Tick(ref state, ref input, TestHelpers.NilusDef);

        Assert.InRange(state.PX, 2f - TestHelpers.NilusDef.CapsuleRadius - 0.03f,
            2f - TestHelpers.NilusDef.CapsuleRadius + 0.03f);
    }

    [Fact]
    public void TriangleMovement_IsDeterministicAcrossIdenticalRuns()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), WallX(2f, 0f, 4f));
        var first = Grounded(0f, 0f);
        first.State = ActionState.Attacking;
        first.AnimLockTicks = 20;
        first.VX = 17f;
        first.VZ = 4f;
        var second = first;

        for (int i = 0; i < 30; i++)
        {
            Simulation.SimulateTick(ref first, Def, default, arena);
            Simulation.SimulateTick(ref second, Def, default, arena);
        }

        Assert.Equal(first.PX, second.PX);
        Assert.Equal(first.PY, second.PY);
        Assert.Equal(first.PZ, second.PZ);
        Assert.Equal(first.VX, second.VX);
        Assert.Equal(first.VY, second.VY);
        Assert.Equal(first.VZ, second.VZ);
        Assert.Equal(first.IsGrounded, second.IsGrounded);
    }

    private static CharacterState Grounded(float x, float z)
    {
        var state = TestHelpers.PlayerState(x, z);
        state.PY = Def.CapsuleHeight * 0.5f;
        state.IsGrounded = true;
        return state;
    }

    private static ArenaDefinition Arena(params CollisionTriangle[] triangles)
    {
        var arena = TestHelpers.TestArena();
        arena.MinX = -20f;
        arena.MaxX = 20f;
        arena.MinZ = -20f;
        arena.MaxZ = 20f;
        arena.CollisionTriangles = triangles;
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    private static CollisionTriangle Floor(float y, float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = minX, AY = y, AZ = minZ,
            BX = minX, BY = y, BZ = maxZ,
            CX = maxX, CY = y, CZ = minZ,
        };

    private static CollisionTriangle WallX(float x, float minY, float maxY)
        => new()
        {
            AX = x, AY = minY, AZ = -10f,
            BX = x, BY = maxY, BZ = -10f,
            CX = x, CY = minY, CZ = 10f,
        };

    private static CollisionTriangle WallZ(float z, float minY, float maxY)
        => new()
        {
            AX = -10f, AY = minY, AZ = z,
            BX = -10f, BY = maxY, BZ = z,
            CX = 10f, CY = minY, CZ = z,
        };
}
