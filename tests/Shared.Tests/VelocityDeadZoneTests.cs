using Xunit;
using System.Collections.Generic;

namespace SlopArena.Shared.Tests;

public class VelocityDeadZoneTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;

    [Fact]
    public void VelocityDeadZone_GroundFriction_SnapsSubthresholdToZero()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.VX = 0.005f;
        state.VZ = 0.003f;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);
        TestHelpers.TickDefault(sim, 1);
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }

    [Fact]
    public void VelocityDeadZone_AirDrag_SnapsSubthresholdToZero()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 10f;
        state.IsGrounded = false;
        state.VX = 0.008f;
        state.VZ = 0.006f;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);
        TestHelpers.TickDefault(sim, 1);
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }

    [Fact]
    public void VelocityDeadZone_AboveThreshold_DoesNotSnap()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.VX = 1.0f;
        state.VZ = 1.0f;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);
        TestHelpers.TickDefault(sim, 1);
        var mid = sim.GetState(1);
        Assert.True(mid.VX > 0f && mid.VZ > 0f);
        for (int i = 0; i < 57; i++)
            TestHelpers.TickDefault(sim, 1);
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }
}


