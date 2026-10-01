using System;
using System.Buffers.Binary;
using System.Linq;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class SwordTrailSnapshotPacketTests
{
    [Fact]
    public void RoundTripsActiveOwnersAndEmptyRemovalSnapshot()
    {
        var active = new SwordTrailSnapshotPacket(42, new ulong[] { 11, 29 });
        var bytes = new byte[active.WireSize];
        active.Serialize(bytes);
        Assert.True(SwordTrailSnapshotPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(42u, decoded.Tick);
        Assert.Equal(new ulong[] { 11, 29 }, decoded.ActiveOwnerIds);

        var empty = new SwordTrailSnapshotPacket(43, Array.Empty<ulong>());
        var emptyBytes = new byte[empty.WireSize];
        empty.Serialize(emptyBytes);
        Assert.True(SwordTrailSnapshotPacket.TryDeserialize(emptyBytes, out var removal));
        Assert.Equal(43u, removal.Tick);
        Assert.Empty(removal.ActiveOwnerIds);
    }

    [Fact]
    public void RejectsMalformedOwnerListsAndWireLengths()
    {
        var valid = new SwordTrailSnapshotPacket(1, new ulong[] { 9 });
        var bytes = new byte[valid.WireSize];
        valid.Serialize(bytes);
        Assert.False(SwordTrailSnapshotPacket.TryDeserialize(bytes.AsSpan(0, bytes.Length - 1), out _));
        Assert.False(SwordTrailSnapshotPacket.TryDeserialize(bytes.Concat(new byte[] { 0 }).ToArray(), out _));

        var zeroOwner = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(zeroOwner.AsSpan(SwordTrailSnapshotPacket.HeaderSize), 0);
        Assert.False(SwordTrailSnapshotPacket.TryDeserialize(zeroOwner, out _));

        var duplicate = new SwordTrailSnapshotPacket(1, new ulong[] { 9, 10 });
        var duplicateBytes = new byte[duplicate.WireSize];
        duplicate.Serialize(duplicateBytes);
        duplicateBytes.AsSpan(SwordTrailSnapshotPacket.HeaderSize + sizeof(ulong), sizeof(ulong))
            .CopyTo(duplicateBytes.AsSpan(SwordTrailSnapshotPacket.HeaderSize));
        Assert.False(SwordTrailSnapshotPacket.TryDeserialize(duplicateBytes, out _));

        var excessiveCount = (byte[])bytes.Clone();
        excessiveCount[9] = SwordTrailSnapshotPacket.MaxOwners + 1;
        Assert.False(SwordTrailSnapshotPacket.TryDeserialize(excessiveCount, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SwordTrailSnapshotPacket(1, new ulong[] { 1, 2, 3, 4, 5 }));
    }
}
