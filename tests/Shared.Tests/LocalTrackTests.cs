using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class LocalTrackTests
{
    private const ulong SelfId = 1;
    private const ulong OpponentId = 2;

    [Fact]
    public void Tick_AdvancesLikeServerSimulation_ForIdleMovement()
    {
        // A LocalTrack ticked with a rightward-move input should move exactly like a
        // plain ServerSimulation given the same input — no divergence for a fresh sim.
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(def, TestHelpers.PlayerState());

        var reference = TestHelpers.MakeSim(arena);
        TestHelpers.RegisterPlayer(reference, def, TestHelpers.PlayerState());

        var input = TestHelpers.Input(moveX: 1f);
        CharacterState localResult = default;
        for (int i = 0; i < 10; i++)
            localResult = track.Tick(input);
        CharacterState referenceResult = default;
        for (int i = 0; i < 10; i++)
        {
            reference.Tick(new System.Collections.Generic.Dictionary<ulong, InputState> { { SelfId, input } });
            referenceResult = reference.GetState(SelfId);
        }

        Assert.Equal(referenceResult.PX, localResult.PX);
        Assert.Equal(referenceResult.PZ, localResult.PZ);
        Assert.Equal(referenceResult.State, localResult.State);
    }

    [Fact]
    public void ReconcileWithServer_SnapsPositionWhenServerDisagrees_DuringPredictableWindow()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(def, TestHelpers.PlayerState());

        // Advance 5 ticks of pure idle (Predictable) — matches the ring's recorded ticks 1..5.
        CharacterState state = default;
        for (int i = 0; i < 5; i++)
            state = track.Tick(default);
        Assert.Equal(0, track.CorrectionCount);

        // Server disagrees on tick 3's position (simulated packet loss / float drift).
        var serverPacket = new CharacterStatePacket
        {
            PositionX = state.PX + 5f, // deliberately wrong vs. what we actually had at tick 3
            CurrentActionState = (byte)ActionState.Idle,
        };
        track.ReconcileWithServer(new ServerEntityPacket { EntityId = SelfId, Tick = 3, State = serverPacket });

        Assert.Equal(1, track.CorrectionCount);
        // After replaying ticks 4-5 forward from the corrected tick-3 base with zero input,
        // PX should now reflect the server's correction, not the original run's value.
        Assert.Equal(state.PX + 5f, track.GetState().PX);
    }

    [Fact]
    public void ReconcileWithServer_SkipsCorrection_WhenPacketTickOutsideWindow()
    {
        var track = new SlopArena.Shared.Rollback.LocalTrack(TestHelpers.TestArena(), SelfId);
        track.RegisterEntity(TestHelpers.MankiDef, TestHelpers.PlayerState());
        for (int i = 0; i < 3; i++) track.Tick(default);

        // Tick 999 was never in this LocalTrack's history — must be a no-op, not a crash.
        track.ReconcileWithServer(new ServerEntityPacket { EntityId = SelfId, Tick = 999, State = default });

        Assert.Equal(0, track.CorrectionCount);
    }

    [Fact]
    public void SyncOpponentMirror_PreventsTargetLockCrash()
    {
        // Regression test for the KeyNotFoundException risk: ServerSimulation.Tick()
        // indexes _states[targetId] directly whenever input.TargetEntityId != 0, for ANY
        // entity, attacking or not. A self-only LocalTrack sim with no opponents registered
        // must not crash when the player has an opponent soft-locked on screen.
        var arena = TestHelpers.TestArena();
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(TestHelpers.MankiDef, TestHelpers.PlayerState());
        track.SyncOpponentMirror(OpponentId, TestHelpers.MankiDef, TestHelpers.PlayerState(x: 5f));

        var input = new InputState { TargetEntityId = (byte)OpponentId };
        var ex = Record.Exception(() => { track.Tick(input); });

        Assert.Null(ex);
    }
}
