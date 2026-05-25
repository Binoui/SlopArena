using System;
using System.Buffers.Binary;

namespace MoveBox.Shared
{
    public struct ClientInputPacket
    {
        public uint TickNumber;
        public byte MovementFlags; // Bit 0: Z, Bit 1: Q, Bit 2: S, Bit 3: D, Bit 4: Space (Jump), Bit 5: Shift (Dash), Bit 6: E (Respawn), Bit 7: Ctrl/C (Crouch)
        public float MouseWorldX;
        public float MouseWorldY;
        public byte ActionFlags;   // Bit 0: Attack (LMB), Bit 1: Parry (R), Bits 2-3: movement profile 0-3

        public const int Size = 4 + 1 + 4 + 4 + 1; // 14 bytes

        public void Serialize(Span<byte> buffer)
        {
            if (buffer.Length < Size) 
                throw new ArgumentException("Buffer too small");
                
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(0, 4), TickNumber);
            buffer[4] = MovementFlags;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(5, 4), MouseWorldX);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(9, 4), MouseWorldY);
            buffer[13] = ActionFlags;
        }

        public static ClientInputPacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size) 
                throw new ArgumentException("Buffer too small");
                
            var packet = new ClientInputPacket();
            packet.TickNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
            packet.MovementFlags = buffer[4];
            packet.MouseWorldX = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(5, 4));
            packet.MouseWorldY = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(9, 4));
            packet.ActionFlags = buffer[13];
            return packet;
        }
    }
}
