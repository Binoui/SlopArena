using System;
using System.Buffers.Binary;
using System.IO;

namespace SlopArena.Shared
{
    /// <summary>
    /// Downlink per-entity state envelope:
    /// entityId(8) + tick(4) + CharacterStatePacket(164) + relay marker(1)
    /// + InputState(22) when relayed.
    /// </summary>
    public struct ServerEntityPacket
    {
        public ulong EntityId;
        public uint Tick;
        public CharacterStatePacket State;
        public bool HasInput;
        public InputState Input;

        public const int BaseSize = 8 + 4 + CharacterStatePacket.Size;
        public const int RelaySize = 1 + InputState.Size;
        public const int MaxSize = BaseSize + RelaySize;
        public const int NoInputSize = BaseSize + 1;

        public int WireSize => HasInput ? MaxSize : NoInputSize;

        public void Serialize(Span<byte> buffer)
        {
            if (buffer.Length < WireSize)
                throw new ArgumentException("Buffer too small", nameof(buffer));

            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(0, 8), EntityId);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(8, 4), Tick);
            State.Serialize(buffer.Slice(12, CharacterStatePacket.Size));
            buffer[BaseSize] = HasInput ? (byte)1 : (byte)0;
            if (HasInput)
                Input.Write(buffer.Slice(BaseSize + 1, InputState.Size));
        }

        public static ServerEntityPacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length != NoInputSize && buffer.Length != MaxSize)
                throw new ArgumentException($"Entity payload must be exactly {NoInputSize} or {MaxSize} bytes.", nameof(buffer));

            byte relayMarker = buffer[BaseSize];
            bool hasInput = buffer.Length == MaxSize;
            if (relayMarker == 1 && !hasInput)
                throw new ArgumentException("Input relay is truncated.", nameof(buffer));
            if (relayMarker != (hasInput ? (byte)1 : (byte)0))
                throw new InvalidDataException("Relay marker does not match entity payload length.");

            var packet = new ServerEntityPacket
            {
                EntityId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(0, 8)),
                Tick = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8, 4)),
                State = CharacterStatePacket.Deserialize(buffer.Slice(12, CharacterStatePacket.Size)),
                HasInput = hasInput,
            };
            if (hasInput)
                packet.Input = InputState.Deserialize(buffer.Slice(BaseSize + 1, InputState.Size));
            return packet;
        }
    }
}
