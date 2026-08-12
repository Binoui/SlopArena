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
        /// <summary>Ticks since last actionable event (attack, dash, jump, hit, landing). Drives FallRamp gravity.</summary>
        public ushort AirTimeTicks;
        /// <summary>
        /// Consecutive ticks the jump key has been held (issue #116 / ADR-0016). Reset when
        /// <c>InputState.JumpHeld</c> drops. Drives the short-hop decision at JumpSquat expiry:
        /// releasing within <c>Simulation.ShortHopWindowTicks</c> produces a reduced jump.
        /// Serialized so rollback replay of a JumpSquat opponent is byte-identical.
        /// </summary>
        public byte JumpHeldTicks;
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
        /// <summary>
        /// chain window remaining
        /// </summary>
        public ushort ComboTimerTicks;
        /// <summary>
        /// self-lock from attack (remaining)
        /// </summary>
        public ushort AnimLockTicks;
        /// <summary>
        /// Landing lag lock from landing mid-aerial (issue #125): remaining ticks the
        /// character is planted — no ability input, no jump/dash/burst, no input movement —
        /// after touching ground while an air-started attack whose stage declared
        /// LandingLagTicks is still active (unless the landing tick fell in an auto-cancel
        /// window). Sim-internal like AnimLockTicks: applied server-side, not on the wire;
        /// the client renders the state the packet says and the authority enforces the lock.
        /// Cleared by hitstun (ApplyKnockback) — being hit ends the commitment.
        /// </summary>
        public ushort LandingLagTicks;
        /// <summary>
        /// aimed charge progress (0 = none, >0 = charging)
        /// </summary>
        public ushort ChargeTicks;
        /// <summary>
        /// ── Charge-stock pool (refundable ability charges, e.g. Kistu Rising Slash) ──
        /// Number of charges currently spent/unavailable (0 = full pool). The pool max
        /// and regen cadence come from the ability spec's "max_charges"/"charge_regen_ticks"
        /// params. Refunded on hit by the ability's OnHitEntity. Server-sim only (not on the wire).
        /// </summary>
        public byte ChargeStockSpent;
        /// <summary>Ticks until the next spent charge regenerates. 0 when nothing is regenerating.</summary>
        public ushort ChargeStockRegenTicks;
        /// <summary>Full regen period for one charge, cached from the ability spec at spend time.</summary>
        public ushort ChargeStockRegenPeriod;

        /// <summary>
        /// ── Knockback ──
        /// </summary>
        public float KVX, KVY, KVZ;     // knockback velocity (decays separately)

        /// <summary>
        /// ── Hitstun + DI (Directional Influence) ──
        /// </summary>
        /// <summary>Remaining no-input lock ticks after hitstun begins (ADR-0012 renamed the comment — hitstop is the freeze).</summary>
        public ushort HitstunTicks;
        /// <summary>Remaining hitstop freeze ticks (ADR-0012). While > 0 the entity is frozen: no state machine, no physics, no timer decrement, no ability ticking.</summary>
        public ushort HitstopTicks;
        /// <summary>Queued launch payload, set at hit connect (ResolveHits), applied at freeze expiry (SimulateTick gate). Server + local-sim only — NOT on the wire.</summary>
        public float QueuedKBDirX, QueuedKBDirZ;
        public sbyte QueuedKBAngle;
        public float QueuedKBBase, QueuedKBGrowth;
        public ushort QueuedKBStun;
        /// <summary>True when the hit's OnHitEntity rewrote the launch at connect (e.g. NilusNetherGrasp's yank — the hitbox itself carries zero KB). The freeze-expiry gate then restores the QueuedKVX/Y/Z snapshot instead of recomputing from the raw params. Server + local-sim only — NOT on the wire.</summary>
        public bool QueuedKVOverride;
        /// <summary>Final knockback velocity snapshot taken after OnHitEntity (see QueuedKVOverride).</summary>
        public float QueuedKVX, QueuedKVY, QueuedKVZ;
        /// <summary>Hitstun animation tier: 0=small, 1=medium, 2=hard. Set at hit time.</summary>
        public byte HitstunLevel;
        /// <summary>
        /// accumulated DI input during hitstun
        /// </summary>
        public float DIX, DIY;
        /// <summary>Original launch magnitude (base + growth·(damage%·0.01)) of the current hitstun,
        /// captured at launch application, consumed by Combo Influence at hitstun expiry.
        /// 0 when no hitstun is live. Server + local-sim only — NOT on the wire (like the Queued fields).</summary>
        public float LaunchMagnitude;

        /// <summary>
        /// ── Burst (ADR-0014) ──
        /// </summary>
        /// <summary>Remaining cooldown ticks for Burst (one use per 60 s). ON THE WIRE — both players' HUDs read it.</summary>
        public ushort BurstCooldownTicks;
        /// <summary>Remaining recovery lock ticks after bursting (the punish window). ON THE WIRE — opponent's window must be visible.</summary>
        public ushort BurstRecoveryTicks;
        /// <summary>1 = defensive fired (shove attacker pending), 2 = offensive fired (hitbox pending). Set by SimulateTick, consumed+cleared by ServerSimulation each tick. Server + local-sim only — NOT on the wire.</summary>
        public byte BurstPending;
        /// <summary>Last entity to land a hit on this state. Set in ResolveHits, consumed by the defensive shove. Server + local-sim only — NOT on the wire.</summary>
        public ulong LastAttackerEntityId;


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
        /// Warp active flag. > 0 = currently warping toward target.
        /// Velocity is set to SprintSpeed (constant) in ProcessWarp.
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
        /// <summary>
        /// Per-slot cooldown ticks (11 slots — ADR-0016; slots 6-10 have no kit data yet).
        /// </summary>
        public ushort Cooldown0, Cooldown1, Cooldown2, Cooldown3, Cooldown4, Cooldown5,
            Cooldown6, Cooldown7, Cooldown8, Cooldown9, Cooldown10;

        /// <summary>Cooldown for an ActiveSlot value (1-11), 0 for out-of-range.</summary>
        public ushort GetCooldown(byte activeSlot) => activeSlot switch
        {
            AbilitySlots.Lmb => Cooldown0,
            AbilitySlots.Rmb => Cooldown1,
            AbilitySlots.Slot1 => Cooldown2,
            AbilitySlots.E => Cooldown3,
            AbilitySlots.R => Cooldown4,
            AbilitySlots.F => Cooldown5,
            AbilitySlots.Slot2 => Cooldown6,
            AbilitySlots.Slot3 => Cooldown7,
            AbilitySlots.Slot4 => Cooldown8,
            AbilitySlots.Slot5 => Cooldown9,
            AbilitySlots.A => Cooldown10,
            _ => 0,
        };

        /// <summary>Set cooldown for an ActiveSlot value (1-11); out-of-range is ignored.</summary>
        public void SetCooldown(byte activeSlot, ushort ticks)
        {
            switch (activeSlot)
            {
                case AbilitySlots.Lmb: Cooldown0 = ticks; break;
                case AbilitySlots.Rmb: Cooldown1 = ticks; break;
                case AbilitySlots.Slot1: Cooldown2 = ticks; break;
                case AbilitySlots.E: Cooldown3 = ticks; break;
                case AbilitySlots.R: Cooldown4 = ticks; break;
                case AbilitySlots.F: Cooldown5 = ticks; break;
                case AbilitySlots.Slot2: Cooldown6 = ticks; break;
                case AbilitySlots.Slot3: Cooldown7 = ticks; break;
                case AbilitySlots.Slot4: Cooldown8 = ticks; break;
                case AbilitySlots.Slot5: Cooldown9 = ticks; break;
                case AbilitySlots.A: Cooldown10 = ticks; break;
            }
        }
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
