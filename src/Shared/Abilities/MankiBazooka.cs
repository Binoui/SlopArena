using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's R (slot 4): Bazooka — rise high into the air (5m max, less if already airborne),
    /// downward, fire a projectile on release.
    ///
    /// FSM (matches animator generator pattern):
    ///   ComboStage=0 (Rising → Aiming): shows spell_r_start, auto-transitions to
    ///     spell_r_loop when start anim finishes. Maintains height, shows ground
    ///     indicator. On release → ComboStage=1.
    ///   ComboStage=1 (Firing): shows spell_r_end, spawns projectile at trigger tick,
    ///     then ends.
    /// </summary>
    public sealed class MankiBazooka : ServerAbility
    {
        private bool _projectileSpawned;
        private float _cachedAimDistance;
        private float _cachedAimYaw;
        private float _startPY;
        private bool _riseComplete;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _projectileSpawned = false;
            _cachedAimDistance = 0f;
            _cachedAimYaw = 0f;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            _startPY = s.PY;

            float riseHeight = GetParam(def, "rise_height", 5f);
            float riseVelocity = GetParam(def, "rise_velocity", 14f);

            // Already airborne above rise height — skip the rise, go straight to hover-aim
            if (!s.IsGrounded && s.PY >= riseHeight)
            {
                _riseComplete = true;
                s.AnimLockTicks = 180;
                SetVelocityInFacing(ref s, 0f, 0f);

                if (Simulation.OnDebugLog != null)
                    Simulation.OnDebugLog.Invoke(
                        $"[MankiBazooka] Airborne above height — skipping rise");
            }
            else
            {
                _riseComplete = false;
                s.AnimLockTicks = 50;
                SetVelocityInFacing(ref s, 0f, riseVelocity);
            }

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiBazooka] OnStart slot={Slot+1} startPY={_startPY:F2} riseVel={riseVelocity}");
        }

        public override void OnEnd(ref CharacterState s)
        {
            s.IsAiming = false;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            if (s.ComboStage == 0)
            {
                TickRiseAndAim(ref s, input, def);
            }
            else
            {
                TickFiring(ref s, input, def);
            }
        }

        private void TickRiseAndAim(ref CharacterState s, InputState input, CharacterDefinition def)
        {
            float riseHeight = GetParam(def, "rise_height", 10f);
            ushort minRiseTicks = (ushort)GetParam(def, "min_rise_ticks", 8f);
            bool reachedHeight = s.PY >= _startPY + riseHeight;
            bool minTimeElapsed = s.AttackElapsedTicks >= minRiseTicks;

            // Maintain IsAiming so ground indicator stays visible (sim may override from input)
            s.IsAiming = true;
            if (!_riseComplete)
            {
                if (reachedHeight && minTimeElapsed)
                {
                    _riseComplete = true;
                    // Hover in place
                    SetVelocityInFacing(ref s, 0f, 0f);
                    s.AnimLockTicks = 180; // allow indefinite aiming

                    if (Simulation.OnDebugLog != null)
                        Simulation.OnDebugLog.Invoke(
                            $"[MankiBazooka] Rise complete -> Aiming at height={s.PY:F2}");
                }
                // AnimIndex stays 0 (spell_r_start) — HasExitTime transition
                // takes us to spell_r_loop automatically
            }

            // Check for release: input.IsAiming goes false when player lifts R key
            ushort maxHoldTicks = (ushort)GetParam(def, "charge_hold_ticks", 180f);

            // But don't allow release during initial rise (minRiseTicks needed)
            if (_riseComplete && (!input.IsAiming || (maxHoldTicks > 0 && s.ChargeTicks >= maxHoldTicks)))
            {
                _cachedAimDistance = s.AimTargetDistance;
                _cachedAimYaw = s.AimYaw;

                s.ComboStage = 1;
                AnimIndex = 2; // spell_r_end
                s.AttackElapsedTicks = 0;
                s.AnimLockTicks = 50;

                if (Simulation.OnDebugLog != null)
                    Simulation.OnDebugLog.Invoke(
                        $"[MankiBazooka] Release -> Firing! " +
                        $"aimDist={_cachedAimDistance:F2} yaw={_cachedAimYaw:F2}");
            }
        }

        private void TickFiring(ref CharacterState s, InputState input, CharacterDefinition def)
        {
            ushort triggerTick = (ushort)GetParam(def, "fire_trigger_tick", 5f);

            if (!_projectileSpawned && s.AttackElapsedTicks >= triggerTick)
            {
                _projectileSpawned = true;
                s.IsAiming = false;

                if (Simulation.OnDebugLog != null)
                    Simulation.OnDebugLog.Invoke(
                        $"[MankiBazooka] Firing projectile! dist={_cachedAimDistance:F2} yaw={_cachedAimYaw:F2}");

                // Compute target position from cached aim data
                float maxRange = GetParam(def, "max_aim_range", 20f);
                float aimDist = Math.Clamp(_cachedAimDistance, 0.5f, maxRange);
                if (_cachedAimDistance <= 0.001f)
                    aimDist = 5f; // default

                float aimCos = MathF.Cos(_cachedAimYaw);
                float aimSin = MathF.Sin(_cachedAimYaw);
                float targetX = s.PX + aimDist * aimSin;
                float targetZ = s.PZ + aimDist * aimCos;

                // Compute projectile velocity toward ground target
                float dx = targetX - s.PX;
                float dz = targetZ - s.PZ;
                float hDist = MathF.Sqrt((dx * dx) + (dz * dz));

                float g = GetParam(def, "projectile_gravity", 10f);
                float speed = GetParam(def, "projectile_speed", 35f);

                float vx, vy, vz;
                if (hDist > 0.01f)
                {
                    float hSpeed = speed;
                    float vSpeed = -speed * 0.4f; // slight downward component
                    vx = (dx / hDist) * hSpeed;
                    vz = (dz / hDist) * hSpeed;
                    vy = vSpeed;
                }
                else
                {
                    vx = 0f;
                    vz = 0f;
                    vy = -speed * 0.6f;
                }

                float projRadius = GetParam(def, "hitbox_radius", 1.5f);
                float projDamage = GetParam(def, "damage", 15f);
                ApplyBuffBonuses(ref s, ref projDamage, ref projRadius);

                Resolver.Spawn(new Hitbox
                {
                    X = s.PX,
                    Y = s.PY + 0.8f,
                    Z = s.PZ,
                    VX = vx,
                    VY = vy,
                    VZ = vz,
                    Radius = projRadius,
                    Shape = HitboxShape.Sphere,
                    EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                    Damage = projDamage,
                    BaseKnockback = GetParam(def, "knockback_base", 8f),
                    KnockbackGrowth = GetParam(def, "knockback_growth", 12f),
                    KnockbackUpward = GetParam(def, "knockback_upward", 12f),
                    StunTicks = (ushort)GetParam(def, "stun_ticks", 25f),
                    DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 60f),
                    OwnerId = s.EntityId,
                    Gravity = g,
                    Explosion = new ProjectileExplosion
                    {
                        Radius = GetParam(def, "explosion_radius", 3f),
                        Damage = GetParam(def, "explosion_damage", 25f),
                        BaseKnockback = GetParam(def, "explosion_kb_base", 7.2f),
                        KnockbackGrowth = GetParam(def, "explosion_kb_growth", 10.8f),
                        KnockbackUpward = GetParam(def, "explosion_knockback_upward", 14f),
                        StunTicks = (ushort)GetParam(def, "explosion_stun_ticks", 20f),
                        DurationTicks = (ushort)GetParam(def, "explosion_duration_ticks", 6f),
                    },
                });
            }

            ushort duration = (ushort)GetParam(def, "throw_duration", 60f);
            if (s.AttackElapsedTicks >= duration)
            {
                if (Simulation.OnDebugLog != null)
                    Simulation.OnDebugLog.Invoke(
                        $"[MankiBazooka] Ability end (firing complete)");
                EndAbility(ref s);
            }
        }
    }
}
