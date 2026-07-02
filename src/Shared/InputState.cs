using System;
using System.Buffers.Binary;

namespace SlopArena.Shared
{
    /// <summary>
    /// Input state for one tick of simulation.
    /// Pure C# — no Godot types. Matches what ClientInputPacket carries.
    /// </summary>
    public struct InputState
    {
        public bool Up, Down, Left, Right;
        public bool Jump, Dash, Crouch;
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
        /// <summary>Client's selected target entity ID (0=none). Computed from screen-center proximity.</summary>
        public byte TargetEntityId;

        /// <summary>Warp target position (local-only, not networked).</summary>
        public float WarpTargetX, WarpTargetZ;
        public float WarpSpeed;
        public float WarpAttackRange;

        /// <summary>17 bytes (2 floats + 1 flags + 1 slot + 2 facing + 2 aim + 2 distance + 1 target)</summary>
        public const int Size = 8 + 1 + 1 + 2 + 2 + 2 + 1;

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
            if (Crouch) flags |= 0x40;
            if (IsAiming) flags |= 0x80;
            buf[8] = flags;
            buf[9] = ActiveSlot;
            BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(10), FacingYaw);
            BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(12), AimYaw);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(14), AimDistance);
            buf[16] = TargetEntityId;
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
            input.Crouch = (flags & 0x40) != 0;
            input.IsAiming = (flags & 0x80) != 0;
            input.ActiveSlot = buf[9];
            input.FacingYaw = buf.Length >= 12 ? BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(10)) : (short)0;
            input.AimYaw = buf.Length >= 14 ? BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(12)) : (short)0;
            input.AimDistance = buf.Length >= 16 ? BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(14)) : (ushort)0;
            input.TargetEntityId = buf.Length >= 17 ? buf[16] : (byte)0;
            return input;
        }
    }
}
