using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's A — Ki Shot: a fixed-startup camera-directed ki projectile.
    /// </summary>
    public sealed class FightGuyKiShot : ServerAbility
    {
        private readonly CookedKiShotCapabilityParameters _parameters;
        private ushort _ticks;
        private float _cachedAimYaw;
        private float _cachedAimPitch;

        public FightGuyKiShot(CookedKiShotCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _cachedAimYaw = s.AimYaw;
            _cachedAimPitch = s.AimPitch;
            s.State = ActionState.Attacking;
            s.IsAiming = false;
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;
            if (_ticks != _parameters.StartupTicks)
                return;

            AnimIndex = 1;
            float cosPitch = MathF.Cos(_cachedAimPitch);
            float speed = _parameters.ProjectileSpeed;
            float vx = speed * cosPitch * MathF.Sin(_cachedAimYaw);
            float vy = speed * MathF.Sin(_cachedAimPitch);
            float vz = speed * cosPitch * MathF.Cos(_cachedAimYaw);
            float damage = _parameters.Damage;
            float radius = _parameters.HitboxRadius;
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            Resolver.Spawn(new Hitbox
            {
                X = s.PX,
                Y = s.PY + _parameters.LaunchOffsetY,
                Z = s.PZ,
                VX = vx,
                VY = vy,
                VZ = vz,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = damage,
                BaseKnockback = _parameters.KnockbackBase,
                KnockbackGrowth = _parameters.KnockbackGrowth,
                KnockbackAngle = (sbyte)_parameters.KnockbackAngle,
                StunTicks = _parameters.StunTicks,
                DurationTicks = _parameters.MaxFlightTicks,
                OwnerId = s.EntityId,
                Gravity = _parameters.Gravity,
            });
        }
    }
}
