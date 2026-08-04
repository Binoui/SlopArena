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
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

    private static NetplayHarness Harness(int delayTicks = 0, int dropEvery = 0)
        => new NetplayHarness(TestHelpers.TestArena(), Def, delayTicks, dropEvery);

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
    public void JumpDashAttackTrace_ConvergesExact_WithRttDelay()
    {
        // 2-tick RTT; attacks happen at tick 5-12 while the entities are ~10m apart
        // (Manki LMB range is far shorter), so no cross-hit lands and both sides
        // stay exactly converged — including the opponent's Complex→Predictable
        // re-registration after its attack ends.
        var h = Harness(delayTicks: 2);
        for (int t = 0; t < 240; t++)
        {
            InputState in1 = TestHelpers.Input(moveX: 1f,
                jump: t == 20 || t == 100, dash: t == 40);
            InputState in2 = TestHelpers.Input(moveX: -1f,
                jump: t == 30 || t == 110, dash: t == 50,
                activeSlot: t is >= 5 and < 12 ? (byte)1 : (byte)0);
            h.Step(in1, in2);
        }
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
            InputState in1 = TestHelpers.Input(moveX: 1f, jump: t == 20, dash: t == 40);
            InputState in2 = TestHelpers.Input(moveX: -1f, jump: t == 30, dash: t == 50);
            h.Step(in1, in2);
        }
        for (int t = 0; t < 8; t++) h.Step(default, default); // flush RTT window
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }

    [Fact]
    public void OpponentAttack_RawTrackThenReRegistration_ConvergesAfterComplexEnds()
    {
        // Entity 2 attacks early while ~9.5m from entity 1 (no cross-hit): the client
        // must route the Complex state to RawTrack, then re-register + rebuild the
        // predicted track when the attack ends, and land back on exact convergence.
        var h = Harness();
        for (int t = 0; t < 120; t++)
        {
            InputState in1 = TestHelpers.Input();
            InputState in2 = TestHelpers.Input(moveX: -0.5f,
                activeSlot: t is >= 5 and < 12 ? (byte)1 : (byte)0);
            h.Step(in1, in2);
        }
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }
}
