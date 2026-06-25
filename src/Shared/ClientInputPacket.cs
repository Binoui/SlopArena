using System;
using System.Buffers.Binary;

namespace SlopArena.Shared
{
    public struct ClientInputPacket
    {
        public uint TickNumber;
        /// <summary>
        /// Bit 0: Z, Bit 1: Q, Bit 2: S, Bit 3: D, Bit 4: Space (Jump), Bit 5: Shift (Dash), Bit 6: E (Respawn), Bit 7: Ctrl/C (Crouch)
        /// </summary>
        public byte MovementFlags;
        public float MouseWorldX;
        public float MouseWorldY;
        /// <summary>
        /// Bit 0: Attack (LMB), Bit 1: Parry (R), Bits 2-3: movement profile 0-3
        /// </summary>
        public byte ActionFlags;

        /// <summary>
        /// 14 bytes
        /// </summary>
        public const int Size = 4 + 1 + 4 + 4 + 1;

        public void Serialize(Span<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException("Buffer too small");

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(0, 4), TickNumber);
            buffer[4] = MovementFlags;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(5, 4), BitConverter.SingleToInt32Bits(MouseWorldX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(9, 4), BitConverter.SingleToInt32Bits(MouseWorldY));
            buffer[13] = ActionFlags;
        }

        public static ClientInputPacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException("Buffer too small");

            var packet = new ClientInputPacket();
            packet.TickNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
            packet.MovementFlags = buffer[4];
            packet.MouseWorldX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(5, 4)));
            packet.MouseWorldY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(9, 4)));
            packet.ActionFlags = buffer[13];
            return packet;
        }
    }
}
