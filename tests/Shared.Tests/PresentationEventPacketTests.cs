using System;
using System.Buffers.Binary;
using System.Text;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class PresentationEventPacketTests
{
    private static PresentationEventPacket Packet(uint tick = 42, ulong entity = 7, int operation = 11, string id = "presentation.cyclone-kick.start")
        => new(tick, entity, operation, id, 3, PresentationEventSource.CapabilityExplosion, 1.25f, -2.5f, 3.75f, 0.5f);

    [Fact]
    public void RoundTripPreservesEventIdentityAndTransform()
    {
        var packet = Packet();
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);

        Assert.Equal(PresentationEventPacket.HeaderSize + Encoding.UTF8.GetByteCount(packet.PresentationId), packet.WireSize);
        Assert.True(PresentationEventPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(packet.ToEvent(), decoded!.Value.ToEvent());
        Assert.Equal(3, decoded.Value.AttackSequence);
        Assert.Equal(PresentationEventSource.CapabilityExplosion, decoded.Value.Source);
        Assert.Equal(1.25f, decoded.Value.WorldX);
        Assert.Equal(-2.5f, decoded.Value.WorldY);
        Assert.Equal(3.75f, decoded.Value.WorldZ);
        Assert.Equal(0.5f, decoded.Value.WorldYaw);
    }

    [Fact]
    public void InvalidDatagramsAreRejected()
    {
        var packet = Packet(1, 2, 3, "presentation.hit");
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);

        Assert.False(PresentationEventPacket.TryDeserialize(bytes.AsSpan(0, bytes.Length - 1), out _));
        var trailing = new byte[bytes.Length + 1];
        Array.Copy(bytes, trailing, bytes.Length);
        Assert.False(PresentationEventPacket.TryDeserialize(trailing, out _));

        var wrongMagic = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongMagic, 0);
        Assert.False(PresentationEventPacket.TryDeserialize(wrongMagic, out _));

        var oldVersion = (byte[])bytes.Clone();
        oldVersion[4] = 1;
        Assert.False(PresentationEventPacket.TryDeserialize(oldVersion, out _));

        var negativeIndex = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(negativeIndex.AsSpan(17), -1);
        Assert.False(PresentationEventPacket.TryDeserialize(negativeIndex, out _));

        var unknownSource = (byte[])bytes.Clone();
        unknownSource[22] = 255;
        Assert.False(PresentationEventPacket.TryDeserialize(unknownSource, out _));

        var nonfiniteTransform = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(nonfiniteTransform.AsSpan(23), unchecked((int)0x7FC00000));
        Assert.False(PresentationEventPacket.TryDeserialize(nonfiniteTransform, out _));

        var invalidUtf8 = (byte[])bytes.Clone();
        invalidUtf8[40] = 0xFF;
        Assert.False(PresentationEventPacket.TryDeserialize(invalidUtf8, out _));

        var zeroLength = (byte[])bytes.Clone();
        zeroLength[39] = 0;
        Assert.False(PresentationEventPacket.TryDeserialize(zeroLength, out _));

        var overlong = new byte[PresentationEventPacket.MaxSize + 1];
        Array.Copy(bytes, overlong, bytes.Length);
        overlong[39] = 65;
        Assert.False(PresentationEventPacket.TryDeserialize(overlong, out _));
    }

    [Fact]
    public void RollbackDeduplicatesPredictionAndLateConfirmation()
    {
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(TestHelpers.TestArena(), 1);
        var value = new TimelinePresentationEvent(3, 2, 9, "presentation.hit", 4, PresentationEventSource.Timeline, 1f, 2f, 3f, 0.25f);
        sim.IngestPresentationEvent(value);
        sim.IngestPresentationEvent(value with { PresentationId = "presentation.other" });

        var accepted = sim.DrainPresentationEvents();
        var only = Assert.Single(accepted);
        Assert.Equal(value, only);
        Assert.Empty(sim.DrainPresentationEvents());
    }

    [Fact]
    public void PredictedOpponentReplayEmitsAndSuppressesLateConfirmation()
    {
        var def = TestHelpers.FightGuyDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(TestHelpers.TestArena(), 1);
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        for (var tick = 0; tick < 11; tick++)
            sim.Tick(new System.Collections.Generic.Dictionary<ulong, InputState> { [1] = default });

        var opponent = TestHelpers.PlayerState(x: 10f) with
        {
            EntityId = 2,
            PY = TestHelpers.GroundPY(def),
        };
        sim.RegisterEntity(2, def, opponent);
        sim.IngestOpponentBatch(new[]
        {
            new ServerEntityPacket
            {
                EntityId = 2,
                Tick = 10,
                State = CharacterStatePacket.FromState(opponent, 10),
                HasInput = true,
                Input = new InputState { ActiveSlot = 5 },
            },
        });
        var predicted = Assert.Single(sim.DrainPresentationEvents());
        Assert.Equal(new PresentationEventKey(11, 2, 1, PresentationEventSource.Timeline, 10), predicted.Key);
        sim.IngestPresentationEvent(predicted);
        Assert.Empty(sim.DrainPresentationEvents());
    }

    [Fact]
    public void IndependentKeysAndDroppedDatagramDoNotAffectState()
    {
        var second = new PresentationEventPacket(11, 2, 4, "presentation.b", 1, PresentationEventSource.Timeline, 0f, 1f, 2f, 0f);
        var third = new PresentationEventPacket(12, 2, 5, "presentation.c", 2, PresentationEventSource.CapabilityExplosion, 3f, 4f, 5f, 1f);
        var firstPacket = new PresentationEventPacket(10, 2, 4, "presentation.a", 0, PresentationEventSource.Timeline, 0f, 0f, 0f, 0f);
        var firstBytes = new byte[firstPacket.WireSize];
        var secondBytes = new byte[second.WireSize];
        var thirdBytes = new byte[third.WireSize];
        firstPacket.Serialize(firstBytes);
        second.Serialize(secondBytes);
        third.Serialize(thirdBytes);

        Assert.False(PresentationEventPacket.TryDeserialize(firstBytes.AsSpan(0, firstBytes.Length - 1), out _));
        Assert.True(PresentationEventPacket.TryDeserialize(secondBytes, out var decodedSecond));
        Assert.True(PresentationEventPacket.TryDeserialize(thirdBytes, out var decodedThird));

        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(TestHelpers.TestArena(), 1);
        sim.RegisterEntity(1, TestHelpers.MankiDef, TestHelpers.PlayerState());
        var before = sim.GetState(1);
        sim.IngestPresentationEvent(decodedSecond!.Value.ToEvent());
        sim.IngestPresentationEvent(decodedThird!.Value.ToEvent());
        Assert.Equal(before, sim.GetState(1));
        var accepted = sim.DrainPresentationEvents();
        Assert.Equal(2, accepted.Count);
        Assert.Contains(second.ToEvent(), accepted);
        Assert.Contains(third.ToEvent(), accepted);
    }
}
