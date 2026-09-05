using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's Q — Round Bomb: hold to aim a ground cursor, release to lob a bomb that
    /// explodes on impact. The hold/aim/throw lifecycle lives in
    /// <see cref="AimHoldAbility"/>; this class only builds the projectile from spec Params.
    /// </summary>
    public sealed class MankiRoundBomb : AimHoldAbility, IAimHoldCapability
    {
        private readonly CookedMankiRoundBombCapabilityParameters? _parameters;
        private float _cachedAimDistance;
        private float _cachedAimYaw;

        public MankiRoundBomb() { }
        public MankiRoundBomb(CookedMankiRoundBombCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        public MankiRoundBomb(MankiRoundBombCapabilityParameters parameters)
            : this(parameters != null ? new CookedMankiRoundBombCapabilityParameters(
                parameters.ThrowTriggerTick,
                parameters.MaxRange,
                parameters.LaunchAngle,
                parameters.Gravity,
                parameters.HitboxRadius,
                parameters.Damage,
                parameters.StunTicks,
                parameters.MaxFlightTicks,
                parameters.KbAngle,
                parameters.ExplosionDamage,
                parameters.ExplosionRadius,
                parameters.ExplosionKbBase,
                parameters.ExplosionKbGrowth,
                parameters.ExplosionStunTicks,
                parameters.ExplosionDurationTicks,
                parameters.ExplosionKbAngle) : throw new ArgumentNullException(nameof(parameters))) { }
        protected override byte GetReleaseAnimIndex(CharacterDefinition def) => 1;  // spell_q_attack

        protected override void OnAimStart(ref CharacterState s, CharacterDefinition def)
        {
            _cachedAimDistance = 0f;
            _cachedAimYaw = 0f;

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiQ] OnStart slot={Slot} animLock={s.AnimLockTicks} airborne={!s.IsGrounded}");
        }

        protected override void OnAimTick(ref CharacterState s, CharacterDefinition def)
        {
            // Track the cursor while held — the throw uses the aim at RELEASE. The
            // release tick's input carries zeroed AimDistance (the client's release
            // context only forwards the last yaw), so OnRelease must not be the only
            // place the distance is captured.
            _cachedAimDistance = s.AimTargetDistance;
            _cachedAimYaw = s.AimYaw;
        }

        protected override void OnRelease(ref CharacterState s, CharacterDefinition def)
        {
            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiQ] Release -> throw! ticks={s.AttackElapsedTicks} " +
                    $"aiming={s.IsAiming} charge={s.ChargeTicks}/{GetMaxHoldTicks(def)} " +
                    $"aimDist={s.AimTargetDistance:F2} aimYaw={s.AimYaw:F2}");
        }

        protected override void OnFire(ref CharacterState s, CharacterDefinition def)
        {
            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiQ] Projectile spawned! dist={_cachedAimDistance:F2} yaw={_cachedAimYaw:F2}rad");

            float maxRange = _parameters?.MaxRange ?? GetParam(def, "max_range", 12f);
            float D = Math.Clamp(_cachedAimDistance, 0.5f, maxRange);
            float launchAngleDeg = _parameters?.LaunchAngle ?? GetParam(def, "launch_angle", 30f);
            float g = _parameters?.Gravity ?? GetParam(def, "gravity", 30f);
            float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
            float dY = -def.CapsuleHeight * 0.5f - launchOffsetY;

            float launchRad = launchAngleDeg * (MathF.PI / 180f);
            CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
                out float _, out float hSpeed, out float vSpeed);

            float aimCos = MathF.Cos(_cachedAimYaw);
            float aimSin = MathF.Sin(_cachedAimYaw);

            float projRadius = _parameters?.HitboxRadius ?? GetParam(def, "hitbox_radius", 0.6f);
            float projDamage = _parameters?.Damage ?? GetParam(def, "damage", 8f);
            float kbBase = GetParam(def, "knockback_base", 4f);
            float kbGrowth = GetParam(def, "knockback_growth", 6f);
            float explosionKbBase = _parameters?.ExplosionKbBase ?? GetParam(def, "explosion_kb_base", 2.4f);
            float explosionKbGrowth = _parameters?.ExplosionKbGrowth ?? GetParam(def, "explosion_kb_growth", 3.6f);
            float kbAngle = _parameters?.KbAngle ?? GetParam(def, "kb_angle", 30f);
            float explosionKbAngle = _parameters?.ExplosionKbAngle ?? GetParam(def, "explosion_kb_angle", 30f);
            float explosionRadius = _parameters?.ExplosionRadius ?? GetParam(def, "explosion_radius", 3f);
            float explosionDamage = _parameters?.ExplosionDamage ?? GetParam(def, "explosion_damage", 25f);
            ushort stunTicks = _parameters?.StunTicks ?? (ushort)GetParam(def, "stun_ticks", 14f);
            ushort maxFlightTicks = _parameters?.MaxFlightTicks ?? (ushort)GetParam(def, "max_flight_ticks", 90f);
            ushort explosionStunTicks = _parameters?.ExplosionStunTicks ?? (ushort)GetParam(def, "explosion_stun_ticks", 20f);
            ushort explosionDurationTicks = _parameters?.ExplosionDurationTicks ?? (ushort)GetParam(def, "explosion_duration_ticks", 6f);

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
                DurationTicks = maxFlightTicks,
                OwnerId = s.EntityId,
                AttackSlot = (byte)(Slot + 1),
                Gravity = g,
                Explosion = new ProjectileExplosion
                {
                    Radius = explosionRadius,
                    Damage = explosionDamage,
                    Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)explosionKbAngle, BaseKnockback = explosionKbBase, KnockbackGrowth = explosionKbGrowth },
                    StunTicks = explosionStunTicks,
                    DurationTicks = explosionDurationTicks,
                },
            });
        }
    }
}
