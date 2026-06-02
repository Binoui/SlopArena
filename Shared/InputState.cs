namespace SlopArena.Shared
{
    /// <summary>
    /// Input state for one tick of simulation.
    /// Pure C# — no Godot types. Matches what ClientInputPacket carries.
    /// </summary>
    public struct InputState
    {
        /// <summary>Forward (W/Z) pressed</summary>
        public bool Up;
        /// <summary>Backward (S) pressed</summary>
        public bool Down;
        /// <summary>Left (Q/A) pressed</summary>
        public bool Left;
        /// <summary>Right (D) pressed</summary>
        public bool Right;
        /// <summary>Jump (Space) pressed</summary>
        public bool Jump;
        /// <summary>Dash (Shift) just pressed (edge-triggered for air dodge)</summary>
        public bool Dash;
        /// <summary>Crouch (C) pressed</summary>
        public bool Crouch;
        /// <summary>Attack (LMB) pressed</summary>
        public bool Attack;
        /// <summary>Normalized horizontal input X (-1 to 1)</summary>
        public float MoveX;
        /// <summary>Normalized vertical input Y (-1 to 1)</summary>
        public float MoveY;
    }
}
