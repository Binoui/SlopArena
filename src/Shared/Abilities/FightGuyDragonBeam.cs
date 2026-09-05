using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's F — Dragon Beam: a fixed-startup camera-directed beam.
    /// </summary>
    public sealed class FightGuyDragonBeam : ServerAbility
    {
        private readonly CookedDragonBeamCapabilityParameters _parameters;
        private ushort _ticks;
        private float _cachedAimYaw;
        private float _cachedAimPitch;
        private readonly HashSet<ulong> _hitEntities = new();

        public FightGuyDragonBeam(CookedDragonBeamCapabilityParameters parameters)
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
            s.VX = 0f;
            s.VZ = 0f;
            _hitEntities.Clear();
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;
            s.VX = 0f;
            s.VZ = 0f;
            if (_ticks != _parameters.FireTick)
                return;

            float cosPitch = MathF.Cos(_cachedAimPitch);
            float dirX = cosPitch * MathF.Sin(_cachedAimYaw);
            float dirY = MathF.Sin(_cachedAimPitch);
            float dirZ = cosPitch * MathF.Cos(_cachedAimYaw);
            float startY = s.PY + _parameters.LaunchOffsetY;
            float damage = _parameters.Damage;
            float radius = _parameters.BeamRadius;

            Resolver.Spawn(new Hitbox
            {
                X = s.PX,
                Y = startY,
                Z = s.PZ,
                EndX = s.PX + dirX * _parameters.BeamRange,
                EndY = startY + dirY * _parameters.BeamRange,
                EndZ = s.PZ + dirZ * _parameters.BeamRange,
                Radius = radius,
                Shape = HitboxShape.Capsule,
                Damage = damage,
                BaseKnockback = _parameters.KnockbackBase,
                KnockbackGrowth = _parameters.KnockbackGrowth,
                KnockbackAngle = (sbyte)_parameters.KnockbackAngle,
                StunTicks = _parameters.StunTicks,
                DurationTicks = _parameters.HitboxDurationTicks,
                OwnerId = s.EntityId,
                FreezesOwner = false,
                HitsMultipleOpponents = true,
                HitEntities = _hitEntities,
            });
        }
    }
}
