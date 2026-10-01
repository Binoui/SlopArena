using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>Presentation-only snapshot of players with an active sword hitbox.</summary>
public readonly struct SwordTrailSnapshotPacket
{
    private const uint Magic = 0x4E525453; // STRN
    private const byte Version = 1;
    public const int HeaderSize = 10;
    public const int MaxOwners = 4;
    public const int MaxWireSize = HeaderSize + sizeof(ulong) * MaxOwners;

    public SwordTrailSnapshotPacket(uint tick, IReadOnlyList<ulong> activeOwnerIds)
    {
        if (activeOwnerIds is null || activeOwnerIds.Count > MaxOwners)
            throw new ArgumentOutOfRangeException(nameof(activeOwnerIds));
        Tick = tick;
        ActiveOwnerIds = activeOwnerIds;
    }

    public uint Tick { get; }
    public IReadOnlyList<ulong> ActiveOwnerIds { get; }
    public int WireSize => HeaderSize + sizeof(ulong) * ActiveOwnerIds.Count;

    public void Serialize(Span<byte> buffer)
    {
        if (ActiveOwnerIds is null || ActiveOwnerIds.Count > MaxOwners || buffer.Length < WireSize)
            throw new ArgumentException("Invalid sword trail snapshot buffer or owners.", nameof(buffer));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Magic);
        buffer[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(5), Tick);
        buffer[9] = (byte)ActiveOwnerIds.Count;
        for (int i = 0; i < ActiveOwnerIds.Count; i++)
        {
            ulong ownerId = ActiveOwnerIds[i];
            if (ownerId == 0) throw new ArgumentException("Owner IDs must be nonzero.", nameof(ActiveOwnerIds));
            for (int j = 0; j < i; j++)
                if (ActiveOwnerIds[j] == ownerId)
                    throw new ArgumentException("Owner IDs must be distinct.", nameof(ActiveOwnerIds));
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(HeaderSize + i * sizeof(ulong)), ownerId);
        }
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> buffer, out SwordTrailSnapshotPacket packet)
    {
        packet = default;
        if (buffer.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(buffer) != Magic || buffer[4] != Version)
            return false;
        int count = buffer[9];
        if (count > MaxOwners || buffer.Length != HeaderSize + count * sizeof(ulong)) return false;
        var owners = new ulong[count];
        for (int i = 0; i < count; i++)
        {
            ulong ownerId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(HeaderSize + i * sizeof(ulong)));
            if (ownerId == 0) return false;
            for (int j = 0; j < i; j++)
                if (owners[j] == ownerId) return false;
            owners[i] = ownerId;
        }
        packet = new SwordTrailSnapshotPacket(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(5)), owners);
        return true;
    }
}
