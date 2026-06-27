using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Edge cases: input buffering, cooldown countdown, entity isolation.
/// Tests focus on simulation integrity rather than ServerAbility behavior.
/// </summary>
public class EdgeCaseTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPx = TestHelpers.MankiGroundPY;

    [Fact]
    public void Cooldown_CountsDownOverTime()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.Cooldown0 = 60;
        TestHelpers.RegisterPlayer(sim, Def, state);

        for (int i = 0; i < 60; i++)
            TestHelpers.TickDefault(sim, 1);

        var after = sim.GetState(1);
        Assert.Equal(0, (int)after.Cooldown0);
    }

    [Fact]
    public void TwoEntitiesIdle_NeitherStateCorrupted()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        var pState = TestHelpers.PlayerState();
        pState.PY = GroundPx;
        sim.RegisterEntity(1, Def, pState);

        var nState = TestHelpers.NpcState(3f, 0f);
        nState.PY = GroundPx;
        sim.RegisterEntity(100, Def, nState);

        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var pAfter = sim.GetState(1);
        var nAfter = sim.GetState(100);
        Assert.Equal(ActionState.Idle, pAfter.State);
        Assert.Equal(ActionState.Idle, nAfter.State);
    }
}
