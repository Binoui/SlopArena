using System;
using System.Buffers.Binary;

namespace SlopArena.Shared
{
    public struct CharacterStatePacket
    {
        public uint TickNumber;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float VelocityX;
        public float VelocityY;
        public float VelocityZ;
        public byte CurrentActionState;      // Idle, Dashing, Hitstun, WallCling, Sliding
        public ushort StateDurationFrames;   // Number of physics frames remaining in this state

        public const int Size = 4 + 4 + 4 + 4 + 4 + 4 + 4 + 1 + 2; // 31 bytes

        public void Serialize(Span<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException("Buffer too small");

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(0, 4), TickNumber);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(4, 4), PositionX);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(8, 4), PositionY);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(12, 4), PositionZ);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(16, 4), VelocityX);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(20, 4), VelocityY);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(24, 4), VelocityZ);
            buffer[28] = CurrentActionState;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(29, 2), StateDurationFrames);
        }

        public static CharacterStatePacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException("Buffer too small");

            var packet = new CharacterStatePacket();
            packet.TickNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
            packet.PositionX = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(4, 4));
            packet.PositionY = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(8, 4));
            packet.PositionZ = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(12, 4));
            packet.VelocityX = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(16, 4));
            packet.VelocityY = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(20, 4));
            packet.VelocityZ = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(24, 4));
            packet.CurrentActionState = buffer[28];
            packet.StateDurationFrames = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(29, 2));
            return packet;
        }
    }
}
