using System;
using System.Buffers.Binary;

namespace SlopArena.Shared
{
    /// <summary>
    /// Input state for one tick of simulation.
    /// Pure C# — no Godot types; this is the serialized input payload.
    /// </summary>
    public struct InputState
    {
        public bool Up, Down, Left, Right;
        public bool Jump, Dash, Burst;
        /// <summary>
        /// True while the jump key is physically held (issue #116 / #106). The sim counts
        /// consecutive held ticks (<c>CharacterState.JumpHeldTicks</c>) and releases within
        /// <c>Simulation.ShortHopWindowTicks</c> produce a reduced short hop.
        /// </summary>
        public bool JumpHeld;
        /// <summary>
        /// LMB facing snap (ADR-0017, issue #126): one-tick edge set on the LMB press —
        /// the sim snaps <c>FacingYaw</c> to the camera azimuth (<c>AimYaw</c>) when the
        /// input gate allows, and exits a persistent target lock (ADR-0018) when accepted.
        /// </summary>
        public bool FaceToCamera;
        /// <summary>
        /// RMB target-lock toggle (ADR-0018, issue #127): one-tick edge set on the RMB
        /// press. The sim toggles sim-authoritative <c>CharacterState.LockOn</c>.
        /// </summary>
        public bool ToggleLock;
        public float MoveX, MoveY;
        /// <summary>
        /// 0 = none, 1 = LMB, 2 = RMB, 3 = Q, 4 = E, 5 = R, 6 = F
        /// </summary>
        public byte ActiveSlot;
        /// <summary>True while holding an aim-to-fire ability (RMB charge, Q throw).</summary>
        public bool IsAiming;
        public short FacingYaw;
        /// <summary>Aim yaw in degrees × 100 (short, -18000 to 18000). Sent by client, overrides FacingYaw for combat.</summary>
        public short AimYaw;
        /// <summary>Aim distance in cm (ushort, 0-6500, i.e. 0-65m). Set by client during targeted-aiming state.</summary>
        public ushort AimDistance;
        /// <summary>Aim pitch in degrees × 100 (short, -9000 to 9000). Camera-relative vertical aim.</summary>
        public short AimPitch;

        /// <summary>Client's selected target entity ID (0=none). Computed from screen-center proximity.</summary>
        public byte TargetEntityId;

        /// <summary>Warp target position (local-only, not networked).</summary>
        public float WarpTargetX, WarpTargetZ;
        public float WarpSpeed;
        public float WarpAttackRange;

        /// <summary>20 bytes (2 floats + 1 flags + 1 slot + 2 facing + 2 aim + 2 pitch + 2 distance + 1 target + 1 flags2)</summary>
        /// <remarks>
        /// Flags byte (byte 8): 1=Up, 2=Down, 4=Left, 8=Right, 0x10=Jump, 0x20=Dash,
        /// 0x40=Burst (ADR-0014; formerly Crouch, deprecated), 0x80=IsAiming.
        /// Flags2 byte (byte 19): 1=JumpHeld (ADR-0016 short hop, issue #116),
        /// 2=FaceToCamera (ADR-0017 LMB facing snap, issue #126), 4=ToggleLock
        /// (ADR-0018 RMB target-lock toggle, issue #127).
        /// </remarks>
        public const int Size = 8 + 1 + 1 + 2 + 2 + 2 + 2 + 1 + 1;

        public void Write(Span<byte> buf)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf, BitConverter.SingleToInt32Bits(MoveX));
            BinaryPrimitives.WriteInt32LittleEndian(buf.Slice(4), BitConverter.SingleToInt32Bits(MoveY));
            byte flags = 0;
            if (Up) flags |= 1;
            if (Down) flags |= 2;
            if (Left) flags |= 4;
            if (Right) flags |= 8;
            if (Jump) flags |= 0x10;
            if (Dash) flags |= 0x20;
            if (Burst) flags |= 0x40;
            if (IsAiming) flags |= 0x80;
            buf[8] = flags;
            buf[9] = ActiveSlot;
            BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(10), FacingYaw);
            BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(12), AimYaw);
            BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(14), AimPitch);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(16), AimDistance);
            buf[18] = TargetEntityId;
            byte flags2 = 0;
            if (JumpHeld) flags2 |= 1;
            if (FaceToCamera) flags2 |= 2;
            if (ToggleLock) flags2 |= 4;
            buf[19] = flags2;
        }

        public static InputState Deserialize(ReadOnlySpan<byte> buf)
        {
            var input = new InputState
            {
                MoveX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf)),
                MoveY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(4))),
            };
            byte flags = buf[8];
            input.Up = (flags & 1) != 0;
            input.Down = (flags & 2) != 0;
            input.Left = (flags & 4) != 0;
            input.Right = (flags & 8) != 0;
            input.Jump = (flags & 0x10) != 0;
            input.Dash = (flags & 0x20) != 0;
            input.Burst = (flags & 0x40) != 0;
            input.IsAiming = (flags & 0x80) != 0;
            input.ActiveSlot = buf[9];
            input.FacingYaw = buf.Length >= 12 ? BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(10)) : (short)0;
            input.AimYaw = buf.Length >= 14 ? BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(12)) : (short)0;
            input.AimPitch = buf.Length >= 16 ? BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(14)) : (short)0;
            input.AimDistance = buf.Length >= 18 ? BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(16)) : (ushort)0;
            input.TargetEntityId = buf.Length >= 19 ? buf[18] : (byte)0;
            if (buf.Length >= 20)
            {
                input.JumpHeld = (buf[19] & 1) != 0;
                input.FaceToCamera = (buf[19] & 2) != 0;
                input.ToggleLock = (buf[19] & 4) != 0;
            }
            return input;
        }
    }
}
