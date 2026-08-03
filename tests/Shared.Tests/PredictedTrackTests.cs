using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class PredictedTrackTests
{
    private const ulong OpponentId = 2;

    private static ServerEntityPacket MakePacket(uint tick, CharacterState state, bool hasInput, InputState input = default)
        => new ServerEntityPacket
        {
            EntityId = OpponentId,
            Tick = tick,
            State = CharacterStatePacket.FromState(state, tick),
            HasInput = hasInput,
            Input = input,
        };

    [Fact]
    public void ApplyBatch_RegistersAndTracksOnFirstPacket()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var packet = MakePacket(10, TestHelpers.PlayerState(x: 3f), hasInput: false);
        track.ApplyBatch(new[] { packet }, currentLocalTick: 10, defs, baked);

        Assert.True(track.IsTracking(OpponentId));
        Assert.Equal(3f, track.GetState(OpponentId).PX);
    }

    [Fact]
    public void ApplyBatch_ReplaysFrontierWithHeldLastInput()
    {
        // The batch confirms tick 10; the local clock is already at tick 13 (3-tick RTT).
        // The relayed input (moving +X) should be held for those 3 frontier ticks.
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var movingInput = TestHelpers.Input(moveX: 1f);
        var packet = MakePacket(10, TestHelpers.PlayerState(), hasInput: true, movingInput);
        track.ApplyBatch(new[] { packet }, currentLocalTick: 13, defs, baked);

        Assert.Equal(3u, track.LastFrontierTicks);

        // Reference: a plain ServerSimulation confirmed at the same base, ticked 3 times
        // with the same held input, should land at the same position.
        var reference = TestHelpers.MakeSim(arena);
        reference.RegisterEntity(OpponentId, def, TestHelpers.PlayerState());
        CharacterState referenceResult = default;
        for (int i = 0; i < 3; i++)
        {
            reference.Tick(new Dictionary<ulong, InputState> { { OpponentId, movingInput } });
            referenceResult = reference.GetState(OpponentId);
        }

        Assert.Equal(referenceResult.PX, track.GetState(OpponentId).PX);
    }

    [Fact]
    public void ApplyBatch_NoInputMarker_HoldsDefaultNotLastRelayed()
    {
        // hasInput=false must reproduce the server's default(InputState) path exactly (D2) —
        // not silently reuse whatever was last relayed.
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var packet = MakePacket(10, TestHelpers.PlayerState(), hasInput: false);
        track.ApplyBatch(new[] { packet }, currentLocalTick: 11, defs, baked);

        var reference = TestHelpers.MakeSim(arena);
        reference.RegisterEntity(OpponentId, def, TestHelpers.PlayerState());
        var referenceResult = TestHelpers.TickDefault(reference, 1);

        Assert.Equal(referenceResult.PX, track.GetState(OpponentId).PX);
    }

    [Fact]
    public void StopTracking_RemovesEntityFromPrediction()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };
        track.ApplyBatch(new[] { MakePacket(1, TestHelpers.PlayerState(), false) }, 1, defs, baked);
        Assert.True(track.IsTracking(OpponentId));

        track.StopTracking(OpponentId);

        Assert.False(track.IsTracking(OpponentId));
    }
}
