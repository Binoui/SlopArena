using System;

namespace SlopArena.Shared.Abilities
{
    public sealed class MankiRoundBomb : ServerAbility
    {
        private bool _projectileSpawned;
        private float _cachedAimDistance;
        private float _cachedAimYaw;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _projectileSpawned = false;
            _cachedAimDistance = 0f;
            _cachedAimYaw = 0f;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            s.AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            s.AnimLockTicks = 8;
            s.ChargeTicks = 0;

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiQ] OnStart slot={Slot} animLock={s.AnimLockTicks} airborne={!s.IsGrounded}");
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort maxHoldTicks = (ushort)GetParam(def, "charge_hold_ticks", 180f);
            bool dbg = Simulation.OnDebugLog != null;

            if (s.ComboStage == 0)
            {
                if (s.AttackElapsedTicks > 8 && s.AnimIndex != 1)
                {
                    s.AnimIndex = 1;
                    if (dbg) Simulation.OnDebugLog?.Invoke(
                        $"[MankiQ] Enter loop: ticks={s.AttackElapsedTicks} charge={s.ChargeTicks}");
                }

                if (s.AttackElapsedTicks > 8 && (!input.IsAiming || (maxHoldTicks > 0 && s.ChargeTicks >= maxHoldTicks)))
                {
                    if (dbg) Simulation.OnDebugLog?.Invoke(
                        $"[MankiQ] Release -> throw! ticks={s.AttackElapsedTicks} " +
                        $"aiming={input.IsAiming} charge={s.ChargeTicks}/{maxHoldTicks} " +
                        $"aimDist={s.AimTargetDistance:F2} aimYaw={s.AimYaw:F2}");
                    _cachedAimDistance = s.AimTargetDistance;
                    _cachedAimYaw = s.AimYaw;
                    s.ComboStage = 1;
                    s.AnimIndex = 2;
                    s.AttackElapsedTicks = 0;
                }
                return;
            }

            if (dbg && s.AttackElapsedTicks == 0)
                Simulation.OnDebugLog?.Invoke($"[MankiQ] Throw phase start");

            ushort throwTick = (ushort)GetParam(def, "throw_trigger_tick", 10f);

            if (!_projectileSpawned && s.AttackElapsedTicks >= throwTick)
            {
                _projectileSpawned = true;
                s.IsAiming = false;

                if (dbg) Simulation.OnDebugLog?.Invoke(
                    $"[MankiQ] Projectile spawned! dist={_cachedAimDistance:F2} yaw={_cachedAimYaw:F2}rad");

                float D = Math.Clamp(_cachedAimDistance, 0.5f, GetParam(def, "max_range", 12f));
                float launchAngleDeg = GetParam(def, "launch_angle", 30f);
                float g = GetParam(def, "gravity", 30f);
                float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
                float dY = -def.CapsuleHeight * 0.5f - launchOffsetY;

                float launchRad = launchAngleDeg * (MathF.PI / 180f);
                CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
                    out float _, out float hSpeed, out float vSpeed);

                float aimCos = MathF.Cos(_cachedAimYaw);
                float aimSin = MathF.Sin(_cachedAimYaw);

                float projRadius = GetParam(def, "hitbox_radius", 0.6f);
                float projDamage = GetParam(def, "damage", 8f);
                ApplyBuffBonuses(ref s, ref projDamage, ref projRadius);

                Resolver.Spawn(new Hitbox
                {
                    X = s.PX,
                    Y = s.PY + launchOffsetY,
                    Z = s.PZ,
                    VX = hSpeed * aimSin,
                    VY = vSpeed,
                    VZ = hSpeed * aimCos,
                    Radius = projRadius,
                    Shape = HitboxShape.Sphere,
                    EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                    Damage = projDamage,
                    KnockbackForce = GetParam(def, "knockback_force", 10f),
                    KnockbackUpward = GetParam(def, "knockback_upward", 6f),
                    StunTicks = (ushort)GetParam(def, "stun_ticks", 14f),
                    DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 90f),
                    OwnerId = s.EntityId,
                    Gravity = g,
                    Explosion = new ProjectileExplosion
                    {
                        Radius = GetParam(def, "explosion_radius", 3f),
                        Damage = GetParam(def, "explosion_damage", 25f),
                        KnockbackForce = GetParam(def, "explosion_knockback_force", 18f),
                        KnockbackUpward = GetParam(def, "explosion_knockback_upward", 12f),
                        StunTicks = (ushort)GetParam(def, "explosion_stun_ticks", 20f),
                        DurationTicks = (ushort)GetParam(def, "explosion_duration_ticks", 6f),
                    },
                });
            }

            ushort duration = (ushort)GetParam(def, "throw_duration", 60f);
            if (s.AttackElapsedTicks >= duration)
            {
                if (dbg) Simulation.OnDebugLog?.Invoke(
                    $"[MankiQ] Ability end (throw complete)");
                EndAbility(ref s);
            }
        }
    }
}
