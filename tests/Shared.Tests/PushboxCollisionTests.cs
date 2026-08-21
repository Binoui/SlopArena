using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class PushboxCollisionTests
{
    [Fact]
    public void MovingFightersAreSeparatedAtBodyContact()
    {
        var def = TestHelpers.CombatDef;
        var sim = TestHelpers.MakeSim();
        var first = TestHelpers.PlayerState(-0.2f, 0f) with { PY = TestHelpers.CombatGroundPY, VX = 1f };
        var second = TestHelpers.NpcState(0.2f, 0f) with { PY = TestHelpers.CombatGroundPY, VX = -1f };
        sim.RegisterEntity(1, def, first);
        sim.RegisterEntity(100, def, second);

        sim.Tick(new() { { 1, default }, { 100, default } });

        var a = sim.GetState(1);
        var b = sim.GetState(100);
        float distance = System.MathF.Sqrt((b.PX - a.PX) * (b.PX - a.PX) + (b.PZ - a.PZ) * (b.PZ - a.PZ));
        Assert.True(distance >= def.CapsuleRadius * 2f - 0.0001f, $"body overlap remained: {distance}");
    }

    [Fact]
    public void VerticallySeparatedFightersDoNotPushEachOther()
    {
        var def = TestHelpers.CombatDef;
        var pairSim = TestHelpers.MakeSim();
        var singleSim = TestHelpers.MakeSim();
        var first = TestHelpers.PlayerState(0f, 0f) with { PY = TestHelpers.CombatGroundPY, VX = 1f };
        var second = TestHelpers.NpcState(0.2f, 0f) with { PY = TestHelpers.CombatGroundPY + 10f, VX = -1f, IsGrounded = false };
        pairSim.RegisterEntity(1, def, first);
        pairSim.RegisterEntity(100, def, second);
        singleSim.RegisterEntity(1, def, first);

        pairSim.Tick(new() { { 1, default }, { 100, default } });
        singleSim.Tick(new() { { 1, default } });

        Assert.Equal(singleSim.GetState(1).PX, pairSim.GetState(1).PX, 3);
    }
}
