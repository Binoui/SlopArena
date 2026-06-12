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
        public bool Jump, Dash, Crouch, Attack;
        public float MoveX, MoveY;
        /// <summary>
        /// 0 = none, 1 = LMB, 2 = RMB, 3 = Q, 4 = E, 5 = R, 6 = F
        /// </summary>
        public byte ActiveSlot;

        /// <summary>8 bytes (2 floats + 1 byte flags + 1 byte slot)</summary>
        public const int Size = 8 + 1 + 1;

        public void Write(Span<byte> buf)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buf, MoveX);
            BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(4), MoveY);
            byte flags = 0;
            if (Up) flags |= 1;
            if (Down) flags |= 2;
            if (Left) flags |= 4;
            if (Right) flags |= 8;
            if (Jump) flags |= 0x10;
            if (Dash) flags |= 0x20;
            if (Crouch) flags |= 0x40;
            if (Attack) flags |= 0x80;
            buf[8] = flags;
            buf[9] = ActiveSlot;
        }

        public static InputState Deserialize(ReadOnlySpan<byte> buf)
        {
            var input = new InputState
            {
                MoveX = BinaryPrimitives.ReadSingleLittleEndian(buf),
                MoveY = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(4)),
            };
            byte flags = buf[8];
            input.Up = (flags & 1) != 0;
            input.Down = (flags & 2) != 0;
            input.Left = (flags & 4) != 0;
            input.Right = (flags & 8) != 0;
            input.Jump = (flags & 0x10) != 0;
            input.Dash = (flags & 0x20) != 0;
            input.Crouch = (flags & 0x40) != 0;
            input.Attack = (flags & 0x80) != 0;
            input.ActiveSlot = buf[9];
            return input;
        }
    }
}
