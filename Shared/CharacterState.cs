namespace SlopArena.Shared
{
    /// <summary>
    /// Complete state of a character at a single tick.
    /// Pure C# — no Godot types. Used by both client prediction
    /// and server authoritative simulation.
    /// All durations are in ticks (1/60s at 60Hz).
    /// </summary>
    public struct CharacterState
    {
        /// <summary>
        /// ── Position & Velocity ──
        /// </summary>
        public float PX, PY, PZ;
        public float VX, VY, VZ;

        /// <summary>
        /// ── Action state machine ──
        /// </summary>
        public ActionState State;
        /// <summary>
        /// remaining ticks in current state
        /// </summary>
        public ushort StateTicks;

        /// <summary>
        /// ── Resources ──
        /// </summary>
        public ushort DamagePercent;    // 0-999, Smash-style % (increases when hit, knockback scales with it)
        public byte JumpsLeft;
        public byte AirDodgesLeft;
        public bool IsGrounded;
        public bool WasAirborneDuringKnockback;

        /// <summary>
        /// ── Dash ──
        /// </summary>
        public ushort DashCooldownTicks;
        /// <summary>
        /// remaining dash ticks
        /// </summary>
        public ushort DashDurationTicks;
        public float DashDirX, DashDirZ;

        /// <summary>
        /// ── Invincibility (dash, respawn) ──
        /// </summary>
        public ushort InvincibilityTicks; // remaining ticks of invincibility

        /// <summary>
        /// ── Combo / Attack ──
        /// </summary>
        public byte ComboStage;          // 0 = none, 1-3 = stage
        /// <summary>
        /// chain window remaining
        /// </summary>
        public ushort ComboTimerTicks;
        /// <summary>
        /// self-lock from attack (remaining)
        /// </summary>
        public ushort AnimLockTicks;
        /// <summary>
        /// buffered LMB chains (max 2, for spam)
        /// </summary>
        public byte BufferedChain;
        /// <summary>
        /// ticks holding RMB
        /// </summary>
        public ushort HeavyHoldTicks;
        /// <summary>
        /// true when hold threshold reached
        /// </summary>
        public bool HeavyCharged;
        /// <summary>
        /// aimed charge progress (0 = none, >0 = charging)
        /// </summary>
        public ushort ChargeTicks;

        /// <summary>
        /// ── Knockback ──
        /// </summary>
        public float KVX, KVY, KVZ;     // knockback velocity (decays separately)

        /// <summary>
        /// ── Hitstun + DI (Directional Influence) ──
        /// </summary>
        public ushort HitstunTicks;     // frames frozen before knockback starts
        /// <summary>
        /// accumulated DI input during hitstun
        /// </summary>
        public float DIX, DIY;

        /// <summary>
        /// ── Facing ──
        /// </summary>
        public float FacingYaw;          // radians, +Z = 0

        /// <summary>
        /// ── Sprint / Dash-dance ──
        /// </summary>
        public ushort DirHoldTicks;      // ticks holding same direction
        public bool IsSprinting;
        /// <summary>
        /// turnaround lag remaining
        /// </summary>
        public ushort TurnaroundTicks;

        /// <summary>
        /// ── Last input direction (for tech roll, air dodge fallback) ──
        /// </summary>
        public float LastDirX, LastDirZ;

        /// <summary>
        /// ── Entity ID (0 = unassigned) ──
        /// </summary>
        public ulong EntityId;

        /// <summary>
        /// ── Per-slot cooldowns (0-5) ──
        /// </summary>
        public ushort Cooldown0, Cooldown1, Cooldown2, Cooldown3, Cooldown4, Cooldown5;
    }
}
