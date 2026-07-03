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
        /// <summary>Animation index into the ability's AnimationNames[] (set by server ability class).</summary>
        public byte AnimIndex;
        /// <summary>Facing yaw in radians, from server authority.</summary>
        public float FacingYaw;

        /// <summary>Match lifecycle state from server.</summary>
        public MatchState MatchState;
        /// <summary>Buff timer remaining (0 = no active buff). Set by Overclock etc.</summary>
        public ushort BuffRemainingTicks;
        /// <summary>Active buff flags bitfield (see BuffType enum).</summary>
        public byte BuffActiveFlags;
        /// <summary>Hitstun animation tier: 0=small, 1=medium, 2=hard.</summary>
        public byte HitstunLevel;
        /// <summary>44 bytes</summary>
        public const int Size = 4 + 4 + 4 + 4 + 4 + 4 + 4 + 1 + 1 + 2 + 1 + 1 + 1 + 4 + 1 + 2 + 1 + 1;

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
                AnimIndex = s.AnimIndex,
                MatchState = s.MatchState,
                BuffRemainingTicks = s.BuffRemainingTicks,
                BuffActiveFlags = s.BuffActiveFlags,
                HitstunLevel = s.HitstunLevel,
            };
        }

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
                AnimIndex = AnimIndex,
                MatchState = MatchState,
                BuffRemainingTicks = BuffRemainingTicks,
                BuffActiveFlags = BuffActiveFlags,
                HitstunLevel = HitstunLevel,
            };
        }

        public void Serialize(Span<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException("Buffer too small");

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(0, 4), TickNumber);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), BitConverter.SingleToInt32Bits(PositionX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8, 4), BitConverter.SingleToInt32Bits(PositionY));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(12, 4), BitConverter.SingleToInt32Bits(PositionZ));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(16, 4), BitConverter.SingleToInt32Bits(VelocityX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20, 4), BitConverter.SingleToInt32Bits(VelocityY));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(24, 4), BitConverter.SingleToInt32Bits(VelocityZ));
            buffer[28] = CurrentActionState;
            buffer[29] = IsGrounded ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(30, 2), StateDurationFrames);
            buffer[32] = AttackSlot;
            buffer[33] = ComboStage;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(34, 4), BitConverter.SingleToInt32Bits(FacingYaw));
            buffer[38] = (byte)MatchState;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(40, 2), BuffRemainingTicks);
            buffer[42] = BuffActiveFlags;
            buffer[43] = HitstunLevel;
        }

        public static CharacterStatePacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException("Buffer too small");

            var packet = new CharacterStatePacket();
            packet.TickNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
            packet.PositionX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(4, 4)));
            packet.PositionY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(8, 4)));
            packet.PositionZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(12, 4)));
            packet.VelocityX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(16, 4)));
            packet.VelocityY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(20, 4)));
            packet.VelocityZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(24, 4)));
            packet.CurrentActionState = buffer[28];
            packet.IsGrounded = buffer[29] != 0;
            packet.StateDurationFrames = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(30, 2));
            packet.AttackSlot = buffer[32];
            packet.ComboStage = buffer[33];
            packet.FacingYaw = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(34, 4)));
            packet.MatchState = (MatchState)buffer[38];
            packet.BuffRemainingTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(40, 2));
            packet.BuffActiveFlags = buffer[42];
            packet.HitstunLevel = buffer[43];
            return packet;
        }
    }
}
