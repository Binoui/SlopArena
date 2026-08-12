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
        /// <summary>Aim pitch in radians, from server authority.</summary>
        public float AimPitch;
        /// <summary>Match death counter (stock counter: stocks = maxStocks - Deaths). Issue #37.</summary>
        public byte Deaths;
        /// <summary>Smash-style damage percent 0-999, sent so the client HUD can show every player's %. Issue #38.</summary>
        public ushort DamagePercent;
        /// <summary>Per-slot cooldown ticks (0-10), sent so the local player's HUD cooldown fills work in PvP. Issue #38, ADR-0016.</summary>
        public ushort Cooldown0, Cooldown1, Cooldown2, Cooldown3, Cooldown4, Cooldown5,
            Cooldown6, Cooldown7, Cooldown8, Cooldown9, Cooldown10;
        /// <summary>Consecutive jump-held ticks (issue #116) — needed for byte-identical replay of a JumpSquat opponent.</summary>
        public byte JumpHeldTicks;
        /// <summary>Persistent target lock state (ADR-0018, issue #127) — client lock indicator.</summary>
        public bool LockOn;
        // ── D10: movement-resource fields (ADR-0011) — needed for PredictedTrack's
        // rebuild-and-replay of Predictable ActionStates (Idle/Dashing/JumpSquat/AirDodging)
        // to be byte-identical. None of these touch the ability-instance or hitbox layer.
        public ushort AirTimeTicks;
        public ushort DashDurationTicks;
        public float DashDirX, DashDirZ;
        public ushort DashCooldownTicks;
        public byte AirDodgesLeft;
        public byte JumpsLeft;
        public ushort InvincibilityTicks;
        public ushort TurnaroundTicks;
        public ushort DirHoldTicks;
        public bool IsSprinting;
        public float LastDirX, LastDirZ;
        public bool WasAirborneDuringKnockback;
        /// <summary>Remaining hitstop freeze ticks (ADR-0012).</summary>
        public ushort HitstopTicks;
        /// <summary>Remaining Burst cooldown ticks (ADR-0014) — HUD for both players.</summary>
        public ushort BurstCooldownTicks;
        /// <summary>Remaining Burst recovery lock ticks (ADR-0014) — opponent's punish window must be visible.</summary>
        public ushort BurstRecoveryTicks;

        /// <summary>113 bytes — 63 base + 12 cooldowns×6→11 (ADR-0016, +10) + 32 D10 movement-resource fields + 2 hitstop (ADR-0012) + 4 burst (ADR-0014) + 1 JumpHeldTicks (ADR-0016) + 1 LockOn (ADR-0018).</summary>
        public const int Size = 4 + 4 + 4 + 4 + 4 + 4 + 4 + 1 + 1 + 2 + 1 + 1 + 1 + 4 + 1 + 2 + 1 + 1 + 4 + 1 + 2 + 2 + 2 + 2 + 2 + 2 + 2
            + 2 + 2 + 2 + 2 + 2 + 2 + 2 + 4 + 4 + 2 + 1 + 1 + 2 + 2 + 2 + 1 + 4 + 4 + 1 + 2 + 2 + 2 + 1 + 1;

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
                AimPitch = s.AimPitch,
                Deaths = s.Deaths,
                DamagePercent = s.DamagePercent,
                Cooldown0 = s.Cooldown0,
                Cooldown1 = s.Cooldown1,
                Cooldown2 = s.Cooldown2,
                Cooldown3 = s.Cooldown3,
                Cooldown4 = s.Cooldown4,
                Cooldown5 = s.Cooldown5,
                Cooldown6 = s.Cooldown6,
                Cooldown7 = s.Cooldown7,
                Cooldown8 = s.Cooldown8,
                Cooldown9 = s.Cooldown9,
                Cooldown10 = s.Cooldown10,
                JumpHeldTicks = s.JumpHeldTicks,
                LockOn = s.LockOn,
                AirTimeTicks = s.AirTimeTicks,
                DashDurationTicks = s.DashDurationTicks,
                DashDirX = s.DashDirX,
                DashDirZ = s.DashDirZ,
                DashCooldownTicks = s.DashCooldownTicks,
                AirDodgesLeft = s.AirDodgesLeft,
                JumpsLeft = s.JumpsLeft,
                InvincibilityTicks = s.InvincibilityTicks,
                TurnaroundTicks = s.TurnaroundTicks,
                DirHoldTicks = s.DirHoldTicks,
                IsSprinting = s.IsSprinting,
                LastDirX = s.LastDirX,
                LastDirZ = s.LastDirZ,
                WasAirborneDuringKnockback = s.WasAirborneDuringKnockback,
                HitstopTicks = s.HitstopTicks,
                BurstCooldownTicks = s.BurstCooldownTicks,
                BurstRecoveryTicks = s.BurstRecoveryTicks,
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
                AimPitch = AimPitch,
                Deaths = Deaths,
                DamagePercent = DamagePercent,
                Cooldown0 = Cooldown0,
                Cooldown1 = Cooldown1,
                Cooldown2 = Cooldown2,
                Cooldown3 = Cooldown3,
                Cooldown4 = Cooldown4,
                Cooldown5 = Cooldown5,
                Cooldown6 = Cooldown6,
                Cooldown7 = Cooldown7,
                Cooldown8 = Cooldown8,
                Cooldown9 = Cooldown9,
                Cooldown10 = Cooldown10,
                JumpHeldTicks = JumpHeldTicks,
                LockOn = LockOn,
                AirTimeTicks = AirTimeTicks,
                DashDurationTicks = DashDurationTicks,
                DashDirX = DashDirX,
                DashDirZ = DashDirZ,
                DashCooldownTicks = DashCooldownTicks,
                AirDodgesLeft = AirDodgesLeft,
                JumpsLeft = JumpsLeft,
                InvincibilityTicks = InvincibilityTicks,
                TurnaroundTicks = TurnaroundTicks,
                DirHoldTicks = DirHoldTicks,
                IsSprinting = IsSprinting,
                LastDirX = LastDirX,
                LastDirZ = LastDirZ,
                WasAirborneDuringKnockback = WasAirborneDuringKnockback,
                HitstopTicks = HitstopTicks,
                BurstCooldownTicks = BurstCooldownTicks,
                BurstRecoveryTicks = BurstRecoveryTicks,
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
            buffer[39] = AnimIndex;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(40, 2), BuffRemainingTicks);
            buffer[42] = BuffActiveFlags;
            buffer[43] = HitstunLevel;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(44, 4), BitConverter.SingleToInt32Bits(AimPitch));
            buffer[48] = Deaths;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(49, 2), DamagePercent);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(51, 2), Cooldown0);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(53, 2), Cooldown1);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(55, 2), Cooldown2);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(57, 2), Cooldown3);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(59, 2), Cooldown4);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(61, 2), Cooldown5);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(63, 2), Cooldown6);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(65, 2), Cooldown7);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(67, 2), Cooldown8);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(69, 2), Cooldown9);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(71, 2), Cooldown10);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(73, 2), AirTimeTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(75, 2), DashDurationTicks);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(77, 4), BitConverter.SingleToInt32Bits(DashDirX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(81, 4), BitConverter.SingleToInt32Bits(DashDirZ));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(85, 2), DashCooldownTicks);
            buffer[87] = AirDodgesLeft;
            buffer[88] = JumpsLeft;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(89, 2), InvincibilityTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(91, 2), TurnaroundTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(93, 2), DirHoldTicks);
            buffer[95] = IsSprinting ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(96, 4), BitConverter.SingleToInt32Bits(LastDirX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(100, 4), BitConverter.SingleToInt32Bits(LastDirZ));
            buffer[104] = WasAirborneDuringKnockback ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(105, 2), HitstopTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(107, 2), BurstCooldownTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(109, 2), BurstRecoveryTicks);
            buffer[111] = JumpHeldTicks;
            buffer[112] = LockOn ? (byte)1 : (byte)0;
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
            packet.AnimIndex = buffer[39];
            packet.BuffRemainingTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(40, 2));
            packet.BuffActiveFlags = buffer[42];
            packet.HitstunLevel = buffer[43];
            packet.AimPitch = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(44, 4)));
            packet.Deaths = buffer[48];
            packet.DamagePercent = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(49, 2));
            packet.Cooldown0 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(51, 2));
            packet.Cooldown1 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(53, 2));
            packet.Cooldown2 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(55, 2));
            packet.Cooldown3 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(57, 2));
            packet.Cooldown4 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(59, 2));
            packet.Cooldown5 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(61, 2));
            packet.Cooldown6 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(63, 2));
            packet.Cooldown7 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(65, 2));
            packet.Cooldown8 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(67, 2));
            packet.Cooldown9 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(69, 2));
            packet.Cooldown10 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(71, 2));
            packet.AirTimeTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(73, 2));
            packet.DashDurationTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(75, 2));
            packet.DashDirX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(77, 4)));
            packet.DashDirZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(81, 4)));
            packet.DashCooldownTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(85, 2));
            packet.AirDodgesLeft = buffer[87];
            packet.JumpsLeft = buffer[88];
            packet.InvincibilityTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(89, 2));
            packet.TurnaroundTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(91, 2));
            packet.DirHoldTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(93, 2));
            packet.IsSprinting = buffer[95] != 0;
            packet.LastDirX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(96, 4)));
            packet.LastDirZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(100, 4)));
            packet.WasAirborneDuringKnockback = buffer[104] != 0;
            packet.HitstopTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(105, 2));
            packet.BurstCooldownTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(107, 2));
            packet.BurstRecoveryTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(109, 2));
            packet.JumpHeldTicks = buffer[111];
            packet.LockOn = buffer[112] != 0;
            return packet;
        }

        /// <summary>
        /// Overwrite only the fields this packet carries on an existing CharacterState,
        /// in place. Unlike ToState() (which builds a fresh CharacterState and leaves every
        /// non-wire field at its default), this preserves everything ApplyTo doesn't touch —
        /// used by LocalTrack (ADR-0011), which must patch its own full-fidelity self state
        /// with the server's authoritative wire fields without clobbering fields the wire
        /// doesn't carry (e.g. AttackElapsedTicks, knockback velocity).
        /// </summary>
        public void ApplyTo(ref CharacterState s)
        {
            s.PX = PositionX; s.PY = PositionY; s.PZ = PositionZ;
            s.VX = VelocityX; s.VY = VelocityY; s.VZ = VelocityZ;
            s.State = (ActionState)CurrentActionState;
            s.IsGrounded = IsGrounded;
            s.StateTicks = StateDurationFrames;
            s.AttackSlot = AttackSlot;
            s.ComboStage = ComboStage;
            s.AnimIndex = AnimIndex;
            s.FacingYaw = FacingYaw;
            s.MatchState = MatchState;
            s.BuffRemainingTicks = BuffRemainingTicks;
            s.BuffActiveFlags = BuffActiveFlags;
            s.HitstunLevel = HitstunLevel;
            s.AimPitch = AimPitch;
            s.Deaths = Deaths;
            s.DamagePercent = DamagePercent;
            s.Cooldown0 = Cooldown0; s.Cooldown1 = Cooldown1; s.Cooldown2 = Cooldown2;
            s.Cooldown3 = Cooldown3; s.Cooldown4 = Cooldown4; s.Cooldown5 = Cooldown5;
            s.Cooldown6 = Cooldown6; s.Cooldown7 = Cooldown7; s.Cooldown8 = Cooldown8;
            s.Cooldown9 = Cooldown9; s.Cooldown10 = Cooldown10;
            s.JumpHeldTicks = JumpHeldTicks;
            s.LockOn = LockOn;
            s.AirTimeTicks = AirTimeTicks;
            s.DashDurationTicks = DashDurationTicks;
            s.DashDirX = DashDirX; s.DashDirZ = DashDirZ;
            s.DashCooldownTicks = DashCooldownTicks;
            s.AirDodgesLeft = AirDodgesLeft;
            s.JumpsLeft = JumpsLeft;
            s.InvincibilityTicks = InvincibilityTicks;
            s.TurnaroundTicks = TurnaroundTicks;
            s.DirHoldTicks = DirHoldTicks;
            s.IsSprinting = IsSprinting;
            s.LastDirX = LastDirX; s.LastDirZ = LastDirZ;
            s.WasAirborneDuringKnockback = WasAirborneDuringKnockback;
            s.HitstopTicks = HitstopTicks;
            s.BurstCooldownTicks = BurstCooldownTicks;
            s.BurstRecoveryTicks = BurstRecoveryTicks;
        }
    }
}
