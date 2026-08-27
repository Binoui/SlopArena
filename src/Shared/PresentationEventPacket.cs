using System;
using System.Buffers.Binary;
using System.Text;

namespace SlopArena.Shared;

public readonly struct PresentationEventPacket
{
    private const uint Magic = 0x53455250;
    public const int Version = 1;
    public const int HeaderSize = 22;
    public const int MaxPresentationIdBytes = 64;
    public const int MaxSize = HeaderSize + MaxPresentationIdBytes;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public PresentationEventPacket(uint matchTick, ulong entityId, int operationIndex, string presentationId)
    {
        if (operationIndex < 0) throw new ArgumentOutOfRangeException(nameof(operationIndex));
        ValidateId(presentationId);
        MatchTick = matchTick;
        EntityId = entityId;
        OperationIndex = operationIndex;
        PresentationId = presentationId;
    }

    public uint MatchTick { get; }
    public ulong EntityId { get; }
    public int OperationIndex { get; }
    public string PresentationId { get; }
    public int WireSize => HeaderSize + Utf8.GetByteCount(PresentationId);

    public TimelinePresentationEvent ToEvent()
        => new(MatchTick, EntityId, OperationIndex, PresentationId);

    public void Serialize(Span<byte> buffer)
    {
        if (buffer.Length < WireSize) throw new ArgumentException("Buffer too small", nameof(buffer));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Magic);
        buffer[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(5), MatchTick);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(9), EntityId);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(17), OperationIndex);
        int length = Utf8.GetBytes(PresentationId, buffer.Slice(HeaderSize));
        buffer[21] = (byte)length;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> buffer, out PresentationEventPacket? packet)
    {
        packet = null;
        if (buffer.Length < HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(buffer) != Magic
            || buffer[4] != Version)
            return false;

        int length = buffer[21];
        if (length is < 1 or > MaxPresentationIdBytes || buffer.Length != HeaderSize + length)
            return false;
        int operationIndex = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(17));
        if (operationIndex < 0) return false;
        string presentationId;
        try
        {
            presentationId = Utf8.GetString(buffer.Slice(HeaderSize, length));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        packet = new PresentationEventPacket(
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(5)),
            BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(9)),
            operationIndex,
            presentationId);
        return true;
    }

    private static void ValidateId(string presentationId)
    {
        if (string.IsNullOrEmpty(presentationId))
            throw new ArgumentException("Presentation ID must not be empty.", nameof(presentationId));
        int byteCount;
        try { byteCount = Utf8.GetByteCount(presentationId); }
        catch (EncoderFallbackException ex) { throw new ArgumentException("Presentation ID must be valid UTF-8.", nameof(presentationId), ex); }
        if (byteCount > MaxPresentationIdBytes)
            throw new ArgumentException("Presentation ID is too long.", nameof(presentationId));
    }
}
