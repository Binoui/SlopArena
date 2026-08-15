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

        // ADR-0019 §6 post-hitstun flight law (InPostHitstunFlight):
        // flight gravity 14 m/s² (raised from 8 in the 2026-08-14 feel pass — launches
        // dropped too slowly, felt floaty) + linear horizontal friction 10 m/s², applied
        // while the victim is airborne from a launch until landing or any action.
        // Hardcoded, not MovementStats — the balance pass can promote them. Public so
        // tools/MoveDataReport prints the live values.
        public const float FlightGravity = 14f;
        public const float FlightFriction = 10f;

        // ADR-0019 balance pass: velocity-only scale on the damage/weight formula.
        // Hitstun is computed from the UNSCALED magnitude so combo timing (stun vs IASA,
        // the combo matrix) is preserved while launch distance shrinks. Tune with
        // tools/MoveDataReport: scripts/move-data.sh fightguy. 1.0 = raw formula.
        public const float KbScaleFactor = 0.14f;
        private const byte MaxAirDodges = 1;

        /// <summary>
        /// Dash duration: 0.25 second = 15 ticks
        /// </summary>
        public const ushort DashDurationTicks = 15;
        /// <summary>
        /// Dash i-frame window (ADR-0020 v2): invincibility covers only the START of the
        /// dash, so dodging through an attack is possible but timing-tight. The dash itself
        /// (DashDurationTicks) runs longer than this — the tail is vulnerable.
        /// </summary>
        private const ushort DashInvincibilityTicks = 4;

        /// <summary>
        /// Short-hop release window in ticks (issue #116 / ADR-0016): releasing the jump key
        /// within this many ticks of the press produces a reduced jump. Digital-optimal timing
        /// tech; tune 3-5 in playtest per ADR-0016.
        /// </summary>
        public const byte ShortHopWindowTicks = 5;

        /// <summary>
        /// Horizontal speed dead zone. Below this, velocity is snapped to zero
        /// to prevent residual drifting from asymptotic friction decay.
        /// </summary>
        private const float VelocityDeadZone = 0.015f;
        /// <summary>
        /// Release brake on the ground (ADR-0020): how fast a Run decelerates to zero once
        /// input is released. A passive coast, slower than the decisive Turnaround pivot.
        /// A Rush release stops instantly (no drift at all).
        /// </summary>
        private const float GroundStopFriction = 36f;

        /// <summary>
        /// Run-reversal (Turnaround) deceleration (ADR-0020): the pivot skid. Much faster
        /// than the coast `GroundFriction` so reversing reads as a short, decisive pivot —
        /// ~0.2 s / ~1.4 m to stop from full run speed (Melee feel).
        /// </summary>
        private const float TurnaroundFriction = 70f;

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
        /// Horizontal search radius for ledge grab (meters).
        /// 0.8m ≈ character width — avoids magnetic pull across small platforms.
        /// </summary>
        private const float LedgeSnapRange = 0.8f;
        /// <summary>
        /// Max Y below surface edge to grab.
        /// Prevents grab from deep below the stage.
        /// </summary>
        private const float LedgeGrabTolerance = 2.5f;
        /// <summary>Invincibility ticks granted on a ledge grab.</summary>
        internal const ushort LedgeGrabInvincibilityTicks = 6;
        private const ushort LedgeRegrabLockDurationTicks = 30;
        private const float LedgeDropSpeed = 3f;

        /// Resolve the effective AttackStage from an AbilitySpec, clamping ComboStage.
        public static AttackStage ResolveStage(AbilitySpec spec, in CharacterState state)
        {
            int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
            return spec.Stages[stageIdx];
        }

        /// <summary>
        /// IASA early-out (issue #124 / ADR-0021 §1): true when the current attack's stage
        /// has passed its <c>IasaTicks</c>. From that tick on, ability inputs AND the dash
        /// interrupt the recovery (the jab → IASA → dash → dash-attack string). 0 = none
        /// (full ADR-0014 lock — the pre-IASA behavior). Never true outside Attacking.
        /// </summary>
        internal static bool IsIasaUnlocked(CharacterState state, CharacterDefinition def)
        {
            if (state.State != ActionState.Attacking || state.AttackSlot == 0) return false;
            var spec = def.GetSlotAbility(state.AttackSlot - 1, !state.IsGrounded);
            if (spec?.Stages is not { Length: > 0 }) return false;
            var stage = ResolveStage(spec, state);
            if (stage.IasaTicks == 0) return false;
            return ElapsedInStage(state, spec) >= stage.IasaTicks;
        }

        /// <summary>
        /// Current stage's elapsed ticks for an attacking entity. AttackElapsedTicks counts
        /// ticks since the last stage reset; stage-driven moves (StageChainAbility) never
        /// reset it mid-attack, so subtracting prior stages' durations yields the current
        /// stage's elapsed. Charge abilities reset it at their mid-attack stage transition
        /// (ChargeAttackAbility/AimHoldAbility), which underflows the subtraction — fall back
        /// to the raw clock (elapsed since the transition). Shared by the IASA check and the
        /// landing-lag auto-cancel windows.
        /// </summary>
        internal static int ElapsedInStage(CharacterState state, AbilitySpec? spec)
        {
            if (spec?.Stages is not { Length: > 0 }) return 0;
            int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
            int elapsed = state.AttackElapsedTicks;
            for (int i = 0; i < stageIdx; i++)
                elapsed -= spec.Stages[i].DurationTicks;
            return elapsed < 0 ? state.AttackElapsedTicks : elapsed;
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
            bool wasGrounded = s.IsGrounded;   // airborne→grounded detection for the Rush reset

            // ── Burst (ADR-0014): dual-use escape/extender. Runs before the hitstop gate —
            // the freeze is the decision window. Cooldown + recovery gate re-use. ──
            if (input.Burst && s.LandingLagTicks == 0 && s.BurstRecoveryTicks == 0 && s.BurstCooldownTicks == 0)
            {
                bool attacking = (s.State is ActionState.Attacking or ActionState.Aiming) && s.AnimLockTicks > 0;
                if (s.HitstopTicks > 0)
                {
                    // Frozen: a defender (launch queued — or any non-attacker, since the queue is
                    // server-local and absent on predicted tracks) escapes; an attacker frozen by
                    // their own connecting hit (no queue) cancels offensively.
                    if (attacking && !HasQueuedLaunch(s)) DoOffensiveBurst(ref s);
                    else DoDefensiveBurst(ref s);
                }
                else if (s.State == ActionState.Hitstun || HasKnockback(s)) DoDefensiveBurst(ref s);
                else if (attacking) DoOffensiveBurst(ref s);
            }

            // Apply combat aim yaw from input (degrees * 100 → radians)
            // FacingYaw (movement-facing) is handled by ProcessNormalMovement via Atan2
            float aimDeg = input.AimYaw * 0.01f;
            s.AimYaw = aimDeg * (MathF.PI / 180f);
            // Store aim target distance (cm → m) for projectile abilities
            s.AimTargetDistance = input.AimDistance * 0.01f;
            // Apply combat aim pitch from input (degrees * 100 → radians)
            s.AimPitch = input.AimPitch * 0.01f * (MathF.PI / 180f);

            // ── Hitstop (ADR-0012): per-pair freeze. While frozen, capture the defender's
            // Combo Influence input, decrement, and skip the state machine, timers, and physics.
            // The launch queued at hit connect applies at freeze expiry.
            if (s.HitstopTicks > 0)
            {
                if (input.MoveX != 0f || input.MoveY != 0f)
                {
                    if (!s.SdiApplied)
                    {
                        ApplySdi(ref s, input.MoveX, input.MoveY);
                        s.SdiApplied = true;
                    }
                    s.DIX = input.MoveX;
                    s.DIY = input.MoveY;
                }
                s.HitstopTicks--;
                if (s.HitstopTicks == 0)
                {
                    // Freeze expired — apply the queued launch, if any was queued.
                    // All-zero queue = the hit never resolved in this sim (prediction tracks
                    // don't run the attacker's ability instances) — skip rather than write a
                    // bogus zero-KB launch; the next authoritative packet corrects.
                    if (s.QueuedKVOverride)
                    {
                        // OnHitEntity rewrote the launch at connect (NetherGrasp yank):
                        // restore the exact snapshot — KV was untouched during the freeze.
                        s.KVX = s.QueuedKVX; s.KVY = s.QueuedKVY; s.KVZ = s.QueuedKVZ;
                        float kvMag = MathF.Sqrt(
                            (s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ));
                        if (s.QueuedKBStun > 0 && kvMag > 0f)
                        {
                            s.HitstunTicks = (ushort)Math.Clamp((int)(0.5f * kvMag), 1, ushort.MaxValue);
                            s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 :
                                s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
                            s.State = ActionState.Hitstun;
                        }
                        else
                        {
                            s.HitstunTicks = 0;
                            s.State = ActionState.Idle;
                        }
                        if (s.KVY > 0f) s.IsGrounded = false;
                        s.AirTimeTicks = 0;
                        s.DashDurationTicks = 0;
                        s.StateTicks = 0;
                        s.WasAirborneDuringKnockback = !s.IsGrounded;
                        s.InPostHitstunFlight = false;
                    }
                    else if (s.QueuedKBResolvedForce)
                    {
                        ApplyKnockbackForce(ref s, s.QueuedKBDirX, s.QueuedKBDirZ,
                            s.QueuedKBAngle, s.QueuedKBForce, s.QueuedKBStun);
                    }
                    else if (!s.QueuedKBZero && (s.QueuedKBBase != 0f || s.QueuedKBGrowth != 0f || s.QueuedKBDamage != 0f || s.QueuedKBStun > 0))
                    {
                        ApplyKnockback(ref s, s.QueuedKBDirX, s.QueuedKBDirZ, s.QueuedKBAngle,
                            s.QueuedKBBase, s.QueuedKBGrowth, s.QueuedKBDamage,
                            s.QueuedKBStun, def.Weight);
                    }
                    else
                    {
                        s.KVX = 0f; s.KVY = 0f; s.KVZ = 0f;
                        s.HitstunTicks = 0;
                        s.State = ActionState.Idle;
                    }
                    ApplyDirectionalInfluence(ref s);
                    s.SdiApplied = false;
                    s.QueuedKBZero = false;
                    s.QueuedKVX = 0f; s.QueuedKVY = 0f; s.QueuedKVZ = 0f;
                    s.QueuedKBDirX = 0f; s.QueuedKBDirZ = 0f; s.QueuedKBAngle = 0;
                    s.QueuedKBBase = 0f; s.QueuedKBGrowth = 0f; s.QueuedKBDamage = 0f; s.QueuedKBForce = 0f; s.QueuedKBResolvedForce = false; s.QueuedKBStun = 0;
                }
                return;
            }

            // Short-hop hold counter (issue #116): consecutive ticks the jump key is held.
            // Reset on release. Serialized on the wire so rollback replay of a JumpSquat
            // opponent is byte-identical (ADR-0011).
            if (input.JumpHeld && s.JumpHeldTicks < 255) s.JumpHeldTicks++;
            else s.JumpHeldTicks = 0;

            // 2.5 JumpSquat: tick down, apply jump force on expiry
            if (s.State == ActionState.JumpSquat)
            {
                if (s.StateTicks > 0) s.StateTicks--;
                if (s.StateTicks == 0)
                {
                    // Short-hop decision (issue #116 / ADR-0016): releasing within
                    // ShortHopWindowTicks of the press yields a reduced jump. If the player
                    // is STILL holding inside the window at squat expiry, the decision is
                    // pending — hold the squat one tick at a time until the release (short)
                    // or the window elapses (full). The deferral is bounded by the window.
                    bool withinWindow = s.JumpHeldTicks <= ShortHopWindowTicks;
                    if (input.JumpHeld && withinWindow)
                    {
                        // decision pending — stay in squat, re-check next tick
                    }
                    else
                    {
                        float force = withinWindow
                            ? stats.ShortHopForce
                            : stats.JumpForce;
                        s.VY = force;
                        s.IsGrounded = false;
                        s.State = ActionState.Idle;
                        s.AirTimeTicks = stats.FloatWindowTicks;
                    }
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
                else if (s.State == ActionState.LedgeHang)
                    ProcessLedgeHang(ref s, stats, input, arena, def);
                // Attacking state is now purely handled by ServerSimulation.TickAbilities
            }

            // NOTE: no ground friction during Attacking (issue #115) — attacks preserve
            // drift and lunge momentum; friction resumes when the ability returns to Idle.



            // 5.5 Consume buffered input (any lock just expired)
            if (s.BufferedSlot > 0 && s.AnimLockTicks == 0 && s.HitstunTicks == 0 &&
                s.BurstRecoveryTicks == 0 && s.LandingLagTicks == 0 &&
                (s.State == ActionState.Idle || s.State == ActionState.Run) && !input.Jump && !input.Dash)
            {
                byte slot = s.BufferedSlot;
                // Issue #117: grounded-only moves (no air spec) buffered while airborne must
                // NOT consume into a stuck Attacking placeholder — drop the buffer instead.
                // The ServerAbility path re-resolves the spec on the next PreTickAbilities.
                if (def.GetSlotAbility(slot - 1, !s.IsGrounded) == null)
                {
                    s.BufferedSlot = 0;
                }
                else
                {
                    s.BufferedSlot = 0;
                    // Ability activation handled by ServerSimulation.Tick pre-sim phase
                    s.State = ActionState.Attacking;
                    s.AttackSlot = slot;
                }
            }

            // 5.75 Jump detection (unconditional except hitstun / already squatting /
            // in landing lag — the lag is a hard no-input lock, issue #125)
            if (input.Jump && s.JumpsLeft > 0 && s.AnimLockTicks == 0 && s.HitstunTicks == 0 && s.BurstRecoveryTicks == 0 && s.LandingLagTicks == 0 && s.State != ActionState.JumpSquat && s.State != ActionState.Aiming && s.State != ActionState.LedgeHang)
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
                    // Double jump — acting mid-tail ends the flight regime (ADR-0019 §6):
                    // a jump after a launch is a normal jump, not a floaty one.
                    s.VY = stats.JumpForce * stats.AirJumpVMultiplier;
                    (float dirX, float dirZ) = GetInputDirection(input);
                    s.VX += dirX * stats.AirSpeedMax * stats.AirJumpHMultiplier;
                    s.VZ += dirZ * stats.AirSpeedMax * stats.AirJumpHMultiplier;
                    s.JumpsLeft--;
                    s.AirTimeTicks = stats.FloatWindowTicks;
                    s.InPostHitstunFlight = false;
                }
            }

            else if (input.Jump)
            {
                string reason = s.AnimLockTicks > 0 ? "anim_lock" :
                    s.LandingLagTicks > 0 ? "landing_lag" :
                    s.HitstunTicks > 0 ? "hitstun" :
                    s.State == ActionState.JumpSquat ? "already_squatting" :
                    s.JumpsLeft <= 0 ? "no_jumps" : "unknown";
                OnDebugLog?.Invoke($"[JumpBlocked] input.Jump=true but blocked by {reason}");
            }

            // 6. Input-driven actions (only when not locked by animation, landing lag or in
            // jump squat; aiming blocks dash — the ability owns movement until release).
            // The dash unlocks on IASA (ADR-0021 §1): a normal whose stage has passed its
            // IasaTicks may be dash-cancelled out of recovery, even while AnimLockTicks is
            // still counting down. Attack activation below stays Idle/Run-gated, so this
            // term only ever opens the DASH here.
            if (s.LandingLagTicks == 0 && (s.AnimLockTicks == 0 || IsIasaUnlocked(s, def)) && s.State != ActionState.Hitstun && s.State != ActionState.JumpSquat && s.State != ActionState.Aiming && s.State != ActionState.LedgeHang)
            {
                // Jump — handled inside ProcessNormalMovement/ProcessAirMovement
                // Dash
                if (input.Dash && s.DashDurationTicks == 0 && s.DashCooldownTicks == 0)
                {
                    StartDash(ref s, stats, input.MoveX, input.MoveY);
                }

                if (input.ActiveSlot > 0 && (s.State == ActionState.Idle || s.State == ActionState.Run) && s.BurstRecoveryTicks == 0)
                {
                    ushort cd = s.GetCooldown(input.ActiveSlot);
                    if (cd == 0)
                    {
                        s.State = ActionState.Attacking;
                        s.AttackSlot = input.ActiveSlot;
                        s.StateTicks = 0;
                    }
                }
            }
            // Buffer input if locked within window
            // NOTE: Combo buffering is now handled by ServerAbility.Tick lifecycle
            // Only general input buffering (unlock window) is kept for client prediction.
            // Landing lag never buffers (issue #125): the lock is a hard no-input window —
            // a press inside it is dropped, like Melee, not queued for unlock.
            if (input.ActiveSlot > 0 && s.LandingLagTicks == 0 && (s.AnimLockTicks > 0 || s.HitstunTicks > 0 || s.BurstRecoveryTicks > 0 || s.State == ActionState.JumpSquat) && s.BufferedSlot == 0)
            {
                // General buffer: within window of unlock
                if (s.State == ActionState.JumpSquat ||
                    (s.AnimLockTicks > 0 && s.AnimLockTicks <= InputBufferWindow) ||
                    (s.HitstunTicks > 0 && s.HitstunTicks <= InputBufferWindow) ||
                    (s.BurstRecoveryTicks > 0 && s.BurstRecoveryTicks <= InputBufferWindow))
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
            // Landing lag (issue #125): "no input, no movement" — the stick cannot steer
            // during the lock, even once the aerial has ended and the state is Idle.
            if (s.LandingLagTicks == 0 && (s.State == ActionState.Idle || s.State == ActionState.Aiming || s.State == ActionState.Run))
            {
                ProcessNormalMovement(ref s, stats, input, processInput: !fixedAim);
            }

            // 6c. Facing snap (LMB, ADR-0017 / issue #126): utility input honored at the
            // input gate — instant facing to the camera azimuth (AimYaw), usable when not
            // attack-locked, not in hitstun, not in burst recovery / landing lag / jump
            // squat / aim stance. Runs AFTER normal movement so the snap wins the tick it
            // is pressed (on the ground, the next tick's movement re-faces — one-tick
            // turnaround for poke spacing). Air facing is sticky, so an air snap holds
            // until the next snap / lock / landing.
            // While locked (ADR-0018 / issue #127), an accepted snap exits the lock —
            // the manual-facing button is the "break free" escape hatch. Rejected snaps
            // (mid-attack, hitstun) leave the lock untouched.
            if (input.FaceToCamera && s.LandingLagTicks == 0 && s.AnimLockTicks == 0
                && s.BurstRecoveryTicks == 0 && s.State != ActionState.Hitstun
                && s.State != ActionState.JumpSquat && s.State != ActionState.Aiming)
            {
                s.FacingYaw = input.AimYaw * 0.01f * (MathF.PI / 180f);
                s.LockOn = false;
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
                ApplyGravity(ref s, stats, input);
            
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
                else if (s.PY <= groundY + PlatformLandTolerance
                    && (s.PY >= groundY - PlatformSnapTolerance || s.PY < groundY))
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

            // Landing resets to a fresh Rush window (ADR-0020): the first reversal after
            // landing is an instant dash, not a Turnaround (Melee resets to a dash on land).
            if (!wasGrounded && s.IsGrounded) s.RushTicks = stats.RushTicks;

            // 11. Landing cleanup
            if (s.State == ActionState.AirDodging && s.IsGrounded)
            {
                s.State = ActionState.Idle;
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
            if (s.LandingLagTicks > 0) s.LandingLagTicks--;
            if (s.HitstunTicks > 0) s.HitstunTicks--;
            if (s.BurstCooldownTicks > 0) s.BurstCooldownTicks--;
            if (s.BurstRecoveryTicks > 0) s.BurstRecoveryTicks--;
            if (s.AttackElapsedTicks < 65535) s.AttackElapsedTicks++;

            // Rush window ticks (ADR-0020): counts ONLY while the fighter is purely
            // moving in one direction on the ground (Run). Any other action — attack,
            // jump, dash, hitstun, aim — freezes it; landings and ability activations
            // refill it. The fighter stays in Rush through footsies and only falls
            // into Run (slow Turnaround) after a long same-direction hold.
            if (s.RushTicks > 0 && s.IsGrounded && s.State == ActionState.Run)
                s.RushTicks--;
            if (s.LedgeRegrabLockTicks > 0) s.LedgeRegrabLockTicks--;

            // State ticks (generic expiry — JumpSquat is handled specially below)
            if (s.StateTicks > 0 && s.State != ActionState.JumpSquat)
            {
                s.StateTicks--;
                if (s.StateTicks == 0 && s.State != ActionState.Idle)
                {
                    s.State = ActionState.Idle;
                }
            }

            // Cooldowns (all 11 slots — issue #117; slots 6-10 got fields in #116 but the
            // decrement only covered 1-6, so their cooldowns never expired).
            for (byte slot = 1; slot <= AbilitySlots.Count; slot++)
            {
                ushort cd = s.GetCooldown(slot);
                if (cd > 0) s.SetCooldown(slot, (ushort)(cd - 1));
            }

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
            // ADR-0019: constant knockback velocity during hitstun. Position is NOT
            // integrated here — the caller falls through to the generic position update
            // (step 9) + ground collision (step 10), which run for Hitstun states. Early
            // versions integrated here too, double-moving the victim by 2x KV*dt per tick.
            s.VX = s.KVX;
            s.VY = s.KVY;
            s.VZ = s.KVZ;
            if (s.VY > 0f) s.IsGrounded = false;

            // Post-hitstop input updates DI only. SDI is exclusively committed
            // during hitstop and applied at the freeze boundary.
            if (input.MoveX != 0f || input.MoveY != 0f)
            {
                s.DIX = input.MoveX;
                s.DIY = input.MoveY;
            }

            if (s.HitstunTicks == 0)
            {
                // Hitstun expiry applies ASDI and transitions to actionable state.
                s.PX += s.DIX * 0.4f;
                s.PZ += s.DIY * 0.4f;
                s.DIX = 0f;
                s.DIY = 0f;
                s.SdiApplied = false;
                s.VX = s.KVX;
                s.VY = s.KVY;
                s.VZ = s.KVZ;
                s.KVX = 0f;
                s.KVY = 0f;
                s.KVZ = 0f;
                s.InPostHitstunFlight = true;
                s.State = ActionState.Idle;
            }
        }
        public static void ApplySdi(ref CharacterState s, float dx, float dz)
        {
            s.PX += dx * 0.4f;
            s.PZ += dz * 0.4f;
        }

        public static void ApplyDirectionalInfluence(ref CharacterState s)
        {
            float mag = MathF.Sqrt(s.KVX * s.KVX + s.KVY * s.KVY + s.KVZ * s.KVZ);
            float inputMag = MathF.Sqrt(s.DIX * s.DIX + s.DIY * s.DIY);
            if (mag <= 0.0001f || inputMag <= 0.0001f) return;
            float tx = s.DIX / inputMag;
            float tz = s.DIY / inputMag;
            float horizontal = MathF.Sqrt(s.KVX * s.KVX + s.KVZ * s.KVZ);
            if (horizontal <= 0.0001f)
            {
                float elevation = 18f * MathF.PI / 180f;
                float signY = s.KVY >= 0f ? 1f : -1f;
                s.KVX = tx * mag * MathF.Sin(elevation);
                s.KVZ = tz * mag * MathF.Sin(elevation);
                s.KVY = signY * mag * MathF.Cos(elevation);
                return;
            }
            float hx = s.KVX / horizontal;
            float hz = s.KVZ / horizontal;
            float dot = Math.Clamp(hx * tx + hz * tz, -1f, 1f);
            float angle = MathF.Acos(dot);
            float turn = MathF.Min(angle, 18f * MathF.PI / 180f * MathF.Sin(angle) * MathF.Sin(angle));
            float cross = hx * tz - hz * tx;
            float sign = cross >= 0f ? 1f : -1f;
            float c = MathF.Cos(turn * sign);
            float sn = MathF.Sin(turn * sign);
            s.KVX = (hx * c - hz * sn) * horizontal;
            s.KVZ = (hx * sn + hz * c) * horizontal;
        }

        // ── KNOCKBACK ──

        internal static bool HasKnockback(CharacterState s)
        {
            return ((s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ)) > 0.0001f;
        }

        /// <summary>Find a grabbable ledge for an off-grid state. Mirrors the old TryLedgeSnap
        /// geometry: entity off-grid, a cardinal neighbour ±LedgeSnapRange has surface, and PY is
        /// within [ledgeY - LedgeGrabTolerance, ledgeY + 0.5]. Returns the ledge surface world Y
        /// (surfaceY), the unit inward direction (inwardX/Z), and the ledge surface sample point
        /// (edgeX/Z).</summary>
        internal static bool FindLedge(CharacterState s, ArenaDefinition arena, float capsuleHalf,
            out float surfaceY, out float inwardX, out float inwardZ, out float edgeX, out float edgeZ)
        {
            surfaceY = 0f; inwardX = 0f; inwardZ = 0f; edgeX = 0f; edgeZ = 0f;
            if (s.IsGrounded) return false;

            float centerSurface = arena.Heightmap.Data != null
                ? arena.Heightmap.Sample(s.PX, s.PZ)
                : float.MinValue;
            if (centerSurface > float.MinValue)
                return false; // over a platform — normal ground collision handles it

            // Four cardinal neighbours, X axis then Z axis. The inward direction is the
            // sign of the offset: the stage is on the side that has surface.
            float n = arena.Heightmap.Data != null ? arena.Heightmap.Sample(s.PX + LedgeSnapRange, s.PZ) : float.MinValue;
            if (n > float.MinValue && s.PY >= (n + capsuleHalf) - LedgeGrabTolerance && s.PY <= (n + capsuleHalf) + 0.5f)
            {
                surfaceY = n; inwardX = 1f; edgeX = s.PX + LedgeSnapRange; edgeZ = s.PZ; return true;
            }
            n = arena.Heightmap.Data != null ? arena.Heightmap.Sample(s.PX - LedgeSnapRange, s.PZ) : float.MinValue;
            if (n > float.MinValue && s.PY >= (n + capsuleHalf) - LedgeGrabTolerance && s.PY <= (n + capsuleHalf) + 0.5f)
            {
                surfaceY = n; inwardX = -1f; edgeX = s.PX - LedgeSnapRange; edgeZ = s.PZ; return true;
            }
            n = arena.Heightmap.Data != null ? arena.Heightmap.Sample(s.PX, s.PZ + LedgeSnapRange) : float.MinValue;
            if (n > float.MinValue && s.PY >= (n + capsuleHalf) - LedgeGrabTolerance && s.PY <= (n + capsuleHalf) + 0.5f)
            {
                surfaceY = n; inwardZ = 1f; edgeX = s.PX; edgeZ = s.PZ + LedgeSnapRange; return true;
            }
            n = arena.Heightmap.Data != null ? arena.Heightmap.Sample(s.PX, s.PZ - LedgeSnapRange) : float.MinValue;
            if (n > float.MinValue && s.PY >= (n + capsuleHalf) - LedgeGrabTolerance && s.PY <= (n + capsuleHalf) + 0.5f)
            {
                surfaceY = n; inwardZ = -1f; edgeX = s.PX; edgeZ = s.PZ - LedgeSnapRange; return true;
            }
            return false;
        }

        /// <summary>Occupied LedgeHang state: recompute the held ledge from position each tick
        /// (no stored ledge state). Three escapes — jump, W (stand onto the stage), S (drop) —
        /// else stay hanging. Lost the ledge → fall.</summary>
        private static void ProcessLedgeHang(ref CharacterState s, MovementStats stats,
            InputState input, ArenaDefinition arena, CharacterDefinition def)
        {
            float capsuleHalf = def.CapsuleHeight * 0.5f;
            if (!FindLedge(s, arena, capsuleHalf, out float surfaceY, out float inwardX, out float inwardZ, out _, out _))
            {
                s.State = ActionState.Idle;
                s.IsGrounded = false;
                return;
            }
            (float dirX, float dirZ) = GetInputDirection(input);
            float toward = dirX * inwardX + dirZ * inwardZ;   // >0 toward stage, <0 away
            if (input.Jump && s.JumpsLeft > 0)
            {
                s.State = ActionState.JumpSquat;
                s.StateTicks = stats.JumpSquatTicks;
                s.JumpsLeft--;
                s.InvincibilityTicks = 0;
            }
            else if (toward > 0.5f)
            {
                // W = stand onto the stage
                s.IsGrounded = true;
                s.PY = surfaceY + capsuleHalf;
                s.PX += inwardX * (LedgeSnapRange + def.CapsuleRadius);
                s.PZ += inwardZ * (LedgeSnapRange + def.CapsuleRadius);
                s.VX = s.VY = s.VZ = 0f;
                s.State = ActionState.Idle;
                s.InvincibilityTicks = 0;
            }
            else if (toward < -0.5f)
            {
                // S = drop
                s.State = ActionState.Idle;
                s.IsGrounded = false;
                s.VY = -LedgeDropSpeed;
                s.InvincibilityTicks = 0;
                s.LedgeRegrabLockTicks = LedgeRegrabLockDurationTicks;
            }
            // else: stay hanging (no-op)
        }

        private static void ProcessKnockback(ref CharacterState s, ArenaDefinition arena, CharacterDefinition def)
        {
            // ADR-0019 post-hitstun flight: linear horizontal friction only;
            // knockback vertical velocity is preserved while flight gravity
            // affects the integrated vertical velocity.
            float horizontal = MathF.Sqrt(s.KVX * s.KVX + s.KVZ * s.KVZ);
            float retained = MathF.Max(0f, horizontal - 10f * TickDt);
            if (horizontal > 0.0001f)
            {
                float scale = retained / horizontal;
                s.KVX *= scale;
                s.KVZ *= scale;
            }

            if (!s.IsGrounded)
                s.KVY -= 8f * TickDt;
            s.VX = s.KVX;
            s.VY = s.KVY;
            s.VZ = s.KVZ;

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
                s.IsGrounded = s.KVY <= 0f
                    && s.PY <= groundY + PlatformLandTolerance
                    && (wasAirborne || s.PY >= groundY - PlatformSnapTolerance);
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
                // Constant dash velocity — no decay. Ground a dash's VY so it never dips
                // into a fall mid-dash; the horizontal velocity is left untouched.
                s.VY = Math.Max(s.VY, 0f);
            }
            else
            {
                // Dash expired (ADR-0020 v2). Grounded: hard stop — the burst is the move
                // (wavedash), no coast. Airborne: preserve horizontal momentum so the dash
                // remains an approach tool; air friction decays it from here.
                if (s.IsGrounded)
                {
                    s.VX = 0f;
                    s.VZ = 0f;
                }
                s.State = ActionState.Idle;
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
                    float friction = stats.AirFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, friction);
                    s.VZ = MoveToward(s.VZ, 0f, friction);
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
            s.InPostHitstunFlight = false;
            // Run/Idle are the locomotion states this method manages. Aiming (mobile aim,
            // e.g. Kistu E) also routes through here but must keep its own state.
            bool isLocomotion = s.State == ActionState.Idle || s.State == ActionState.Run;

            bool hasInput = ((dirX * dirX) + (dirZ * dirZ)) > 1e-4f;

            if (!hasInput)
            {
                bool inRush = s.RushTicks > 0;   // capture before reset
                s.RushTicks = 0;
                if (inRush && s.State == ActionState.Run)
                {
                    // Rush release: a tap is a fixed burst, not a slide — stop dead.
                    // Gated on Run: the stop is a dash-tap property. A fighter at rest
                    // in Idle — e.g. just finished a move with lunge drift — brakes
                    // instead, so end-of-move momentum carries into Idle
                    // (MomentumPreserve). The window refresh from ability activation
                    // must not turn every attack into a dead stop on release.
                    s.VX = 0f;
                    s.VZ = 0f;
                }
                else
                {
                    // Run release: brake to a stop — fast, no semi-truck drift.
                    float friction = GroundStopFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, friction);
                    s.VZ = MoveToward(s.VZ, 0f, friction);
                }
                if (isLocomotion) s.State = ActionState.Idle;
                ApplyVelocityDeadZone(ref s);
                s.LastDirX = s.LastDirZ = 0f;
                return; // facing unchanged
            }

            // Starting from a standstill opens the Rush window (ADR-0020): a fixed
            // dash-dance window during which reversals are instant and velocity is set
            // to cruise speed immediately (no soft-start ramp — Melee's initial dash).
            // A perpendicular redirect (90° axis change) also keeps the fighter in the
            // window; only holding a steady direction lets it expire into Run. Reversals
            // are deliberately excluded — at Run they stay a Turnaround skid.
            bool wasStopped = (s.LastDirX == 0f && s.LastDirZ == 0f);
            float dirChangeDot = (s.LastDirX * dirX) + (s.LastDirZ * dirZ);
            if (wasStopped || MathF.Abs(dirChangeDot) < 0.5f) s.RushTicks = stats.RushTicks;

            float speed = MathF.Sqrt((s.VX * s.VX) + (s.VZ * s.VZ));
            float facingX = MathF.Sin(s.FacingYaw);
            float facingZ = MathF.Cos(s.FacingYaw);
            bool turnInput = (dirX * facingX + dirZ * facingZ) < -0.5f;   // input opposes previous facing

            if (turnInput && s.RushTicks > 0)
            {
                // Rush reversal (ADR-0020): instant full-speed flip, no turn lag (the
                // Melee dash-dance). Facing re-faces below; the window restarts so the
                // fighter stays in Rush as long as it keeps reversing.
                s.VX = dirX * stats.RunSpeed;
                s.VZ = dirZ * stats.RunSpeed;
                s.RushTicks = stats.RushTicks;
                if (isLocomotion) s.State = ActionState.Run;
            }
            else
            {
                bool pivot = speed > VelocityDeadZone && (s.VX * dirX + s.VZ * dirZ) < 0f;   // velocity opposes input
                if (pivot)
                {
                    // Turnaround (Run reversal at cruise): friction-through-zero — the
                    // pivot skid. Decelerates hard (TurnaroundFriction) so the pivot is a
                    // short, decisive turn, not an ice slide; still slower than the Rush flip.
                    float friction = TurnaroundFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, friction);
                    s.VZ = MoveToward(s.VZ, 0f, friction);
                    if (isLocomotion) s.State = ActionState.Run;
                }
                else if (speed > stats.RunSpeed)
                {
                    // SA Dash → Run coast
                    float friction = stats.GroundFriction * TickDt;
                    s.VX = MoveToward(s.VX, dirX * stats.RunSpeed, friction);
                    s.VZ = MoveToward(s.VZ, dirZ * stats.RunSpeed, friction);
                    if (isLocomotion) s.State = ActionState.Run;
                }
                else if (s.RushTicks > 0)
                {
                    // Rush kick-off / hold: cruise speed immediately (no ramp).
                    s.VX = dirX * stats.RunSpeed;
                    s.VZ = dirZ * stats.RunSpeed;
                    if (isLocomotion) s.State = ActionState.Run;
                }
                else
                {
                    // Run hold / Turnaround recovery. The soft-start accel recovers from a
                    // Turnaround (velocity parallel to input). Any perpendicular component
                    // is a redirect: snap to the input direction at current speed, dropping
                    // the perpendicular (no diagonal drag) — ADR-0020.
                    float perp = (s.VX * dirZ) - (s.VZ * dirX);
                    if (MathF.Abs(perp) > VelocityDeadZone)
                    {
                        s.VX = dirX * speed;
                        s.VZ = dirZ * speed;
                    }
                    else
                    {
                        float accel = (stats.RunAccelerationA + stats.RunAccelerationB) * TickDt;
                        s.VX = MoveToward(s.VX, dirX * stats.RunSpeed, accel);
                        s.VZ = MoveToward(s.VZ, dirZ * stats.RunSpeed, accel);
                    }
                    if (isLocomotion) s.State = ActionState.Run;
                }
            }

            ApplyVelocityDeadZone(ref s);
            s.LastDirX = dirX;
            s.LastDirZ = dirZ;
            // Facing follows movement direction — even under target lock, which only
            // steers facing during attacks (per-stage RotateTowardTarget, ADR-0018 /
            // issue #127). Only reached with movement input (early-return above).
            s.FacingYaw = MathF.Atan2(dirX, dirZ);
        }

        private static void ProcessAirMovement(
            ref CharacterState s, MovementStats stats,
            InputState input, float dirX, float dirZ)
        {
            s.IsGrounded = false;

            bool hasInput = ((dirX * dirX) + (dirZ * dirZ)) > 1e-4f;
            if (hasInput)
            {
                float accel = (stats.AirAccelStick + stats.AirAccelBase) * TickDt;
                s.VX = MoveToward(s.VX, dirX * stats.AirSpeedMax, accel);
                s.VZ = MoveToward(s.VZ, dirZ * stats.AirSpeedMax, accel);
            }
            else
            {
                // ADR-0019 §6: post-hitstun flight uses the sharper 10 m/s² azimuth friction.
                float friction = (s.InPostHitstunFlight ? FlightFriction : stats.AirFriction) * TickDt;
                s.VX = MoveToward(s.VX, 0f, friction);
                s.VZ = MoveToward(s.VZ, 0f, friction);
            }
            ApplyVelocityDeadZone(ref s);

            // Air facing is sticky (ADR-0017, issue #126): it locks at takeoff (last
            // ground facing) and drift / camera rotation never re-face the fighter
            // mid-air. Air normals are deterministic — attack direction = the faced
            // direction, changed only by the LMB facing snap or a target lock
            // (ADR-0018). The old velocity-facing overwrite is what made drift re-face
            // the fighter every frame and is deliberately gone.
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

            // Set velocity toward target: constant speed at RunSpeed
            // (auto-run feel — matched to character movement speed)
            float dist = MathF.Sqrt(distSq);
            s.VX = (dx / dist) * def.Movement.RunSpeed;
            s.VZ = (dz / dist) * def.Movement.RunSpeed;
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
            if (s.BurstRecoveryTicks > 0) return; // ADR-0014: burst recovery blocks dash
            if (s.DashCooldownTicks > 0) return;
            if (s.State != ActionState.Idle && s.State != ActionState.Attacking && s.State != ActionState.Dashing && s.State != ActionState.Run) return;
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
            s.InvincibilityTicks = DashInvincibilityTicks; // i-frames only at the start (tight dodge)
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
        /// Apply knockback using the ADR-0019 damage/weight formula.
        /// Magnitude = (base + growth * (damage% / 100 + 1) + damage * 0.1)
        /// * 200 / (weight + 100). StunTicks is a zero/nonzero gate only.
        /// </summary>
        public static void ApplyKnockback(ref CharacterState s, float dirX, float dirZ,
            sbyte angleDeg, float baseKB, float growthKB, float damage,
            ushort stunTicks, float weight, bool applyScale = true)
        {
            s.LandingLagTicks = 0;
            float mass = MathF.Max(0.01f, weight + 100f);
            float magnitude = (baseKB + growthKB * (s.DamagePercent * 0.01f + 1f)
                + damage * 0.1f) * 200f / mass;
            float rad = angleDeg * MathF.PI / 180f;
            float cosA = MathF.Cos(rad);
            float sinA = MathF.Sin(rad);

            s.KVX = dirX * magnitude * cosA;
            s.KVY = magnitude * sinA;
            s.KVZ = dirZ * magnitude * cosA;
            if (s.KVY > 0f)
                s.IsGrounded = false;

            // Hitstun from the UNSCALED magnitude (KbScaleFactor below scales velocity only).
            float kbMagnitude = MathF.Sqrt(
                (s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ));
            if (stunTicks > 0 && kbMagnitude > 0f)
            {
                s.HitstunTicks = (ushort)Math.Clamp((int)(0.5f * kbMagnitude), 1, ushort.MaxValue);
                s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 :
                    s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
                s.State = ActionState.Hitstun;
            }
            else
            {
                s.HitstunTicks = 0;
                s.HitstunLevel = 0;
                s.State = ActionState.Idle;
            }

            // Velocity-only scale (KbScaleFactor) — launch distance, not hitstun.
            // applyScale:false is for fixed tools (grabs) that opt out of the hit-KB balance.
            if (applyScale)
            {
                s.KVX *= KbScaleFactor;
                s.KVY *= KbScaleFactor;
                s.KVZ *= KbScaleFactor;
            }

            s.AirTimeTicks = 0;
            s.DashDurationTicks = 0;
            s.StateTicks = 0;
            s.WasAirborneDuringKnockback = !s.IsGrounded;
        }

        /// <summary>Apply a fully resolved launch force supplied by an ability hook.</summary>
        public static void ApplyKnockbackForce(ref CharacterState s, float dirX, float dirZ,
            sbyte angleDeg, float force, ushort stunTicks)
        {
            s.LandingLagTicks = 0;
            float rad = angleDeg * MathF.PI / 180f;
            float cosA = MathF.Cos(rad);
            float sinA = MathF.Sin(rad);
            s.KVX = dirX * force * cosA;
            s.KVY = force * sinA;
            s.KVZ = dirZ * force * cosA;
            if (s.KVY > 0f) s.IsGrounded = false;
            float magnitude = MathF.Sqrt(s.KVX * s.KVX + s.KVY * s.KVY + s.KVZ * s.KVZ);
            if (stunTicks > 0 && magnitude > 0f)
            {
                s.HitstunTicks = (ushort)Math.Clamp((int)(0.5f * magnitude), 1, ushort.MaxValue);
                s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 : s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
                s.State = ActionState.Hitstun;
            }
            else
            {
                s.HitstunTicks = 0;
                s.HitstunLevel = 0;
                s.State = ActionState.Idle;
            }
            s.AirTimeTicks = 0;
            s.DashDurationTicks = 0;
            s.StateTicks = 0;
            s.WasAirborneDuringKnockback = !s.IsGrounded;
        }

        // ── Burst (ADR-0014) ──

        private static bool HasQueuedLaunch(CharacterState s)
            => s.QueuedKBZero || s.QueuedKBBase != 0f || s.QueuedKBGrowth != 0f || s.QueuedKVOverride || s.QueuedKBStun > 0;

        private static void DoDefensiveBurst(ref CharacterState s)
        {
            // Cancel the pending launch entirely (hitstop path) + break any lock + full stop.
            s.HitstopTicks = 0;
            s.QueuedKVOverride = false;
            s.QueuedKBZero = false;
            s.QueuedKVX = s.QueuedKVY = s.QueuedKVZ = 0f;
            s.QueuedKBDirX = s.QueuedKBDirZ = 0f;
            s.QueuedKBAngle = 0;
            s.QueuedKBBase = 0f; s.QueuedKBGrowth = 0f; s.QueuedKBStun = 0;
            s.HitstunTicks = 0;
            s.KVX = s.KVY = s.KVZ = 0f;
            s.VX = s.VY = s.VZ = 0f;
            s.State = ActionState.Idle;
            s.InvincibilityTicks = BurstConfig.DefensiveInvincibilityTicks; // startup telegraph beats the triggering hit
            s.BurstRecoveryTicks = BurstConfig.DefensiveRecoveryTicks;
            s.BurstCooldownTicks = BurstConfig.CooldownTicks;
            s.BurstPending = 1; // ServerSimulation shoves the last attacker
        }

        private static void DoOffensiveBurst(ref CharacterState s)
        {
            s.AnimLockTicks = 0;
            s.AttackElapsedTicks = 0;
            s.ComboStage = 0;                       // LMB chain resets to stage 1
            s.AttackSlot = 0;                       // signals TickAbilities to drop the ability (interrupt, no OnEnd)
            s.State = ActionState.Idle;
            s.BurstRecoveryTicks = BurstConfig.OffensiveRecoveryTicks;
            s.BurstCooldownTicks = BurstConfig.CooldownTicks;
            s.BurstPending = 2;                     // ServerSimulation spawns the forward hitbox
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
        
        private static void ApplyGravity(ref CharacterState s, MovementStats stats, InputState input)
        {
            if (!s.IsGrounded)
            {
                // Increment AirTime each tick while airborne
                if (s.AirTimeTicks < ushort.MaxValue)
                    s.AirTimeTicks++;

                // Fast fall (issue #116 / #107): holding Down in the air sets a fixed
                // downward velocity — no gravity this tick. Applies in every airborne state
                // except hitstun (ApplyGravity is skipped entirely for Hitstun) and only
                // while already falling. Release cancels naturally: the gate is per-tick.
                if (input.Down && s.HitstunTicks == 0 && s.VY < 0f)
                {
                    s.VY = -stats.FastFallSpeed;
                    return;
                }

                // Post-hitstun flight (ADR-0019 §6): flight gravity 8, no float window —
                // the victim is still "in the launch" until they land or act.
                float gravity = s.InPostHitstunFlight
                    ? FlightGravity
                    : // Float-window-only gravity: reduced during the window, full afterwards
                      // (the FallRamp lerp is gone — ADR-0020).
                      (s.AirTimeTicks < stats.FloatWindowTicks)
                        ? stats.AirFloatGravity
                        : stats.Gravity;

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
