using System;

#nullable enable

namespace SlopArena.Shared
{
    /// <summary>
    /// Pure C# simulation of one tick of game logic.
    /// No Godot dependencies — usable by Server, Client, and AI.
    ///
    /// Architecture:
    ///   SimulateTick() processes ONE tick (1/60s) of movement + combat
    ///   for a single character. It takes the current CharacterState,
    ///   mutates it to the next tick.
    ///
    ///   Hit detection uses SpellResolver (Shared/) — pure math.
    ///
    /// Usage (client):  SimulateTick(ref state, def, input, arena) → apply to Godot body
    /// Usage (server):  SimulateTick(ref state, def, input, arena) → broadcast state
    /// </summary>
    public static class Simulation
    {
        /// <summary>Input buffer window in ticks. Inputs within this many frames of unlock are buffered.</summary>
        public const ushort InputBufferWindow = 6;
        private static int _logCounter;
        /// <summary>Hook for debug logging. Set by the client to receive sim trace messages.</summary>
        public static System.Action<string>? OnDebugLog;
        public const float TickDt = 1f / 60f;

        /// <summary>
        /// ── Constants ──
        /// </summary>
        private const float AirDrag = 0.2f;
        /// <summary>
        /// Exponential knockback decay rate λ (per second). Applied every tick while
        /// knockback velocity is alive: KV *= exp(-λ·dt). Frontloaded, DKO-style —
        /// the launch is fastest right after the hit and smoothly slows, so most travel
        /// happens early and the victim drifts in the tail. Decaying all axes also
        /// flattens launch arcs (less vertical hang).
        /// Velocity halves every ln(2)/λ seconds (~0.39 s at λ=1.8), so a kill-level
        /// launch is roughly half speed by the end of a 22-tick hitstun.
        /// Tune this constant to adjust global knockback travel distance/shape.
        /// </summary>
        private const float KnockbackDecayRate = 1.8f;
        /// <summary>
        /// Gravity applied to vertical knockback velocity each tick (units/s²).
        /// Small so launches read as launches; the exponential decay does the braking.
        /// </summary>
        private const float KnockbackMinGravity = 2.0f;
        private const byte MaxAirDodges = 1;

        /// <summary>
        /// Dash duration: 0.25 second = 15 ticks
        /// </summary>
        public const ushort DashDurationTicks = 15;
        /// <summary>
        /// full duration invincible
        /// </summary>
        private const ushort DashInvincibilityTicks = 15;
        /// <summary>
        /// Dash deceleration per second (m/s²). Applied each tick during dash for a smooth decay curve.
        /// At DashSpeed=30 over 15 ticks: VZ ≈ 30 → 10 m/s, distance ≈ 5m.
        /// </summary>
        private const float DashDeceleration = 80f;

        /// <summary>
        /// 0.2s
        /// </summary>
        private const ushort SprintThresholdTicks = 12;
        /// <summary>
        /// 0.1s
        /// </summary>
        private const ushort TurnaroundLagTicks = 6;

        /// <summary>
        /// Horizontal speed dead zone. Below this, velocity is snapped to zero
        /// to prevent residual drifting from asymptotic friction decay.
        /// </summary>
        private const float VelocityDeadZone = 0.015f;

        /// <summary>
        /// Tolerance for snapping to platform surfaces (units).
        /// Characters must be within this window above the surface to snap.
        /// Public because abilities that write position directly must agree with ground
        /// resolution on what counts as a traversable step (see NilusRiftwalk).
        /// </summary>
        public const float PlatformSnapTolerance = 0.5f;
        /// <summary>
        /// How far above the surface the character can be and still land.
        /// Must be small enough that a jump (VY ≈ 10) immediately breaks it in 1-2 frames.
        /// </summary>
        private const float PlatformLandTolerance = 0.1f;

        /// <summary>
        /// Horizontal search radius for ledge snap (meters).
        /// 0.8m ≈ character width — avoids magnetic pull across small platforms.
        /// </summary>
        private const float LedgeSnapRange = 0.8f;
        /// <summary>
        /// Upward velocity on ledge snap (m/s).
        /// </summary>
        private const float LedgeSnapUpwardBoost = 5f;
        /// <summary>
        /// Max Y below surface edge to snap.
        /// Prevents snap from deep below the stage.
        /// </summary>
        private const float LedgeGrabTolerance = 2.5f;

        /// Resolve the effective AttackStage from an AbilitySpec, clamping ComboStage.
        public static AttackStage ResolveStage(AbilitySpec spec, in CharacterState state)
        {
            int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
            return spec.Stages[stageIdx];
        }

        // ── MAIN ENTRY POINT ──

        /// <summary>
        /// Process one simulation tick for a character.
        /// Mutates state in-place.
        /// </summary>
        public static void SimulateTick(
            ref CharacterState s,
            CharacterDefinition def,
            InputState input,
            ArenaDefinition arena)
        {
            var stats = def.Movement;

            // Apply combat aim yaw from input (degrees * 100 → radians)
            // FacingYaw (movement-facing) is handled by ProcessNormalMovement via Atan2
            float aimDeg = input.AimYaw * 0.01f;
            s.AimYaw = aimDeg * (MathF.PI / 180f);
            // Store aim target distance (cm → m) for projectile abilities
            s.AimTargetDistance = input.AimDistance * 0.01f;
            // Apply combat aim pitch from input (degrees * 100 → radians)
            s.AimPitch = input.AimPitch * 0.01f * (MathF.PI / 180f);


            // 2.5 JumpSquat: tick down, apply jump force on expiry
            if (s.State == ActionState.JumpSquat)
            {
                s.StateTicks--;
                if (s.StateTicks == 0)
                {
                    s.VY = stats.JumpForce;
                    s.IsGrounded = false;
                    s.State = ActionState.Idle;
                    s.AirTimeTicks = (ushort)(stats.FloatWindowTicks + stats.FallRampDuration);
                }
                // During squat: preserve horizontal momentum, no acceleration
            }
            s.IsAiming = input.IsAiming;

            // 1. Tick timers
            TickTimers(ref s);

            // 2. Hitstun overrides everything (DI window)
            if (s.State == ActionState.Hitstun)
            {
                ProcessHitstun(ref s, input);
                // Fall through — position update + ground collision must run during hitstun.
                // Without this, the target stands perfectly still for the entire stun duration
                // (V=KV set but PX/PZ/PY never updated), then does a single-frame hop on expiry.
            }

            // 3. Knockback overrides everything (but dash invincibility still applies)
            if (s.State != ActionState.Hitstun && HasKnockback(s))
            {
                ProcessKnockback(ref s, arena, def);
                return;
            }

            // 4. Warp processing: velocity override during any state
            if (s.WarpSpeed > 0f)
            {
                bool warpComplete = ProcessWarp(ref s, def, arena);
                if (warpComplete)
                {
                    // Warp arrival: velocity cleared, WarpSpeed=0.
                    // Let the ability continue — lunge and hitboxes are still pending.
                    // TickAbilities (called after SimulateTick) handles the rest.
                }
            }
            // Only process state machine if not warping
            else
            {
                // 5. State machine
                if (s.State == ActionState.Dashing)
                    ProcessDash(ref s, stats);
                else if (s.State == ActionState.AirDodging)
                    ProcessAirDodge();
                // Attacking state is now purely handled by ServerSimulation.TickAbilities
            }

            // Ground friction during attacking (abilities handle velocity via LungeForce)
            // Skip during warp — warp velocity should stay at SprintSpeed until arrival
            if (s.State == ActionState.Attacking && s.IsGrounded && s.WarpSpeed <= 0f)
            {
                float friction = stats.GroundFriction * TickDt;
                s.VX = MoveToward(s.VX, 0f, Math.Abs(s.VX) * friction);
                s.VZ = MoveToward(s.VZ, 0f, Math.Abs(s.VZ) * friction);
            }



            // 5.5 Consume buffered input (any lock just expired)
            if (s.BufferedSlot > 0 && s.AnimLockTicks == 0 && s.HitstunTicks == 0 &&
                s.State == ActionState.Idle && !input.Jump && !input.Dash)
            {
                byte slot = s.BufferedSlot;
                s.BufferedSlot = 0;
                // Ability activation handled by ServerSimulation.Tick pre-sim phase
                s.State = ActionState.Attacking;
                s.AttackSlot = slot;
                s.AirTimeTicks = 0;
                if (!s.IsGrounded && s.VY < 0f)
                    s.VY = 0f;
            }

            // 5.75 Jump detection (unconditional except hitstun / already squatting)
            if (input.Jump && s.JumpsLeft > 0 && s.AnimLockTicks == 0 && s.HitstunTicks == 0 && s.State != ActionState.JumpSquat && s.State != ActionState.Aiming)
            {
                if (s.IsGrounded)
                {
                    // Ground jump → enter JumpSquat
                    s.State = ActionState.JumpSquat;
                    s.StateTicks = stats.JumpSquatTicks;
                    s.JumpsLeft--;
                    // VX, VZ preserved — momentum carries through squat into air
                    // VY stays 0 — applied when squat expires
                }
                else
                {
                    // Double jump
                    s.VY = stats.JumpForce;
                    (float dirX, float dirZ) = GetInputDirection(input);
                    s.VX = dirX * stats.WalkSpeed;
                    s.VZ = dirZ * stats.WalkSpeed;
                    s.JumpsLeft--;
                    s.AirTimeTicks = (ushort)(stats.FloatWindowTicks + stats.FallRampDuration);
                }
            }

            else if (input.Jump)
            {
                string reason = s.AnimLockTicks > 0 ? "anim_lock" :
                    s.HitstunTicks > 0 ? "hitstun" :
                    s.State == ActionState.JumpSquat ? "already_squatting" :
                    s.JumpsLeft <= 0 ? "no_jumps" : "unknown";
                OnDebugLog?.Invoke($"[JumpBlocked] input.Jump=true but blocked by {reason}");
            }

            // 6. Input-driven actions (only when not locked by animation or in jump squat;
            // aiming blocks dash — the ability owns movement until release)
            if (s.AnimLockTicks == 0 && s.State != ActionState.Hitstun && s.State != ActionState.JumpSquat && s.State != ActionState.Aiming)
            {
                // Jump — handled inside ProcessNormalMovement/ProcessAirMovement
                // Dash
                if (input.Dash && s.DashDurationTicks == 0 && s.DashCooldownTicks == 0)
                {
                    StartDash(ref s, stats, input.MoveX, input.MoveY);
                }

                if (input.ActiveSlot > 0 && s.State == ActionState.Idle)
                {
                    ushort cd = input.ActiveSlot switch
                    {
                        1 => s.Cooldown0,
                        2 => s.Cooldown1,
                        3 => s.Cooldown2,
                        4 => s.Cooldown3,
                        5 => s.Cooldown4,
                        6 => s.Cooldown5,
                        _ => 0,
                    };
                    if (cd == 0)
                    {
                        s.State = ActionState.Attacking;
                        s.AttackSlot = input.ActiveSlot;
                        s.StateTicks = 0;
                        s.AirTimeTicks = 0;
                        if (!s.IsGrounded && s.VY < 0f)
                            s.VY = 0f;
                    }
                }
            }
            // Buffer input if locked within window
            // NOTE: Combo buffering is now handled by ServerAbility.Tick lifecycle
            // Only general input buffering (unlock window) is kept for client prediction
            if (input.ActiveSlot > 0 && (s.AnimLockTicks > 0 || s.HitstunTicks > 0 || s.State == ActionState.JumpSquat) && s.BufferedSlot == 0)
            {
                // General buffer: within window of unlock
                if (s.State == ActionState.JumpSquat ||
                    (s.AnimLockTicks > 0 && s.AnimLockTicks <= InputBufferWindow) ||
                    (s.HitstunTicks > 0 && s.HitstunTicks <= InputBufferWindow))
                {
                    // No cooldown check here — ServerSimulation handles ability activation validation
                    s.BufferedSlot = input.ActiveSlot;
                }
            }

            // 7. ProcessNormalMovement (idle + aiming — attacks handle velocity via LungeForce.
            // Aiming keeps walk/run unlocked so the player can reposition while aiming.)
            // Fixed-stance aim holds process no inputs: momentum bleeds via friction, but the
            // player cannot steer, dash, or jump. Mobile aim (Kistu's DirectionalDash) keeps control.
            bool fixedAim = s.State == ActionState.Aiming && s.AttackSlot > 0
                && def.GetSlotAbility(s.AttackSlot - 1, !s.IsGrounded)?.Behavior is AbilityBehavior.AimedProjectile or AbilityBehavior.Projectile;
            if (s.State == ActionState.Idle || s.State == ActionState.Aiming)
            {
                ProcessNormalMovement(ref s, stats, input, processInput: !fixedAim);
            }
            
            // 7b. Charge ticks for aimed projectile abilities (Manki Q, FightGuy Q).
            // ServerAbility subclasses read s.ChargeTicks to check max hold duration.
            if ((s.State is ActionState.Attacking or ActionState.Aiming) && s.AttackSlot > 0 && s.ChargeTicks < ushort.MaxValue)
            {
                var spec = def.GetSlotAbility(s.AttackSlot - 1, !s.IsGrounded);
                if (spec != null && spec.Behavior == AbilityBehavior.AimedProjectile)
                {
                    if (input.IsAiming && s.ChargeTicks < spec.ChargeHoldTicks)
                        s.ChargeTicks++;
                }
            }
            
            // 8. Gravity (skip during hitstun — ProcessHitstun handles KVY decay)
            if (s.State != ActionState.Hitstun)
                ApplyGravity(ref s, stats);
            
            // 9. Position integration
            s.PX += s.VX * TickDt;

            s.PZ += s.VZ * TickDt;
            s.PY += s.VY * TickDt;

            // 10. Ground collision via heightmap
            float capsuleHalf = def.CapsuleHeight * 0.5f;
            float surfaceY = arena.Heightmap.Data != null
                ? arena.Heightmap.Sample(s.PX, s.PZ)
                : arena.KillHeight + 1f;
            float groundY = float.NaN;
            if (surfaceY > float.MinValue)
            {
                groundY = surfaceY + capsuleHalf;
                if (s.State == ActionState.Hitstun)
                {
                    // During hitstun: skip snap while rising (KVY > 0) so launch works.
                    // Re-snap when falling (KVY <= 0) — clear KVY to prevent next-tick gravity drill.
                    // Force-snap if below surface to prevent map fall-through.
                    bool atSurface = s.PY <= groundY + PlatformLandTolerance && s.PY >= groundY - PlatformSnapTolerance;
                    if (atSurface && s.KVY <= 0f)
                    {
                        s.IsGrounded = true;
                        s.VY = 0f;
                        s.PY = groundY;
                        s.AirTimeTicks = 0;
                        s.KVY = 0f;
                    }
                    else if (s.PY < groundY - PlatformSnapTolerance)
                    {
                        s.IsGrounded = true;
                        s.VY = 0f;
                        s.KVY = 0f;
                        s.PY = groundY;
                        s.AirTimeTicks = 0;
                    }
                    else
                    {
                        s.IsGrounded = false;
                    }
                }
                else if (s.PY <= groundY + PlatformLandTolerance && s.PY >= groundY - PlatformSnapTolerance)
                {
                    s.IsGrounded = true;
                    s.VY = 0f;
                    s.PY = groundY;
                    s.AirTimeTicks = 0;
                }
                else
                {
                    s.IsGrounded = false;
                }
            }
            else
            {
                s.IsGrounded = false;
            }

            // 11. Landing cleanup
            if (s.State == ActionState.AirDodging && s.IsGrounded)
            {
                s.State = ActionState.Idle;
            }

            // 12. Ledge snap (auto-grab when near stage edge)
            if (!s.IsGrounded && s.HitstunTicks == 0 && !HasKnockback(s))
            {
                TryLedgeSnap(ref s, arena, capsuleHalf);
             }

            // DEBUG: log ground collision data (every 60 ticks = ~1/sec per entity)
            if (_logCounter++ % 60 == 0)
                OnDebugLog?.Invoke(
                    $"[SimGround] sY={surfaceY:F3} cH={capsuleHalf:F3} gY={groundY:F3} PY={s.PY:F3} gnd={s.IsGrounded} st={s.State}");
        }

        // ── TIMERS ──

        private static void TickTimers(ref CharacterState s)
        {
            if (s.DashCooldownTicks > 0) s.DashCooldownTicks--;
            if (s.DashDurationTicks > 0) s.DashDurationTicks--;
            if (s.InvincibilityTicks > 0) s.InvincibilityTicks--;
            if (s.AnimLockTicks > 0) s.AnimLockTicks--;
            if (s.HitstunTicks > 0) s.HitstunTicks--;
            if (s.AttackElapsedTicks < 65535) s.AttackElapsedTicks++;

            // Turnaround ticks
            if (s.TurnaroundTicks > 0) s.TurnaroundTicks--;

            // State ticks (generic expiry — JumpSquat is handled specially below)
            if (s.StateTicks > 0 && s.State != ActionState.JumpSquat)
            {
                s.StateTicks--;
                if (s.StateTicks == 0 && s.State != ActionState.Idle)
                {
                    s.State = ActionState.Idle;
                }
            }

            // Cooldowns
            if (s.Cooldown0 > 0) s.Cooldown0--;
            if (s.Cooldown1 > 0) s.Cooldown1--;
            if (s.Cooldown2 > 0) s.Cooldown2--;
            if (s.Cooldown3 > 0) s.Cooldown3--;
            if (s.Cooldown4 > 0) s.Cooldown4--;
            if (s.Cooldown5 > 0) s.Cooldown5--;

            // Charge-stock regen (refundable ability pools, e.g. Kistu Rising Slash).
            // Only active when a charge is spent; recovers one charge per regen period.
            if (s.ChargeStockSpent > 0)
            {
                if (s.ChargeStockRegenTicks > 0) s.ChargeStockRegenTicks--;
                if (s.ChargeStockRegenTicks == 0)
                {
                    s.ChargeStockSpent--;
                    if (s.ChargeStockSpent > 0)
                        s.ChargeStockRegenTicks = s.ChargeStockRegenPeriod > 0 ? s.ChargeStockRegenPeriod : (ushort)180;
                }
            }

            // Buff timer
            if (s.BuffRemainingTicks > 0)
            {
                s.BuffRemainingTicks--;
                if (s.BuffRemainingTicks == 0)
                    s.BuffActiveFlags = 0;
            }

            // Status timer
            if (s.StatusRemainingTicks > 0)
            {
                s.StatusRemainingTicks--;
                if (s.StatusRemainingTicks == 0)
                    s.StatusFlags = 0;  // clear all statuses when timer expires
            }
        }

        // ── HITSTUN + DI (Directional Influence) ──

        /// <summary>
        /// Process hitstun state: apply knockback immediately (no freeze before flight).
        /// HitstunTicks controls how long the victim can't act (animation lock).
        /// DI input is stored during hitstun and applied when it expires.
        /// </summary>
        private static void ProcessHitstun(ref CharacterState s, InputState input)
        {
            // Exponential (frontloaded) decay: the launch is fastest right after the
            // hit, then smoothly loses speed over the lock (DKO-style). Decays all
            // axes, which also flattens launch arcs.
            float decay = MathF.Exp(-KnockbackDecayRate * TickDt);
            s.KVX *= decay;
            s.KVY *= decay;
            s.KVZ *= decay;

            // Apply gravity to KVY during hitstun so the target doesn't
            // coast upward at constant speed for the full duration.
            // Without this, gravity only starts in ProcessKnockback after
            // hitstun expires, making upward trajectories feel floaty.
            // No clamp: downward KB (spikes) should accelerate downward.
            if (!s.IsGrounded)
                s.KVY -= KnockbackMinGravity * TickDt;

            // Apply knockback force plus gravity toward velocity
            s.VX = s.KVX;
            s.VY = s.KVY;
            s.VZ = s.KVZ;

            // Store current input for DI (applied when hitstun expires)
            s.DIX = input.MoveX;
            s.DIY = input.MoveY;

            // When hitstun ends, apply DI influence to remaining knockback
            if (s.HitstunTicks == 0)
            {
                const float DIStrength = 3.5f;
                s.KVX += s.DIX * DIStrength;
                s.KVZ += s.DIY * DIStrength;
                s.DIX = 0f;
                s.DIY = 0f;

                // Transfer remaining knockback to main velocity and clear KV.
                // Without this, HasKnockback() returns true and ProcessKnockback's
                // early return blocks all movement/action processing for 9+ seconds
                // while KV exponentially decays toward zero.
                s.VX = s.KVX;
                s.VY = s.KVY;
                s.VZ = s.KVZ;
                s.KVX = 0f;
                s.KVY = 0f;
                s.KVZ = 0f;

                s.State = ActionState.Idle;
            }
        }

        // ── KNOCKBACK ──

        private static bool HasKnockback(CharacterState s)
        {
            return ((s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ)) > 0.0001f;
        }

        private static bool TryLedgeSnap(ref CharacterState s, ArenaDefinition arena, float capsuleHalf)
        {
            // 1. Already grounded or in hitstun — skip (caller also guards, but be safe)
            if (s.IsGrounded || s.State == ActionState.Hitstun) return false;

            // 2. Active knockback without hitstun — skip (rare but possible with stunTicks=0)
            if (HasKnockback(s)) return false;

            // 3. Check if currently over a platform — if so, not a ledge scenario
            float centerSurface = arena.Heightmap.Data != null
                ? arena.Heightmap.Sample(s.PX, s.PZ)
                : float.MinValue;
            if (centerSurface > float.MinValue)
                return false; // over a platform — normal ground collision handles it

            // 4. Search 4 cardinal neighbors for a valid surface edge
            float[] offsets = { LedgeSnapRange, -LedgeSnapRange };
            foreach (float dx in offsets)
            {
                float neighborSurface = arena.Heightmap.Sample(s.PX + dx, s.PZ);
                if (neighborSurface > float.MinValue)
                {
                    float ledgeY = neighborSurface + capsuleHalf;
                    if (s.PY >= ledgeY - LedgeGrabTolerance && s.PY <= ledgeY + 0.5f)
                    {
                        s.PY = ledgeY;
                        s.IsGrounded = true;
                        s.VY = LedgeSnapUpwardBoost;
                        s.KVX = 0f; s.KVY = 0f; s.KVZ = 0f;
                        if (s.State == ActionState.AirDodging)
                            s.State = ActionState.Idle;
                        return true;
                    }
                }
            }
            foreach (float dz in offsets)
            {
                float neighborSurface = arena.Heightmap.Sample(s.PX, s.PZ + dz);
                if (neighborSurface > float.MinValue)
                {
                    float ledgeY = neighborSurface + capsuleHalf;
                    if (s.PY >= ledgeY - LedgeGrabTolerance && s.PY <= ledgeY + 0.5f)
                    {
                        s.PY = ledgeY;
                        s.IsGrounded = true;
                        s.VY = LedgeSnapUpwardBoost;
                        s.KVX = 0f; s.KVY = 0f; s.KVZ = 0f;
                        if (s.State == ActionState.AirDodging)
                            s.State = ActionState.Idle;
                        return true;
                    }
                }
            }
            return false;
        }

        private static void ProcessKnockback(ref CharacterState s, ArenaDefinition arena, CharacterDefinition def)
        {
            // Same exponential decay as ProcessHitstun — one continuous frontloaded
            // curve whether the victim is in hitstun or drifting after it.
            float decay = MathF.Exp(-KnockbackDecayRate * TickDt);
            s.KVX *= decay;
            s.KVY *= decay;
            s.KVZ *= decay;

            // Apply to main velocity
            s.VX = s.KVX;
            s.VY = s.KVY;
            s.VZ = s.KVZ;

            // Minimal gravity during knockback
            if (!s.IsGrounded)
                s.VY -= KnockbackMinGravity * TickDt;

            // Position update
            s.PX += s.VX * TickDt;
            s.PZ += s.VZ * TickDt;
            s.PY += s.VY * TickDt;

            // Ground check via heightmap
            bool wasAirborne = !s.IsGrounded;
            float capsuleHalfKb = def.CapsuleHeight * 0.5f;
            float kbSurfaceY = arena.Heightmap.Data != null
                ? arena.Heightmap.Sample(s.PX, s.PZ)
                : float.MinValue;
            if (kbSurfaceY > float.MinValue)
            {
                float groundY = kbSurfaceY + capsuleHalfKb;
                s.IsGrounded = s.PY <= groundY + PlatformLandTolerance && s.PY >= groundY - PlatformSnapTolerance;
            }
            else
            {
                s.IsGrounded = false;
            }

            if (s.IsGrounded)
            {
                s.VY = 0f;
                s.PY = kbSurfaceY + capsuleHalfKb;
            }

            if (wasAirborne && s.IsGrounded)
            {
                // Natural landing clears knockback
                ClearKnockback(ref s);
                s.AirDodgesLeft = MaxAirDodges;
            }
        }

        // ── DASH ──

        private static void ProcessDash(ref CharacterState s, MovementStats stats)
        {
            if (s.DashDurationTicks > 0)
            {
                // Decaying dash speed: start fast, slow down smoothly each tick
                float currentSpeed = MathF.Sqrt((s.VX * s.VX) + (s.VZ * s.VZ));
                float newSpeed = Math.Max(currentSpeed - DashDeceleration * TickDt, 0f);
                if (currentSpeed > 0.001f && newSpeed > 0f)
                {
                    float ratio = newSpeed / currentSpeed;
                    s.VX *= ratio;
                    s.VZ *= ratio;
                }
                else
                {
                    s.VX = 0f;
                    s.VZ = 0f;
                }
                s.VY = Math.Max(s.VY, 0f);
            }
            else
            {
                s.State = ActionState.Idle;
                s.VX = 0f;
                s.VZ = 0f;
            }
        }

        // ── AIR DODGE ──

        private static void ProcessAirDodge()
        {
            // Air dodge maintains its velocity (set once when initiated)
            // Natural drift/end handled by state tick expiry
        }

        /// <summary>
        /// Snap horizontal velocity to zero when below the dead zone threshold.
        /// Prevents residual drift from asymptotic friction/drag decay.
        /// </summary>
        private static void ApplyVelocityDeadZone(ref CharacterState s)
        {
            if (Math.Abs(s.VX) < VelocityDeadZone && Math.Abs(s.VZ) < VelocityDeadZone)
            {
                s.VX = 0f;
                s.VZ = 0f;
            }
        }

        // ── NORMAL MOVEMENT ──

        private static void ProcessNormalMovement(
            ref CharacterState s, MovementStats stats, InputState input, bool processInput = true)
        {
            if (!processInput)
            {
                // Fixed aim: decay horizontal momentum, nothing else — no accel, no facing,
                // no resource resets. Ground mirrors the attacking-friction branch exactly;
                // air uses the per-character AirFriction stat with the air-drag shape from
                // ProcessAirMovement.
                if (s.IsGrounded)
                {
                    float friction = stats.GroundFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, Math.Abs(s.VX) * friction);
                    s.VZ = MoveToward(s.VZ, 0f, Math.Abs(s.VZ) * friction);
                }
                else
                {
                    float drag = stats.AirFriction * TickDt;
                    s.VX *= (1f - drag);
                    s.VZ *= (1f - drag);
                    ApplyVelocityDeadZone(ref s);
                }
                return;
            }

            (float dirX, float dirZ) = GetInputDirection(input);

            if (s.IsGrounded)
            {
                ProcessGroundMovement(ref s, stats, input, dirX, dirZ);
            }
            else
            {
                ProcessAirMovement(ref s, stats, input, dirX, dirZ);
            }

            // Store last input direction for tech roll / air dodge fallback
            s.LastDirX = dirX;
            s.LastDirZ = dirZ;
        }

        private static void ProcessGroundMovement(
            ref CharacterState s, MovementStats stats,
            InputState input, float dirX, float dirZ)
        {
            // Reset resources on ground each tick
            s.AirDodgesLeft = MaxAirDodges;
            s.JumpsLeft = stats.MaxJumps;
            s.IsGrounded = true;

            bool hasInput = ((dirX * dirX) + (dirZ * dirZ)) > 0.0001f;

            if (hasInput)
            {
                // Detect direction change
                bool hadDir = ((s.LastDirX * s.LastDirX) + (s.LastDirZ * s.LastDirZ)) > 0.0001f;
                bool dirChanged = hadDir && (((dirX * s.LastDirX) + (dirZ * s.LastDirZ)) < 0.5f);

                if (dirChanged)
                {
                    s.DirHoldTicks = 0;
                    if (s.IsSprinting)
                    {
                        s.TurnaroundTicks = TurnaroundLagTicks;
                        s.IsSprinting = false;
                    }
                }
                else
                {
                    // Same direction or initial press — accumulate hold time
                    if (s.DirHoldTicks < ushort.MaxValue)
                        s.DirHoldTicks++;

                    if (s.DirHoldTicks >= SprintThresholdTicks && !s.IsSprinting)
                        s.IsSprinting = true;
                }

                s.LastDirX = dirX;
                s.LastDirZ = dirZ;

                if (s.TurnaroundTicks > 0)
                {
                    // Turnaround lag: decelerate
                    float friction = stats.GroundFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, Math.Abs(s.VX) * friction);
                    ApplyVelocityDeadZone(ref s);
                }
                else
                {
                    // Instant speed in input direction
                    float speed = s.IsSprinting ? stats.SprintSpeed : stats.WalkSpeed;
                    s.VX = dirX * speed;
                    s.VZ = dirZ * speed;
                }
            }
            else
            {
                // No input: friction, reset sprint
                s.DirHoldTicks = 0;
                s.IsSprinting = false;
                s.LastDirX = s.LastDirZ = 0f;

                float friction = stats.GroundFriction * TickDt;
                s.VX = MoveToward(s.VX, 0f, Math.Abs(s.VX) * friction);
                s.VZ = MoveToward(s.VZ, 0f, Math.Abs(s.VZ) * friction);
            }
            ApplyVelocityDeadZone(ref s);

            if (hasInput)
            {
                s.FacingYaw = MathF.Atan2(dirX, dirZ);
            }
        }

        private static void ProcessAirMovement(
            ref CharacterState s, MovementStats stats,
            InputState input, float dirX, float dirZ)
        {
            s.IsGrounded = false;

            // Air acceleration toward input direction
            float airAccel = stats.AirAcceleration * TickDt;
            float targetVX = dirX * stats.WalkSpeed;
            float targetVZ = dirZ * stats.WalkSpeed;
            s.VX = MoveToward(s.VX, targetVX, airAccel);
            s.VZ = MoveToward(s.VZ, targetVZ, airAccel);

            // Air drag
            const float drag = AirDrag * TickDt;
            s.VX *= (1f - drag);
            s.VZ *= (1f - drag);
            ApplyVelocityDeadZone(ref s);

            // Dash initiation is handled by PlayerController outside of Simulation
            // (works both ground and air, has cooldown, grants invincibility)

            bool hasAirInput = ((dirX * dirX) + (dirZ * dirZ)) > 0.0001f;
            if (hasAirInput)
            {
                s.FacingYaw = MathF.Atan2(dirX, dirZ);
            }
        }

        // ── ATTACK PROCESSING ──
        // Removed: ProcessAttack() — all ability execution now handled by ServerAbility lifecycle

        // Removed: StartAttackFromSlot() — ability activation is now handled by ServerSimulation pre-sim phase

        /// <summary>
        /// Process warping state: sets velocity toward warp target each tick.
        /// Position update and collision are handled by main SimulateTick loop.
        /// Returns true if warp completed (arrived at target), false if still warping.
        /// </summary>
        private static bool ProcessWarp(ref CharacterState s, CharacterDefinition def, ArenaDefinition arena)
        {
            float dx = s.WarpTargetX - s.PX;
            float dz = s.WarpTargetZ - s.PZ;
            float distSq = dx * dx + dz * dz;
            float attackRangeSq = s.WarpAttackRange * s.WarpAttackRange;

            // Close enough → warp complete
            if (distSq <= attackRangeSq)
            {
                s.WarpSpeed = 0f;
                s.VX = 0f;
                s.VZ = 0f;
                return true;
            }

            // Set velocity toward target: constant speed at SprintSpeed
            // (auto-run feel — matched to character movement speed)
            float dist = MathF.Sqrt(distSq);
            s.VX = (dx / dist) * def.Movement.SprintSpeed;
            s.VZ = (dz / dist) * def.Movement.SprintSpeed;
            s.FacingYaw = MathF.Atan2(dx, dz);

            // Position update and collision handled by main SimulateTick loop (steps 5-7)
            // Gravity is applied by ApplyGravity() (step 5)

            return false; // still warping
        }

        /// Start a dash (ground or air). 1 second duration, grants invincibility.
        /// Can be used on ground or in air.
        /// Clears attack state if interrupting an attack (AttackSlot, AnimLockTicks, ability).
        /// Deactivation of the server-side ability instance is handled by the caller
        /// (ServerSimulation) — StartDash only clears the state fields.
        /// </summary>
        public static void StartDash(ref CharacterState s, MovementStats stats, float dirX, float dirZ)
        {
            if (s.DashCooldownTicks > 0) return;
            if (s.State != ActionState.Idle && s.State != ActionState.Attacking && s.State != ActionState.Dashing) return;
            if (s.InvincibilityTicks > 0) return; // already invincible
            if (HasKnockback(s)) return;

            // Clear attack state when dash interrupts an attack
            // (ServerSimulation deactivates the ServerAbility separately via _activeAbilities removal)
            if (s.State == ActionState.Attacking)
            {
                s.AttackSlot = 0;
                s.ComboStage = 0;
                s.AttackElapsedTicks = 0;
                s.AnimLockTicks = 0;
            }

            // Normalize direction
            float len = MathF.Sqrt((dirX * dirX) + (dirZ * dirZ));
            if (len < 0.01f)
            {
                // No input: dash forward (based on facing)
                dirX = MathF.Sin(s.FacingYaw);
                dirZ = MathF.Cos(s.FacingYaw);
            }
            else
            {
                dirX /= len;
                dirZ /= len;
            }

            s.DashDirX = dirX;
            s.DashDirZ = dirZ;
            s.DashDurationTicks = stats.DashDurationTicks;
            s.DashCooldownTicks = stats.DashCooldownTicks;
            s.InvincibilityTicks = DashInvincibilityTicks; // invincible for full dash
            s.State = ActionState.Dashing;
            s.StateTicks = 0;

            s.VX = dirX * stats.DashSpeed;
            s.VZ = dirZ * stats.DashSpeed;
            s.VY = s.IsGrounded ? Math.Max(s.VY, 0f) : 0f;
            s.AirTimeTicks = s.IsGrounded ? (ushort)0 : (ushort)Math.Max(s.AirTimeTicks, stats.FloatWindowTicks);
        }

        /// <summary>
        /// Apply jump force. Consumes one jump if available.
        /// </summary>
        public static void ApplyJump(ref CharacterState s, float jumpForce)
        {
            if (s.JumpsLeft <= 0) return;
            s.VY = jumpForce;
            s.JumpsLeft--;
            s.IsGrounded = false;
        }

        /// <summary>
        /// Apply knockback using a fixed launch angle + Smash-style magnitude scaling.
        /// KnockbackUpward/KnockbackY replaced by angleDeg: -90 (straight down) to 90 (straight up).
        /// Magnitude = baseKB + growthKB * (damage% * 0.01). Angle determines KVX/KVY/KVZ split.
        /// </summary>
        public static void ApplyKnockback(ref CharacterState s, float dirX, float dirZ,
            sbyte angleDeg, float baseKB, float growthKB, ushort stunTicks)
        {
            float magnitude = baseKB + growthKB * (s.DamagePercent * 0.01f);
            float rad = angleDeg * MathF.PI / 180f;
            float cosA = MathF.Cos(rad);
            float sinA = MathF.Sin(rad);

            s.KVX = dirX * magnitude * cosA;
            s.KVY = magnitude * sinA;
            s.KVZ = dirZ * magnitude * cosA;

            // Launch victim off the ground — prevents ground snap from eating vertical KB
            // (was: IsGrounded stayed true, ground collision reset PY and VY=0 each tick)
            if (s.KVY > 0f)
                s.IsGrounded = false;
            float kbMagnitude = MathF.Sqrt(
                (s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ));

            // Hitstun gates on StunTicks from the hitbox data, not arbitrary 3f threshold.
            // If StunTicks=0, no hitstun (true weak jab). Any positive = hitstun triggers.
            // kbMagnitude < 0.5f is a safety floor (near-zero KB with hitstun looks glitchy).
            if (stunTicks > 0 && kbMagnitude > 0.5f)
            {
                ushort hitstunFromKB = (ushort)Math.Clamp(8 + (int)(kbMagnitude * 0.5f), 8, 60);
                ushort hitstunFinal = Math.Min(hitstunFromKB, stunTicks);
                s.HitstunTicks = hitstunFinal;
                s.State = ActionState.Hitstun;
            }
            else
            {
                s.State = ActionState.Idle;
            }

            // AirTime resets on any damage/hit, regardless of hitstun
            s.AirTimeTicks = 0;

            s.DashDurationTicks = 0;
            s.StateTicks = 0;
            s.WasAirborneDuringKnockback = !s.IsGrounded;
        }

        /// <summary>
        /// Apply damage and increase damage percentage.
        /// </summary>
        public static void ApplyDamage(ref CharacterState s, float damage)
        {
            int newPercent = s.DamagePercent + (int)Math.Round(damage);
            s.DamagePercent = (ushort)Math.Clamp(newPercent, 0, 999);
        }

        /// <summary>
        /// Tech roll: clears knockback, small burst in last input direction.
        /// </summary>
        public static void DoTechRoll(ref CharacterState s)
        {
            ClearKnockback(ref s);

            float dirX = s.LastDirX;
            float dirZ = s.LastDirZ;
            float len = MathF.Sqrt((dirX * dirX) + (dirZ * dirZ));
            if (len < 0.01f)
            {
                // No input: forward
                dirX = MathF.Sin(s.FacingYaw);
                dirZ = MathF.Cos(s.FacingYaw);
            }
            else
            {
                dirX /= len;
                dirZ /= len;
            }

            s.VX = dirX * 10f;
            s.VZ = dirZ * 10f;
            s.VY = 0f;
            s.State = ActionState.Idle;
        }
        private static void ClearKnockback(ref CharacterState s)
        {
            s.KVX = s.KVY = s.KVZ = 0f;
        }

        // ── GRAVITY ──
        
        private static void ApplyGravity(ref CharacterState s, MovementStats stats)
        {
            if (!s.IsGrounded)
            {
                // Increment AirTime each tick while airborne
                if (s.AirTimeTicks < ushort.MaxValue)
                    s.AirTimeTicks++;

                float gravity;

                if (s.AirTimeTicks < stats.FloatWindowTicks)
                {
                    // FloatWindow: zero or reduced gravity set per character
                    gravity = stats.AirFloatGravity;
                }
                else if (s.AirTimeTicks < stats.FloatWindowTicks + stats.FallRampDuration)
                {
                    // FallRamp: lerp from float to full gravity
                    float rampProgress = (s.AirTimeTicks - stats.FloatWindowTicks) / (float)stats.FallRampDuration;
                    gravity = stats.AirFloatGravity + (stats.Gravity - stats.AirFloatGravity) * rampProgress;
                }
                else
                {
                    // Full gravity (also reached when both FloatWindowTicks and FallRampDuration are 0)
                    gravity = stats.Gravity;
                }

                s.VY -= gravity * TickDt;

                // Hard cap on fall speed
                if (s.VY < -stats.MaxFallSpeed)
                    s.VY = -stats.MaxFallSpeed;
            }
        }


        // ── INPUT HELPERS ──

        private static (float dirX, float dirZ) GetInputDirection(InputState input)
        {
            // Use camera-relative MoveX/MoveY
            float dx = input.MoveX;
            float dz = input.MoveY;

            float len = MathF.Sqrt((dx * dx) + (dz * dz));
            if (len > 0.001f)
            {
                dx /= len;
                dz /= len;
            }

            return (dx, dz);
        }

        // ── MATH HELPERS ──

        // (removed GetGroundSurfaceY — replaced by ArenaHeightmap.Sample)

        /// <summary>
        /// Get candidate triangle indices near a sphere at (px, py, pz).
        /// Uses the arena's spatial grid for broadphase culling.
        /// </summary>
        public static int GetCandidateTriangles(
            float px, float py, float pz, float radius,
            in ArenaDefinition arena,
            int[] outIndices)
        {
            var grid = arena.SpatialGrid;
            if (grid.CellStarts == null || grid.CellStarts.Length == 0)
                return 0;

            int ixMin = (int)((px - radius - grid.OriginX) / grid.CellSize);
            int ixMax = (int)((px + radius - grid.OriginX) / grid.CellSize);
            int iyMin = (int)((py - radius - grid.OriginY) / grid.CellSize);
            int iyMax = (int)((py + radius - grid.OriginY) / grid.CellSize);
            int izMin = (int)((pz - radius - grid.OriginZ) / grid.CellSize);
            int izMax = (int)((pz + radius - grid.OriginZ) / grid.CellSize);

            if (ixMin < 0) ixMin = 0;
            if (ixMax >= grid.CellsX) ixMax = grid.CellsX - 1;
            if (iyMin < 0) iyMin = 0;
            if (iyMax >= grid.CellsY) iyMax = grid.CellsY - 1;
            if (izMin < 0) izMin = 0;
            if (izMax >= grid.CellsZ) izMax = grid.CellsZ - 1;

            if (ixMin > ixMax || iyMin > iyMax || izMin > izMax)
                return 0;

            int count = 0;
            for (int iz = izMin; iz <= izMax; iz++)
            {
                for (int iy = iyMin; iy <= iyMax; iy++)
                {
                    for (int ix = ixMin; ix <= ixMax; ix++)
                    {
                        int cell = iz * grid.CellsX * grid.CellsY + iy * grid.CellsX + ix;
                        int start = grid.CellStarts[cell];
                        int end = grid.CellStarts[cell + 1];
                        for (int i = start; i < end; i++)
                        {
                            int ti = grid.CellTriangles[i];
                            bool dup = false;
                            for (int j = 0; j < count; j++)
                                if (outIndices[j] == ti) { dup = true; break; }
                            if (!dup)
                                outIndices[count++] = ti;
                        }
                    }
                }
            }

            return count;
        }

        private static float MoveToward(float from, float to, float delta)
        {
            if (Math.Abs(to - from) <= delta)
                return to;
            return from + (Math.Sign(to - from) * delta);
        }
    }
}
