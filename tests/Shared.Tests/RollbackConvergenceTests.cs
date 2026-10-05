using System;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Two-sim netplay convergence: authoritative ServerSimulation vs client
/// RollbackSimulator over the real packet codecs, with RTT delay and packet loss.
/// The scripted traces avoid cross-hits (attacks happen while the entities are far
/// apart) so the opponent converges exactly too.
/// </summary>
public class RollbackConvergenceTests
{
    private static readonly CharacterDefinition Def = TestHelpers.EngineDef;

    private static NetplayHarness Harness(int delayTicks = 0, int dropEvery = 0, CharacterDefinition? def = null)
        => new NetplayHarness(TestHelpers.TestArena(), def ?? Def, delayTicks, dropEvery);

    private static CharacterDefinition AttackDef()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = AttackSlot(0, "ground.1", isAir: false);
        def.CookedSlots = slots;
        return def;
    }

    private static CookedSlotDefinition AttackSlot(int ordinal, string id, bool isAir)
    {
        var hitbox = new CookedHitbox(
            AuthoringHitboxShape.Sphere, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f,
            null, null, 5f, 0f, 2f, 1f, 5, 3, true, 0);
        var timeline = new CookedTimeline(new[]
        {
            new CookedStage(8, 0, 0, 0, 0, Array.Empty<string>(),
                new CookedTimelineOperation[]
                {
                    new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters, hitbox),
                }),
        });
        return new CookedSlotDefinition(ordinal, id, isAir, "Rollback fixture attack", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false, timeline);
    }

    [Fact]
    public void MovementTrace_ConvergesExact_NoDelay()
    {
        var h = Harness();
        for (int t = 0; t < 120; t++)
            h.Step(TestHelpers.Input(moveX: 1f), TestHelpers.Input(moveX: -1f));
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }


    [Fact]
    public void PacketLoss_ReconvergesAfterLastReceivedPacket()
    {
        // Drop every 5th packet (ticks 5, 10, …). Missed opponent packets make the
        // prediction diverge until the next packet corrects it; after the trace,
        // an idle flush drains the RTT window so the final reconcile + replay
        // re-converges exactly.
        var h = Harness(delayTicks: 2, dropEvery: 5);
        for (int t = 0; t < 240; t++)
        {
            InputState in1 = TestHelpers.Input(moveX: 1f, jump: t == 20, jumpHeld: t >= 20 && t < 40);
            in1.ShieldHeld = in1.ShieldPressed = t == 40;
            InputState in2 = TestHelpers.Input(moveX: -1f, jump: t == 30, jumpHeld: t >= 30 && t < 50);
            in2.ShieldHeld = in2.ShieldPressed = t == 50;
            h.Step(in1, in2);
        }
        for (int t = 0; t < 8; t++) h.Step(default, default); // flush RTT window
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }

    [Fact]
    public void FacingSnapTrace_ConvergesExact_WithRttDelay()
    {
        // FaceToCamera (ADR-0017 / issue #126) rides the relayed InputState: the
        // client's rollback replay must reproduce the server's snapped facing exactly.
        // Keep the opponent stationary; this test is about facing, not pushbox collision
        // response while the delayed opponent mirror is two ticks behind.
        var h = Harness(delayTicks: 2);
        for (int t = 0; t < 120; t++)
        {
            InputState in1 = t == 30
                ? new InputState { FaceToCamera = true, AimYaw = 18000 }
                : t == 31
                    ? TestHelpers.Input(moveX: 1f)
                    : default;
            h.Step(in1, default);
        }
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }

    [Fact]
    public void IndependentOriginsWithAsymmetricDelayAndLoss_RecoverBothTracks()
    {
        var h = Harness(delayTicks: 8, dropEvery: 5);
        Assert.True(h.SetTimeline(300));
        float initialSelfX = h.ServerState(NetplayHarness.SelfId).PX;
        float initialOpponentX = h.ServerState(NetplayHarness.OpponentId).PX;
        const int selfUplinkDelay = 5;
        const int opponentUplinkDelay = 7;

        static InputState SelfTrace(int tick)
            => tick >= 20 && tick < 120 ? TestHelpers.Input(moveX: 1f) : default;
        static InputState OpponentTrace(int tick)
            => tick >= 65 && tick < 155 ? TestHelpers.Input(moveX: -1f) : default;

        for (int tick = 0; tick < 200; tick++)
        {
            var clientInput = SelfTrace(tick);
            var serverInput = SelfTrace(tick - selfUplinkDelay);
            var opponentInput = OpponentTrace(tick - opponentUplinkDelay);
            h.Step(clientInput, serverInput, opponentInput);
        }

        h.SetDropsEnabled(false);
        for (int tick = 0; tick < 40; tick++)
            h.Step(default, default, default);

        Assert.True(h.ServerState(NetplayHarness.SelfId).PX > initialSelfX);
        Assert.True(h.ServerState(NetplayHarness.OpponentId).PX < initialOpponentX);
        Assert.NotEqual(initialOpponentX, h.ClientState(NetplayHarness.OpponentId).PX);
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }

    [Fact]
    public void OpponentAttack_RawTrackThenReRegistration_ConvergesAfterComplexEnds()
    {
        // Entity 2 attacks early while ~9.5m from entity 1 (no cross-hit): the client
        // must route the Complex state to RawTrack, then re-register + rebuild the
        // predicted track when the attack ends, and land back on exact convergence.
        var h = Harness(def: AttackDef());
        bool sawAttack = false;
        for (int t = 0; t < 120; t++)
        {
            InputState in1 = TestHelpers.Input();
            InputState in2 = TestHelpers.Input(moveX: -0.5f,
                activeSlot: t is >= 5 and < 12 ? AbilitySlots.Slot1 : AbilitySlots.None);
            h.Step(in1, in2);
            sawAttack |= h.ServerState(NetplayHarness.OpponentId).State == ActionState.Attacking;
        }
        Assert.True(sawAttack, "the explicit fixture attack should enter its timeline");
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }
}
