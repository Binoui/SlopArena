using System;
using System.Buffers.Binary;
using System.Text;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class PresentationEventPacketTests
{
    [Fact]
    public void RoundTripPreservesEventAndWireSize()
    {
        var packet = new PresentationEventPacket(42, 7, 11, "presentation.cyclone-kick.start");
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);

        Assert.Equal(PresentationEventPacket.HeaderSize + Encoding.UTF8.GetByteCount(packet.PresentationId), packet.WireSize);
        Assert.True(PresentationEventPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(packet.ToEvent(), decoded!.Value.ToEvent());
    }

    [Fact]
    public void InvalidDatagramsAreRejected()
    {
        var packet = new PresentationEventPacket(1, 2, 3, "presentation.hit");
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);

        Assert.False(PresentationEventPacket.TryDeserialize(bytes.AsSpan(0, bytes.Length - 1), out _));
        Assert.False(PresentationEventPacket.TryDeserialize(bytes.Concat(new byte[] { 0 }).ToArray(), out _));

        var wrongMagic = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongMagic, 0);
        Assert.False(PresentationEventPacket.TryDeserialize(wrongMagic, out _));

        var wrongVersion = (byte[])bytes.Clone();
        wrongVersion[4] = 2;
        Assert.False(PresentationEventPacket.TryDeserialize(wrongVersion, out _));

        var negativeIndex = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(negativeIndex.AsSpan(17), -1);
        Assert.False(PresentationEventPacket.TryDeserialize(negativeIndex, out _));

        var invalidUtf8 = (byte[])bytes.Clone();
        invalidUtf8[22] = 0xFF;
        Assert.False(PresentationEventPacket.TryDeserialize(invalidUtf8, out _));

        var zeroLength = (byte[])bytes.Clone();
        zeroLength[21] = 0;
        Assert.False(PresentationEventPacket.TryDeserialize(zeroLength, out _));

        var overlong = new byte[PresentationEventPacket.MaxSize + 1];
        Array.Copy(bytes, overlong, bytes.Length);
        overlong[21] = 65;
        Assert.False(PresentationEventPacket.TryDeserialize(overlong, out _));
    }

    [Fact]
    public void RollbackDeduplicatesPredictionAndLateConfirmation()
    {
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(TestHelpers.TestArena(), 1);
        var value = new TimelinePresentationEvent(3, 2, 9, "presentation.hit");
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
        Assert.Equal(new PresentationEventKey(11, 2, 10), predicted.Key);
        sim.IngestPresentationEvent(predicted);
        Assert.Empty(sim.DrainPresentationEvents());
    }
    [Fact]
    public void IndependentKeysAndDroppedDatagramDoNotAffectState()
    {
        var second = new PresentationEventPacket(11, 2, 4, "presentation.b");
        var third = new PresentationEventPacket(12, 2, 5, "presentation.c");
        var firstBytes = new byte[new PresentationEventPacket(10, 2, 4, "presentation.a").WireSize];
        var secondBytes = new byte[second.WireSize];
        var thirdBytes = new byte[third.WireSize];
        new PresentationEventPacket(10, 2, 4, "presentation.a").Serialize(firstBytes);
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
