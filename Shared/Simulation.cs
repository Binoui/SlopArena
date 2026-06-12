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
        public const float TickDt = 1f / 60f;

        /// <summary>
        /// ── Constants ──
        /// </summary>
        private const float AirDrag = 0.2f;
        /// <summary>
        /// 8.0/s at 60Hz
        /// </summary>
        private const float KnockbackDecayPerTick = 0.1333f;
        private const float KnockbackMinGravity = 9.8f;
        private const byte MaxAirDodges = 1;

        /// <summary>
        /// Dash duration: 0.25 second = 15 ticks
        /// </summary>
        public const ushort DashDurationTicks = 15;
        /// <summary>
        /// full duration invincible
        /// </summary>
        private const ushort DashInvincibilityTicks = 30;

        /// <summary>
        /// 0.2s
        /// </summary>
        private const ushort SprintThresholdTicks = 12;
        /// <summary>
        /// 0.1s
        /// </summary>
        private const ushort TurnaroundLagTicks = 6;

        /// <summary>
        /// Floor height (flat arena ground at Y=0)
        /// </summary>
        public const float FloorHeight = 0f;

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

            // 1. Tick all timers
            TickTimers(ref s);

            // 2. Hitstun overrides everything (DI window)
            if (s.State == ActionState.Hitstun)
            {
                ProcessHitstun(ref s, input);
                return;
            }

            // 3. Knockback overrides everything (but dash invincibility still applies)
            if (HasKnockback(s))
            {
                ProcessKnockback(ref s, arena, def);
                return;
            }

            // 4. State machine
            if (s.State == ActionState.Dashing)
                ProcessDash(ref s, stats);
            else if (s.State == ActionState.AirDodging)
                ProcessAirDodge();

            if (s.State == ActionState.Idle || s.State == ActionState.Attacking)
            {
                ProcessNormalMovement(ref s, stats, input);
            }

            // 5. Gravity
            ApplyGravity(ref s, stats);

            // 6. Position update (Euler integration)
            s.PX += s.VX * TickDt;
            s.PZ += s.VZ * TickDt;
            s.PY += s.VY * TickDt;

            // 7. Ground collision (flat floor)
            float groundY = arena.FloorHeight + def.CapsuleHeight * 0.5f;
            if (s.PY <= groundY + 0.1f)
            {
                s.IsGrounded = true;
                s.PY = groundY;
                s.VY = 0f;
            }
            else
            {
                s.IsGrounded = false;
            }

            // 8. Landing cleanup
            if (s.State == ActionState.AirDodging && s.IsGrounded)
            {
                s.State = ActionState.Idle;
            }
        }

        // ── TIMERS ──

        private static void TickTimers(ref CharacterState s)
        {
            if (s.DashCooldownTicks > 0) s.DashCooldownTicks--;
            if (s.DashDurationTicks > 0) s.DashDurationTicks--;
            if (s.InvincibilityTicks > 0) s.InvincibilityTicks--;
            if (s.AnimLockTicks > 0) s.AnimLockTicks--;
            if (s.ComboTimerTicks > 0) s.ComboTimerTicks--;
            if (s.HitstunTicks > 0) s.HitstunTicks--;

            // Turnaround ticks
            if (s.TurnaroundTicks > 0) s.TurnaroundTicks--;

            // State ticks
            if (s.StateTicks > 0)
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
        }

        // ── HITSTUN + DI (Directional Influence) ──

        /// <summary>
        /// Process hitstun state: freeze victim for a few frames, then apply knockback with DI.
        /// DI (Directional Influence) = held input direction at END of hitstun modifies knockback.
        /// </summary>
        private static void ProcessHitstun(ref CharacterState s, InputState input)
        {
            // Victim is frozen in place during hitstun
            s.VX = 0f;
            s.VZ = 0f;
            // Keep vertical velocity (gravity still applies if airborne)

            // Store current input (will use the LAST frame's input for DI)
            // Player has the full hitstun window to react and hold a direction
            s.DIX = input.MoveX;
            s.DIY = input.MoveY;

            // When hitstun ends, apply knockback modified by held direction
            if (s.HitstunTicks == 0)
            {
                // Apply DI influence to knockback direction
                // DI modifies trajectory based on held direction at end of hitstun
                const float DIStrength = 3.5f; // Strong influence
                s.KVX += s.DIX * DIStrength;
                s.KVZ += s.DIY * DIStrength; // MoveY maps to Z in world space

                // Reset DI
                s.DIX = 0f;
                s.DIY = 0f;

                // Exit hitstun, enter knockback state
                s.State = ActionState.Idle; // Will transition to knockback via HasKnockback check
            }
        }

        // ── KNOCKBACK ──

        private static bool HasKnockback(CharacterState s)
        {
            return ((s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ)) > 0.0001f;
        }

        private static void ProcessKnockback(ref CharacterState s, ArenaDefinition arena, CharacterDefinition def)
        {
            // Decay knockback
            s.KVX -= s.KVX * KnockbackDecayPerTick;
            s.KVY -= s.KVY * KnockbackDecayPerTick;
            s.KVZ -= s.KVZ * KnockbackDecayPerTick;

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

            // Ground check
            bool wasAirborne = !s.IsGrounded;
            float groundY = arena.FloorHeight + def.CapsuleHeight * 0.5f;
            s.IsGrounded = s.PY <= groundY + 0.1f;

            if (s.IsGrounded)
            {
                s.PY = groundY;
                s.VY = 0f;
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
                // Dash: maintain horizontal speed, stop vertical momentum
                s.VX = s.DashDirX * stats.DashSpeed;
                s.VZ = s.DashDirZ * stats.DashSpeed;
                s.VY = Math.Max(s.VY, 0f); // stop falling during dash (ground & air)
            }
            else
            {
                s.State = ActionState.Idle;
                // Stop completely — no slide/momentum after dash
                s.VX = 0f;
                s.VZ = 0f;
            }
            UpdateFacing(ref s);
        }

        // ── AIR DODGE ──

        private static void ProcessAirDodge()
        {
            // Air dodge maintains its velocity (set once when initiated)
            // Natural drift/end handled by state tick expiry
        }

        // ── NORMAL MOVEMENT ──

        private static void ProcessNormalMovement(
            ref CharacterState s, MovementStats stats, InputState input)
        {
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
                    s.VZ = MoveToward(s.VZ, 0f, Math.Abs(s.VZ) * friction);
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

            // Jump — applied by simulation (IsGrounded tracks properly)
            if (input.Jump && s.JumpsLeft > 0)
            {
                s.VY = stats.JumpForce;
                s.JumpsLeft--;
                s.IsGrounded = false;
            }

            UpdateFacing(ref s);
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

            // Dash initiation is handled by PlayerController outside of Simulation
            // (works both ground and air, has cooldown, grants invincibility)

            // Double jump: press space in air to use remaining jump
            if (input.Jump && s.JumpsLeft > 0)
            {
                s.VY = stats.JumpForce;
                s.VX = dirX * stats.WalkSpeed;
                s.VZ = dirZ * stats.WalkSpeed;
                s.JumpsLeft--;
            }

            UpdateFacing(ref s);
        }

        // ── DASH INITIATION ──

        /// <summary>
        /// Start a dash (ground or air). 1 second duration, grants invincibility.
        /// Can be used on ground or in air.
        /// </summary>
        public static void StartDash(ref CharacterState s, MovementStats stats, float dirX, float dirZ)
        {
            if (s.DashCooldownTicks > 0) return;
            if (s.State != ActionState.Idle && s.State != ActionState.Attacking && s.State != ActionState.Dashing) return;
            if (s.InvincibilityTicks > 0) return; // already invincible
            if (HasKnockback(s)) return;

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
            s.DashDurationTicks = DashDurationTicks;
            s.DashCooldownTicks = stats.DashCooldownTicks;
            s.InvincibilityTicks = DashInvincibilityTicks; // invincible for full dash
            s.State = ActionState.Dashing;
            s.StateTicks = DashDurationTicks;

            s.VX = dirX * stats.DashSpeed;
            s.VZ = dirZ * stats.DashSpeed;
            s.VY = Math.Max(s.VY, 0f);
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
        /// Apply knockback force, scaled by damage percent (Smash-style).
        /// Knockback = baseForce * (1 + DamagePercent * 0.01)
        /// So at 0%: 1x, at 100%: 2x, at 200%: 3x, etc.
        /// If baseForce is ~0 (no-KB moves), knockback stays minimal.
        /// Triggers hitstun first (freeze + DI window), then knockback applies.
        /// </summary>
        public static void ApplyKnockback(ref CharacterState s, float kbX, float kbY, float kbZ)
        {
            float kbScale = 1f + (s.DamagePercent * 0.01f);

            // Store scaled knockback (will be applied after hitstun)
            s.KVX = kbX * kbScale;
            s.KVY = kbY * kbScale;
            s.KVZ = kbZ * kbScale;

            // Calculate hitstun duration based on knockback strength
            float kbMagnitude = MathF.Sqrt((s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ));

            // Hitstun formula: 8-20 frames depending on knockback strength
            // Weak hits: 8 frames, Strong hits: 20+ frames
            ushort hitstunFrames = (ushort)Math.Clamp(8 + (int)(kbMagnitude * 0.5f), 8, 25);

            // Only apply hitstun if knockback is significant (not weak jabs)
            if (kbMagnitude > 3f)
            {
                s.HitstunTicks = hitstunFrames;
                s.State = ActionState.Hitstun;
            }
            else
            {
                // Weak hit: skip hitstun, apply knockback immediately
                s.State = ActionState.Idle;
            }

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
                // Float system:
                // - During attack animation (AnimLockTicks > 0): no gravity, maintain position
                // - During dash: no gravity, maintains horizontal momentum
                // - After float ends: gravity resumes naturally from current VY (starts slow)
                if (s.AnimLockTicks > 0 || s.State == ActionState.Dashing || s.State == ActionState.Attacking)
                {
                    // Gentle downward drift during float (not full gravity)
                    // Attack float: almost zero movement
                    if (s.VY > -3f)
                        s.VY -= 6f * TickDt; // ~6 m/s² drift (vs full gravity 35-42)
                }
                else
                {
                    s.VY -= stats.Gravity * TickDt;
                }

                // Hard cap on fall speed
                if (s.VY < -stats.MaxFallSpeed)
                    s.VY = -stats.MaxFallSpeed;
            }
        }

        // ── FACING ──

        private static void UpdateFacing(ref CharacterState s)
        {
            float hSpeedSq = (s.VX * s.VX) + (s.VZ * s.VZ);
            if (hSpeedSq > 0.01f)
            {
                s.FacingYaw = MathF.Atan2(s.VX, s.VZ);
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

        private static float MoveToward(float from, float to, float delta)
        {
            if (Math.Abs(to - from) <= delta)
                return to;
            return from + (Math.Sign(to - from) * delta);
        }
    }
}
