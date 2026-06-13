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
        /// <summary>
        /// Idle, Dashing, Hitstun, WallCling, Sliding
        /// </summary>
        public byte CurrentActionState;
        /// <summary>
        /// Number of physics frames remaining in this state
        /// </summary>
        public ushort StateDurationFrames;

        /// <summary>IsGrounded flag from server.</summary>
        public bool IsGrounded;

        /// <summary>Attack slot (1-6) for animation selection on client/ghost.</summary>
        public byte AttackSlot;
        /// <summary>Combo stage index for animation selection.</summary>
        public byte ComboStage;
        /// <summary>Facing yaw in radians, from server authority.</summary>
        public float FacingYaw;

        /// <summary>38 bytes</summary>
        public const int Size = 4 + 4 + 4 + 4 + 4 + 4 + 4 + 1 + 1 + 2 + 1 + 1 + 4;

        /// <summary>Convert from CharacterState to serializable packet.</summary>
        public static CharacterStatePacket FromState(CharacterState s, uint tick = 0)
        {
            return new CharacterStatePacket
            {
                TickNumber = tick,
                PositionX = s.PX,
                PositionY = s.PY,
                PositionZ = s.PZ,
                VelocityX = s.VX,
                VelocityY = s.VY,
                VelocityZ = s.VZ,
                CurrentActionState = (byte)s.State,
                IsGrounded = s.IsGrounded,
                StateDurationFrames = s.StateTicks,
                AttackSlot = s.AttackSlot,
                ComboStage = s.ComboStage,
                FacingYaw = s.FacingYaw,
            };
        }

        /// <summary>Convert back to CharacterState.</summary>
        public CharacterState ToState()
        {
            return new CharacterState
            {
                PX = PositionX,
                PY = PositionY,
                PZ = PositionZ,
                VX = VelocityX,
                VY = VelocityY,
                VZ = VelocityZ,
                State = (ActionState)CurrentActionState,
                IsGrounded = IsGrounded,
                StateTicks = StateDurationFrames,
                AttackSlot = AttackSlot,
                ComboStage = ComboStage,
                FacingYaw = FacingYaw,
            };
        }

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
            buffer[29] = IsGrounded ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(30, 2), StateDurationFrames);
            buffer[32] = AttackSlot;
            buffer[33] = ComboStage;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(34, 4), FacingYaw);
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
            packet.IsGrounded = buffer[29] != 0;
            packet.StateDurationFrames = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(30, 2));
            packet.AttackSlot = buffer[32];
            packet.ComboStage = buffer[33];
            packet.FacingYaw = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(34, 4));
            return packet;
        }
    }
}
