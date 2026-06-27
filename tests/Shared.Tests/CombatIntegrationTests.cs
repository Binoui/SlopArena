using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Two-entity combat integration tests.
/// Abilities are partially implemented — tests verify basic multi-entity
/// simulation integrity rather than full hit detection.
/// </summary>
public class CombatIntegrationTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPx = TestHelpers.MankiGroundPY;

    [Fact]
    public void TwoEntities_TickBoth_NeitherCorrupted()
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

    [Fact]
    public void TwoEntities_PlayerAttacks_NpcIdle_NpcStateStable()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        var pState = TestHelpers.PlayerState();
        pState.PY = GroundPx;
        sim.RegisterEntity(1, Def, pState);

        var nState = TestHelpers.NpcState(1.5f, 0f);
        nState.PY = GroundPx;
        sim.RegisterEntity(100, Def, nState);

        // Player presses LMB once, then both get default input
        for (int i = 0; i < 60; i++)
        {
            var inputs = new Dictionary<ulong, InputState>
            {
                { 1, i == 0 ? TestHelpers.Input(activeSlot: 1) : default },
                { 100, default },
            };
            sim.Tick(inputs);
        }

        // Player attacked, NPC stayed idle — basic multi-entity sim works
        var npcAfter = sim.GetState(100);
        Assert.Equal(ActionState.Idle, npcAfter.State);
    }
}
