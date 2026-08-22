using System;
using System.Buffers.Binary;

namespace SlopArena.Shared;

/// <summary>
/// One player's authoritative final match statistics. Names and character
/// presentation are resolved by the client from the locked match roster.
/// </summary>
public readonly struct MatchResultEntry
{
    public MatchResultEntry(ulong entityId, byte placement, byte kos, byte falls)
    {
        EntityId = entityId;
        Placement = placement;
        KOs = kos;
        Falls = falls;
    }

    public ulong EntityId { get; }
    public byte Placement { get; }
    public byte KOs { get; }
    public byte Falls { get; }
}

/// <summary>
/// Final match snapshot emitted by the authoritative game server. It is sent
/// repeatedly during the post-match window because the transport is UDP.
/// </summary>
public sealed class MatchResultPacket
{
    private const uint Magic = 0x544C5352; // little-endian bytes: RSLT
    private const int HeaderSize = 10;
    private const int EntrySize = 11;
    public const int MaxPlayers = 4;
    public const int MaxSize = HeaderSize + EntrySize * MaxPlayers;

    public MatchResultPacket(uint durationTicks, bool sharedVictory, MatchResultEntry[] entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        if (entries.Length is < 1 or > MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(entries));

        DurationTicks = durationTicks;
        SharedVictory = sharedVictory;
        Entries = entries;
    }

    public uint DurationTicks { get; }
    public bool SharedVictory { get; }
    public MatchResultEntry[] Entries { get; }
    public int WireSize => HeaderSize + EntrySize * Entries.Length;

    public void Serialize(Span<byte> buffer)
    {
        if (buffer.Length < WireSize)
            throw new ArgumentException("Buffer too small", nameof(buffer));

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(0, 4), Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(4, 4), DurationTicks);
        buffer[8] = SharedVictory ? (byte)1 : (byte)0;
        buffer[9] = (byte)Entries.Length;

        int offset = HeaderSize;
        foreach (var entry in Entries)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), entry.EntityId);
            buffer[offset + 8] = entry.Placement;
            buffer[offset + 9] = entry.KOs;
            buffer[offset + 10] = entry.Falls;
            offset += EntrySize;
        }
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> buffer, out MatchResultPacket? packet)
    {
        packet = null;
        if (buffer.Length < HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4)) != Magic)
            return false;

        int count = buffer[9];
        if (count is < 1 or > MaxPlayers || buffer.Length < HeaderSize + EntrySize * count)
            return false;

        var entries = new MatchResultEntry[count];
        int offset = HeaderSize;
        for (int i = 0; i < count; i++)
        {
            entries[i] = new MatchResultEntry(
                BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset, 8)),
                buffer[offset + 8],
                buffer[offset + 9],
                buffer[offset + 10]);
            offset += EntrySize;
        }

        packet = new MatchResultPacket(
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4, 4)),
            buffer[8] != 0,
            entries);
        return true;
    }
}
