using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class PredictedTrackTests
{
    private const ulong OpponentId = 2;
    private static readonly CharacterDefinition Def = TestHelpers.EngineDef;

    private static CharacterState GroundState(float x = 0f)
        => TestHelpers.PlayerState(x) with { PY = TestHelpers.GroundPY(Def) };

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
        var def = Def;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var packet = MakePacket(10, GroundState(x: 3f), hasInput: false);
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
        var def = Def;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var movingInput = TestHelpers.Input(moveX: 1f);
        var packet = MakePacket(10, GroundState(), hasInput: true, movingInput);
        track.ApplyBatch(new[] { packet }, currentLocalTick: 13, defs, baked);

        Assert.Equal(3u, track.LastFrontierTicks);

        // Reference: a plain ServerSimulation confirmed at the same base, ticked 3 times
        // with the same held input, should land at the same position.
        var reference = TestHelpers.MakeSim(arena);
        reference.RegisterEntity(OpponentId, def, GroundState());
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
        var def = Def;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var packet = MakePacket(10, GroundState(), hasInput: false);
        track.ApplyBatch(new[] { packet }, currentLocalTick: 11, defs, baked);

        var reference = TestHelpers.MakeSim(arena);
        reference.RegisterEntity(OpponentId, def, GroundState());
        var referenceResult = TestHelpers.TickDefault(reference, 1);

        Assert.Equal(referenceResult.PX, track.GetState(OpponentId).PX);
    }

    [Fact]
    public void ApplyBatch_DoesNotReplayDownEdgeIntoFrontier()
    {
        var arena = TestHelpers.TestArena();
        var def = Def;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var descending = TestHelpers.PlayerState();
        descending.PY = 5f;
        descending.IsGrounded = false;
        descending.VY = -1f;
        var exact = TestHelpers.Input(down: true);
        exact.DownPressed = true;
        var packet = MakePacket(10, descending, hasInput: true, exact);

        track.ApplyBatch(new[] { packet }, currentLocalTick: 11, defs, baked);

        var reference = TestHelpers.MakeSim(arena);
        reference.RegisterEntity(OpponentId, def, descending);
        var reused = exact;
        reused.DownPressed = false;
        reference.Tick(new Dictionary<ulong, InputState> { { OpponentId, reused } });

        Assert.Equal(reference.GetState(OpponentId).VY, track.GetState(OpponentId).VY);
        Assert.True(packet.Input.DownPressed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyBatch_DoesNotRepeatDefenseEdgesOnPredictionFrontier(bool grab)
    {
        var arena = TestHelpers.TestArena();
        var def = Def;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { [OpponentId] = def };
        var baked = new Dictionary<ulong, BakedAnimationData?> { [OpponentId] = null };
        var edge = new InputState { ShieldPressed = !grab, GrabPressed = grab };

        track.ApplyBatch(new[] { MakePacket(10, GroundState(), true, edge) },
            11, defs, baked);

        Assert.NotEqual(grab ? ActionState.GrabAttempt : ActionState.Shielding,
            track.GetState(OpponentId).State);
    }

    [Fact]
    public void StopTracking_RemovesEntityFromPrediction()
    {
        var arena = TestHelpers.TestArena();
        var def = Def;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };
        track.ApplyBatch(new[] { MakePacket(1, GroundState(), false) }, 1, defs, baked);
        Assert.True(track.IsTracking(OpponentId));

        track.StopTracking(OpponentId);

        Assert.False(track.IsTracking(OpponentId));
    }
}
