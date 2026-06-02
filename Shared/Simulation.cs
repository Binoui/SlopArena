using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// Pure C# simulation of one tick of game logic.
    /// No Godot dependencies — usable by Server, Client, and AI.
    ///
    /// Architecture:
    ///   SimulateTick() processes ONE tick (1/60s) of movement + combat
    ///   for a single character. It takes the current CharacterState,
    ///   mutates it to the next tick, and fires hit events via callback.
    ///
    ///   Hit detection uses SpellResolver (Shared/) — pure math.
    ///
    /// Usage (client):  SimulateTick(ref state, def, input, arena) → apply to Godot body
    /// Usage (server):  SimulateTick(ref state, def, input, arena) → broadcast state
    /// </summary>
    public static class Simulation
    {
        public const float TickDt = 1f / 60f;

        // ── Constants ──
        private const float AirDrag = 0.2f;
        private const float AirDodgeSpeed = 35.0f;
        private const float KnockbackDecayPerTick = 0.1333f; // 8.0/s at 60Hz
        private const float KnockbackMinGravity = 9.8f;
        private const byte MaxAirDodges = 1;

        // Dash duration: 0.25 second = 15 ticks
        public const ushort DashDurationTicks = 15;
        private const ushort DashInvincibilityTicks = 30; // full duration invincible

        // Tick-duration constants
        private const ushort AirDodgeDurationTicks = 7;     // ~0.117s
        private const ushort SprintThresholdTicks = 12;     // 0.2s
        private const ushort TurnaroundLagTicks = 6;        // 0.1s

        // Floor height (flat arena ground at Y=0)
        public const float FloorHeight = 0f;

        // ── MAIN ENTRY POINT ──

        /// <summary>
        /// Process one simulation tick for a character.
        /// Mutates state in-place.
        /// When combat hits occur, calls onHit for each entity hit.
        /// </summary>
        public static void SimulateTick(
            ref CharacterState s,
            CharacterDefinition def,
            InputState input,
            ArenaDefinition arena,
            Action<ulong, float, float, float, float>? onHit = null)
        {
            var stats = def.Movement;

            // 1. Tick all timers
            TickTimers(ref s);

            // 2. Knockback overrides everything (but dash invincibility still applies)
            if (HasKnockback(s))
            {
                ProcessKnockback(ref s, arena);
                return;
            }

            // 3. State machine
            if (s.State == ActionState.Dashing)
                ProcessDash(ref s, stats);
            else if (s.State == ActionState.AirDodging)
                ProcessAirDodge(ref s);

            if (s.State == ActionState.Idle || s.State == ActionState.Attacking)
            {
                ProcessNormalMovement(ref s, stats, input);
            }

            // 4. Gravity
            ApplyGravity(ref s, stats);

            // 5. Position update (Euler integration)
            s.PX += s.VX * TickDt;
            s.PZ += s.VZ * TickDt;
            s.PY += s.VY * TickDt;

            // 6. Ground collision (flat floor at VoidHeight)
            if (s.IsGrounded && s.PY <= FloorHeight + 0.1f)
            {
                s.PY = FloorHeight;
                s.VY = 0f;
            }

            // 7. Void death check
            if (s.PY < arena.KillHeight)
            {
                RespawnCharacter(ref s, arena);
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

            // Combo expiry
            if (s.ComboTimerTicks == 0)
                s.ComboStage = 0;

            // Cooldowns
            if (s.Cooldown0 > 0) s.Cooldown0--;
            if (s.Cooldown1 > 0) s.Cooldown1--;
            if (s.Cooldown2 > 0) s.Cooldown2--;
            if (s.Cooldown3 > 0) s.Cooldown3--;
            if (s.Cooldown4 > 0) s.Cooldown4--;
            if (s.Cooldown5 > 0) s.Cooldown5--;
        }

        // ── KNOCKBACK ──

        private static bool HasKnockback(CharacterState s)
        {
            return (s.KVX * s.KVX + s.KVY * s.KVY + s.KVZ * s.KVZ) > 0.0001f;
        }

        private static void ProcessKnockback(ref CharacterState s, ArenaDefinition arena)
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
            s.IsGrounded = s.PY <= FloorHeight + 0.1f;

            if (s.IsGrounded)
            {
                s.PY = FloorHeight;
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

        private static void ProcessAirDodge(ref CharacterState s)
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

            bool hasInput = (dirX * dirX + dirZ * dirZ) > 0.0001f;

            if (hasInput)
            {
                // Detect direction change
                bool hadDir = (s.LastDirX * s.LastDirX + s.LastDirZ * s.LastDirZ) > 0.0001f;
                bool dirChanged = hadDir && (dirX * s.LastDirX + dirZ * s.LastDirZ) < 0.5f;

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

                // Jump
                if (input.Jump && s.JumpsLeft > 0)
                {
                    s.VY = stats.JumpForce;
                    s.JumpsLeft--;
                    s.IsGrounded = false;
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
            float drag = AirDrag * TickDt;
            s.VX *= (1f - drag);
            s.VZ *= (1f - drag);

            // Dash initiation is handled by PlayerController outside of Simulation
            // (works both ground and air, has cooldown, grants invincibility)

            // Double jump: press space in air to use remaining jump
            if (input.Jump && s.JumpsLeft > 0)
            {
                s.VY = stats.JumpForce;
                s.JumpsLeft--;
            }

            UpdateFacing(ref s);
        }

        // ── AIR DODGE INITIATION ──

        private static void StartAirDodge(ref CharacterState s, float dirX, float dirZ)
        {
            s.State = ActionState.AirDodging;
            s.StateTicks = AirDodgeDurationTicks;
            s.AirDodgesLeft--;

            float len = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
            if (len > 0.001f)
            {
                s.VX = (dirX / len) * AirDodgeSpeed;
                s.VZ = (dirZ / len) * AirDodgeSpeed;
            }
            else
            {
                // No input = backward dodge (relative to facing)
                s.VX = 0f;
                s.VZ = -AirDodgeSpeed * 0.5f;
            }
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
            float len = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
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
        /// </summary>
        public static void ApplyKnockback(ref CharacterState s, float kbX, float kbY, float kbZ)
        {
            float kbScale = 1f + (s.DamagePercent * 0.01f);
            s.KVX = kbX * kbScale;
            s.KVY = kbY * kbScale;
            s.KVZ = kbZ * kbScale;
            s.State = ActionState.Idle;
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
            float len = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
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
            float hSpeedSq = s.VX * s.VX + s.VZ * s.VZ;
            if (hSpeedSq > 0.01f)
            {
                s.FacingYaw = MathF.Atan2(s.VX, s.VZ);
            }
        }

        // ── RESPAWN ──

        private static void RespawnCharacter(ref CharacterState s, ArenaDefinition arena)
        {
            var spawn = arena.SpawnPoints.Length > 0 ? arena.SpawnPoints[0] : default;
            s.DamagePercent = 0;
            s.PX = spawn.X;
            s.PY = spawn.Y;
            s.PZ = spawn.Z;
            s.VX = s.VY = s.VZ = 0f;
            s.KVX = s.KVY = s.KVZ = 0f;
            s.State = ActionState.Idle;
            s.StateTicks = 0;
            s.JumpsLeft = 2;
            s.AirDodgesLeft = MaxAirDodges;
            s.IsGrounded = false;
            s.ComboStage = 0;
            s.ComboTimerTicks = 0;
            s.AnimLockTicks = 0;
            s.DashCooldownTicks = 0;
            s.DashDurationTicks = 0;
            s.InvincibilityTicks = 0;
            s.DirHoldTicks = 0;
            s.IsSprinting = false;
            s.TurnaroundTicks = 0;
            s.FacingYaw = spawn.Yaw;
        }

        // ── INPUT HELPERS ──

        private static (float dirX, float dirZ) GetInputDirection(InputState input)
        {
            // Use camera-relative MoveX/MoveY
            float dx = input.MoveX;
            float dz = input.MoveY;

            float len = MathF.Sqrt(dx * dx + dz * dz);
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
            return from + Math.Sign(to - from) * delta;
        }
    }
}
