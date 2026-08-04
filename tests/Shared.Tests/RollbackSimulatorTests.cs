using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class RollbackSimulatorTests
{
    private const ulong SelfId = 1;
    private const ulong OpponentId = 2;

    private static ServerEntityPacket MakePacket(ulong entityId, uint tick, CharacterState state, bool hasInput = false, InputState input = default)
        => new ServerEntityPacket
        {
            EntityId = entityId,
            Tick = tick,
            State = CharacterStatePacket.FromState(state, tick),
            HasInput = hasInput,
            Input = input,
        };

    [Fact]
    public void SelfEntity_UsesLocalTrack_OpponentIdle_UsesPredictedTrack()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        sim.Tick(new Dictionary<ulong, InputState> { { SelfId, TestHelpers.Input(moveX: 1f) } });
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 1, TestHelpers.PlayerState(x: 10f)) });

        // Self moved (LocalTrack advanced it); opponent reflects the ingested packet.
        Assert.NotEqual(0f, sim.GetState(SelfId).PX);
        Assert.Equal(10f, sim.GetState(OpponentId).PX);
    }

    [Fact]
    public void OpponentEnteringComplexState_SwitchesToRawTrack_NoLongerRebuilt()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        // Tick 1: opponent Idle — PredictedTrack picks it up.
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 1, TestHelpers.PlayerState(x: 10f)) });
        Assert.Equal(10f, sim.GetState(OpponentId).PX);

        // Tick 2: server reports the opponent now Attacking, at a new position — Complex state,
        // must land on RawTrack: rendered exactly as reported, no re-simulation.
        var attackingState = TestHelpers.PlayerState(x: 11f);
        attackingState.State = ActionState.Attacking;
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 2, attackingState) });

        Assert.Equal(11f, sim.GetState(OpponentId).PX);
        Assert.Equal(ActionState.Attacking, sim.GetState(OpponentId).State);
    }

    [Fact]
    public void ReconcileSelf_RoutesToLocalTrack_IncrementsCorrectionCount()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        for (int i = 0; i < 3; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { SelfId, default } });

        var wrongState = TestHelpers.PlayerState(x: 999f);
        sim.ReconcileSelf(MakePacket(SelfId, 1, wrongState));

        Assert.Equal(1, sim.CorrectionCount);
        Assert.Equal(999f, sim.GetState(SelfId).PX);
    }

    [Fact]
    public void GetAllStates_IncludesSelfAndEveryRegisteredOpponent()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        var all = sim.GetAllStates();

        Assert.True(all.ContainsKey(SelfId));
        Assert.True(all.ContainsKey(OpponentId));
    }

    [Fact]
    public void IngestOpponentBatch_UnregisteredOpponent_FallsBackToRawTrack_NoThrow()
    {
        // Regression (PvP crash): PvPMatch originally never registered entities, so the
        // bridge's _defs was empty. The first Predictable opponent packet threw
        // KeyNotFoundException on defs[EntityId] — but only AFTER _registered.Add had
        // already marked the entity, so the next batch took the SetState branch and
        // created an unpaired _states key (state without def), crashing
        // SimulateMovement every tick. Unknown entities must fall back to RawTrack.
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());

        // Opponent 2 is deliberately NOT registered — the PvP bug condition.
        var idle = TestHelpers.PlayerState(x: 10f);
        for (int i = 0; i < 3; i++)
            sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, (uint)(i + 1), idle) });

        // Raw fallback: rendered exactly as reported, no re-simulation, no throw.
        Assert.Equal(10f, sim.GetState(OpponentId).PX);
        Assert.Equal(ActionState.Idle, sim.GetState(OpponentId).State);
    }
}
