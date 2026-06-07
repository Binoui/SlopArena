using System;

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
        // ── Position & Velocity ──
        public float PX, PY, PZ;
        public float VX, VY, VZ;

        // ── Action state machine ──
        public ActionState State;
        public ushort StateTicks;       // remaining ticks in current state

        // ── Resources ──
        public ushort DamagePercent;    // 0-999, Smash-style % (increases when hit, knockback scales with it)
        public byte JumpsLeft;
        public byte AirDodgesLeft;
        public bool IsGrounded;
        public bool WasAirborneDuringKnockback;

        // ── Dash ──
        public ushort DashCooldownTicks;
        public ushort DashDurationTicks; // remaining dash ticks
        public float DashDirX, DashDirZ;

        // ── Invincibility (dash, respawn) ──
        public ushort InvincibilityTicks; // remaining ticks of invincibility

        // ── Combo / Attack ──
        public byte ComboStage;          // 0 = none, 1-3 = stage
        public ushort ComboTimerTicks;   // chain window remaining
        public ushort AnimLockTicks;     // self-lock from attack (remaining)
        public byte BufferedChain;       // buffered LMB chains (max 2, for spam)
        public ushort HeavyHoldTicks;    // ticks holding RMB
        public bool HeavyCharged;        // true when hold threshold reached

        // ── Knockback ──
        public float KVX, KVY, KVZ;     // knockback velocity (decays separately)

        // ── Facing ──
        public float FacingYaw;          // radians, +Z = 0

        // ── Sprint / Dash-dance ──
        public ushort DirHoldTicks;      // ticks holding same direction
        public bool IsSprinting;
        public ushort TurnaroundTicks;   // turnaround lag remaining

        // ── Last input direction (for tech roll, air dodge fallback) ──
        public float LastDirX, LastDirZ;

        // ── Entity ID (0 = unassigned) ──
        public ulong EntityId;

        // ── Per-slot cooldowns (0-5) ──
        public ushort Cooldown0, Cooldown1, Cooldown2, Cooldown3, Cooldown4, Cooldown5;
    }
}
