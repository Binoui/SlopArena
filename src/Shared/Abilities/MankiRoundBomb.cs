using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's Q — Round Bomb: hold to aim a ground cursor, release to lob a bomb that
    /// explodes on impact. The hold/aim/throw lifecycle lives in
    /// <see cref="AimHoldAbility"/>; this class only builds the projectile from spec Params.
    /// </summary>
    public sealed class MankiRoundBomb : AimHoldAbility
    {
        private float _cachedAimDistance;
        private float _cachedAimYaw;

        protected override byte GetReleaseAnimIndex(CharacterDefinition def) => 1;  // spell_q_attack

        protected override void OnAimStart(ref CharacterState s, CharacterDefinition def)
        {
            _cachedAimDistance = 0f;
            _cachedAimYaw = 0f;

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiQ] OnStart slot={Slot} animLock={s.AnimLockTicks} airborne={!s.IsGrounded}");
        }

        protected override void OnRelease(ref CharacterState s, CharacterDefinition def)
        {
            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiQ] Release -> throw! ticks={s.AttackElapsedTicks} " +
                    $"aiming={s.IsAiming} charge={s.ChargeTicks}/{GetMaxHoldTicks(def)} " +
                    $"aimDist={s.AimTargetDistance:F2} aimYaw={s.AimYaw:F2}");
            _cachedAimDistance = s.AimTargetDistance;
            _cachedAimYaw = s.AimYaw;
        }

        protected override void OnFire(ref CharacterState s, CharacterDefinition def)
        {
            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
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
            float kbBase = GetParam(def, "knockback_base", 4f);
            float kbGrowth = GetParam(def, "knockback_growth", 6f);
            float explosionKbBase = GetParam(def, "explosion_kb_base", 2.4f);
            float explosionKbGrowth = GetParam(def, "explosion_kb_growth", 3.6f);
            float kbAngle = GetParam(def, "kb_angle", 30f);
            float explosionKbAngle = GetParam(def, "explosion_kb_angle", 30f);

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
                BaseKnockback = kbBase, KnockbackGrowth = kbGrowth,
                KnockbackAngle = (sbyte)kbAngle,
                StunTicks = (ushort)GetParam(def, "stun_ticks", 14f),
                DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 90f),
                OwnerId = s.EntityId,
                Gravity = g,
                Explosion = new ProjectileExplosion
                {
                    Radius = GetParam(def, "explosion_radius", 3f),
                    Damage = GetParam(def, "explosion_damage", 25f),
                    Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)explosionKbAngle, BaseKnockback = explosionKbBase, KnockbackGrowth = explosionKbGrowth },
                    StunTicks = (ushort)GetParam(def, "explosion_stun_ticks", 20f),
                    DurationTicks = (ushort)GetParam(def, "explosion_duration_ticks", 6f),
                },
            });
        }
    }
}
