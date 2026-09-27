using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Demo edge behavior: no automatic ledge grab; normal landing remains available.</summary>
public class LedgeSnapTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPx = TestHelpers.MankiGroundPY; // 0.75 (capsuleHalf)

    [Fact]
    public void FallingBesideStage_DoesNotGrabOrGainResources()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.EdgeState(posX: 199.5f);
        state.JumpsLeft = 0;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var before = sim.GetState(1);
        var after = TestHelpers.TickDefault(sim, 1);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.False(after.IsGrounded);
        Assert.True(after.PY < before.PY);
        Assert.True(after.VY < 0f);
        Assert.Equal(0u, after.JumpsLeft);
        Assert.Equal((ushort)0, after.InvincibilityTicks);
    }

    [Fact]
    public void FallingOverPlatform_LandsNormally()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(x: 10f, z: 10f);
        state.PY = GroundPx + 0.1f; // just above ground
        state.VY = -5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickDefault(sim, 1);

        // Normal ground collision still works.
        Assert.True(after.IsGrounded);
        TestHelpers.AssertNear(GroundPx, after.PY, 0.01f);
        TestHelpers.AssertNear(0f, after.VY, 0.01f);
    }

    [Fact]
    public void AirDodgeRecoveryBesideStage_ContinuesFallingWithoutRefillingResources()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.EdgeState(posX: 199.5f);
        state.State = ActionState.AirDodgeRecovery;
        state.StateTicks = 20;
        state.AirDodgeRecoveryTicks = 20;
        state.AirDodgesLeft = 0;
        state.JumpsLeft = 0;
        sim.RegisterEntity(1, Def, state);

        var before = sim.GetState(1);
        var after = TestHelpers.TickDefault(sim, 1);

        Assert.Equal(ActionState.AirDodgeRecovery, after.State);
        Assert.False(after.IsGrounded);
        Assert.True(after.PY < before.PY);
        Assert.Equal((byte)0, after.AirDodgesLeft);
        Assert.Equal(0u, after.JumpsLeft);
    }
}
