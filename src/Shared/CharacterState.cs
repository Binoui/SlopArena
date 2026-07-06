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
        /// <summary>Match lifecycle state (Waiting, Countdown, Playing, Ended).</summary>
        public MatchState MatchState;

        /// <summary>
        /// ── Resources ──
        /// </summary>
        public ushort DamagePercent;    // 0-999, Smash-style % (increases when hit, knockback scales with it)
        public byte JumpsLeft;
        public byte AirDodgesLeft;
        public bool IsGrounded;
        public bool WasAirborneDuringKnockback;
        public byte Deaths;              // match death counter, server authority

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
        public ushort AttackElapsedTicks;  // elapsed ticks since attack start
        /// <summary>Which ability slot this attack uses (1-6). 0 = none.</summary>
        public byte AttackSlot;
        /// <summary>Buffered input slot (general buffer during any lock). 0 = none.</summary>
        public byte BufferedSlot;
        /// <summary>
        /// 0 = none, 1-3 = stage
        /// </summary>
        public byte ComboStage;
        /// <summary>Animation index (into spec's AnimationNames[]) set by server ability. Synced to client.</summary>
        public byte AnimIndex;
        /// <summary>True when a server-side ability class is active (skip old data-driven attack processing).</summary>
        public bool IsServerAbility;
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
        /// <summary>Hitstun animation tier: 0=small, 1=medium, 2=hard. Set at hit time.</summary>
        public byte HitstunLevel;
        /// <summary>
        /// accumulated DI input during hitstun
        /// </summary>
        public float DIX, DIY;

        /// <summary>
        /// ── Facing ──
        /// </summary>
        public float FacingYaw;          // radians, +Z = 0
        /// <summary>Combat aim yaw in radians — sent by client, used for hitboxes/hurtboxes.</summary>
        public float AimYaw;
        /// <summary>Target distance for projectile aim (meters). Set from InputState.AimDistance each tick.</summary>
        public float AimTargetDistance;
        /// <summary>Combat aim pitch in radians — sent by client, used for projectile direction.</summary>
        public float AimPitch;

        /// <summary>True while player is holding an aim-to-fire ability (RMB charge, Q throw).</summary>
        public bool IsAiming;

        /// <summary>
        /// Warp speed as a fraction of remaining distance closed per tick.
        /// e.g. 0.3 = 30% of remaining distance toward target each tick.
        /// Velocity is computed as: V = dx * WarpSpeed / TickDt in ProcessWarp.
        /// Set to 0 by ProcessWarp on arrival (within AttackRange).
        /// </summary>
        public float WarpTargetX;
        public float WarpTargetZ;
        public float WarpSpeed;
        public float WarpAttackRange;  // stop warping when this close
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
        /// ── Targeting ──
        /// </summary>
        /// <summary>Soft-lock target entity ID. 0 = none.</summary>
        public ulong TargetEntityId;

        /// <summary>
        /// ── Per-slot cooldowns (0-5) ──
        /// </summary>
        public ushort Cooldown0, Cooldown1, Cooldown2, Cooldown3, Cooldown4, Cooldown5;
        /// <summary>
        /// ── Buff / Self-enhancement ──
        /// </summary>
        public ushort BuffRemainingTicks;  // 0 = no active buff
        public byte BuffActiveFlags;        // bitfield, see BuffType enum
        /// <summary>
        /// ── Status effects (Marked, Slowed, etc.) ──
        /// </summary>
        public byte StatusFlags;           // bitfield, see StatusType enum
        public ushort StatusRemainingTicks; // shared countdown for all statuses; 0 = no active status
    }
}
