using System;
using SlopArena.Shared

namespace SlopArena.Shared
{
    public struct ClientInputPacket
    {
        public uint TickNumber;
        public byte MovementFlags; // Bit 0: Z, Bit 1: Q, Bit 2: S, Bit 3: D, Bit 4: Space (Jump), Bit 5: Shift (Dash), Bit 6: E (Respawn), Bit 7: Ctrl/C (Crouch)
        public float MouseWorldX;
        public float MouseWorldY;
using SlopArena.Shared

using SlopArena.Shared

        public void Serialize(Span<byte> buffer)
        {
            if (buffer.Length < Size) 
using SlopArena.Shared

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(0, 4), TickNumber);
            buffer[4] = MovementFlags;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(5, 4), MouseWorldX);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(9, 4), MouseWorldY);
            buffer[13] = ActionFlags;
using SlopArena.Shared

        public static ClientInputPacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size) 
using SlopArena.Shared

            var packet = new ClientInputPacket();
            packet.TickNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
            packet.MovementFlags = buffer[4];
            packet.MouseWorldX = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(5, 4));
            packet.MouseWorldY = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(9, 4));
            packet.ActionFlags = buffer[13];
            return packet;
        }
    }
using SlopArena.Shared
