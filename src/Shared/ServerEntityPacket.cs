using System;
using System.Buffers.Binary;

namespace SlopArena.Shared
{
    /// <summary>
    /// Downlink per-entity state envelope: the existing broadcast
    /// (entityId + tick + CharacterStatePacket) plus the input-relay section
    /// (issue #80, ADR-0010): a 1-byte hasInput flag followed by the 19-byte
    /// InputState the server actually consumed for that entity that tick.
    ///
    /// Layout:
    ///   [0..7]   entityId          (8)
    ///   [8..11]  tick              (4)   — _serverTick echo, unchanged reconciliation anchor
    ///   [12..121] CharacterStatePacket (110)
    ///   [122]    hasInput          (1)   — 0x01 = relayed InputState follows; 0x00 = no input this tick
    ///   [123..142] InputState      (20)  — present iff hasInput == 1
    ///
    /// hasInput = 0 reproduces the server's empty-queue path exactly
    /// (Simulation falls back to default(InputState)): the client must omit the
    /// entity from its re-sim inputs dict. The flag is always present, so a
    /// no-input packet is 123 bytes and a relayed-input packet is 143 bytes.
    /// </summary>
    public struct ServerEntityPacket
    {
        public ulong EntityId;
        public uint Tick;
        public CharacterStatePacket State;
        /// <summary>True when the server consumed an InputState for this entity this tick.</summary>
        public bool HasInput;
        /// <summary>The relayed input (meaningful iff <see cref="HasInput"/>).</summary>
        public InputState Input;

        /// <summary>122 bytes — envelope without the relay section.</summary>
        public const int BaseSize = 8 + 4 + CharacterStatePacket.Size;
        /// <summary>21 bytes — hasInput flag + InputState.</summary>
        public const int RelaySize = 1 + InputState.Size;
        /// <summary>143 bytes — full envelope with relayed input.</summary>
        public const int MaxSize = BaseSize + RelaySize;
        /// <summary>123 bytes — envelope with the no-input marker.</summary>
        public const int NoInputSize = BaseSize + 1;

        /// <summary>Encoded length of this packet (123 or 143 bytes).</summary>
        public int WireSize => HasInput ? MaxSize : NoInputSize;

        public void Serialize(Span<byte> buffer)
        {
            if (buffer.Length < WireSize)
                throw new ArgumentException("Buffer too small");

            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(0, 8), EntityId);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(8, 4), Tick);
            State.Serialize(buffer.Slice(12));
            buffer[BaseSize] = HasInput ? (byte)1 : (byte)0;
            if (HasInput)
                Input.Write(buffer.Slice(BaseSize + 1));
        }

        public static ServerEntityPacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < BaseSize)
                throw new ArgumentException("Buffer too small");

            var packet = new ServerEntityPacket
            {
                EntityId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(0, 8)),
                Tick = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8, 4)),
                State = CharacterStatePacket.Deserialize(buffer.Slice(12)),
            };

            if (buffer.Length >= MaxSize && buffer[BaseSize] == 1)
            {
                packet.HasInput = true;
                // The MaxSize gate IS the truncation handling: a packet short of
                // 145 bytes with the flag set reads as the no-input marker, keeping
                // the invariant that HasInput implies a full, exact relayed input.
                // (Truncated packets are never emitted by the server.)
                packet.Input = InputState.Deserialize(buffer.Slice(BaseSize + 1));
            }
            return packet;
        }
    }
}
