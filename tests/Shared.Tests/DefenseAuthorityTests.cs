using System.Collections.Generic;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public class DefenseAuthorityTests
{
    private const ulong Self = 1;
    private const ulong Other = 2;

    private static ServerEntityPacket Packet(ulong id, uint tick, CharacterState state) => new()
    {
        EntityId = id, Tick = tick, State = CharacterStatePacket.FromState(state, tick),
    };

    private static RollbackSimulator Match()
    {
        var sim = new RollbackSimulator(TestHelpers.TestArena(), Self);
        sim.RegisterEntity(Self, TestHelpers.MankiDef, TestHelpers.PlayerState());
        sim.RegisterEntity(Other, TestHelpers.MankiDef, TestHelpers.PlayerState(x: 2));
        return sim;
    }

    [Fact]
    public void ConfirmedCaptureInterruptsComplexLocalAttackAndWaitsForTerminal()
    {
        var sim = Match();
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { ActiveSlot = 1 } });
        var captured = TestHelpers.PlayerState();
        captured.State = ActionState.Grabbed;
        captured.InteractionId = 17;
        captured.InteractionPartnerId = Other;
        captured.InteractionTick = 1;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, captured) });
        Assert.Equal(ActionState.Grabbed, sim.GetState(Self).State);

        for (int i = 0; i < 3; i++)
            sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { Jump = true, ActiveSlot = 1 } });
        Assert.Equal(ActionState.Grabbed, sim.GetState(Self).State);
        var partner = TestHelpers.PlayerState(x: 2);
        partner.State = ActionState.Throwing;
        partner.InteractionId = 17;
        partner.InteractionPartnerId = Self;
        partner.InteractionTick = 1;
        sim.IngestAuthoritativeBatch(new[] { Packet(Other, 2, partner) });
        Assert.Equal((ulong)17, sim.GetState(Other).InteractionId);

        var released = TestHelpers.PlayerState();
        released.LastTerminalInteractionId = 17;
        released.InteractionTerminalTick = 3;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 3, released) });
        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        Assert.Equal((ulong)0, sim.GetState(Self).InteractionId);
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, captured), Packet(Self, 3, released) });
        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = TestHelpers.Input(moveX: 1) });
        Assert.True(sim.GetState(Self).PX > released.PX);
    }

    [Fact]
    public void TerminalWithoutCaptureAndLateCompanionCannotResurrectInteraction()
    {
        var sim = Match();
        var terminal = TestHelpers.PlayerState();
        terminal.LastTerminalInteractionId = 51;
        terminal.InteractionTerminalTick = 8;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 8, terminal) });
        var stale = TestHelpers.PlayerState();
        stale.State = ActionState.Grabbed;
        stale.InteractionId = 51;
        stale.InteractionPartnerId = Other;
        stale.InteractionTick = 6;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 7, stale), Packet(Other, 7, stale) });
        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        sim.IngestAuthoritativeBatch(new[] { Packet(Other, 9, terminal) });
        Assert.Equal((ulong)0, sim.GetState(Other).InteractionId);
    }

    [Fact]
    public void OpponentCaptureArrivingFirstIsHeldUntilMatchingSelfIdentityAndTick()
    {
        var sim = Match();
        sim.Tick(new Dictionary<ulong, InputState>());
        var partner = TestHelpers.PlayerState(x: 2);
        partner.State = ActionState.Throwing;
        partner.InteractionId = 72;
        partner.InteractionPartnerId = Self;
        partner.InteractionTick = 1;
        sim.IngestAuthoritativeBatch(new[] { Packet(Other, 2, partner) });
        Assert.Equal((ulong)0, sim.GetState(Other).InteractionId);

        var captured = TestHelpers.PlayerState();
        captured.State = ActionState.Grabbed;
        captured.InteractionId = 72;
        captured.InteractionPartnerId = Other;
        captured.InteractionTick = 1;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 2, captured) });
        Assert.Equal((ulong)72, sim.GetState(Self).InteractionId);
        Assert.Equal((ulong)72, sim.GetState(Other).InteractionId);
    }

    [Fact]
    public void LateBlockStunOverridesComplexAttackWithoutReplayingAttack()
    {
        var sim = Match();
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { ActiveSlot = 1 } });
        var blocked = TestHelpers.PlayerState();
        blocked.State = ActionState.Shielding;
        blocked.BlockStunTicks = 6;
        blocked.BlockHitstopKind = (byte)DefenseBlockHitstopKind.ShieldContact;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, blocked) });
        Assert.Equal((ushort)6, sim.GetState(Self).BlockStunTicks);
        Assert.Equal(ActionState.Shielding, sim.GetState(Self).State);
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, blocked) });
        Assert.Equal(1, sim.CorrectionCount);
    }
}
