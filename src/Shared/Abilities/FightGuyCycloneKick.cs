using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's R — Cyclone Kick: forward lunge with per-tick hitboxes.
    /// Each opponent can be hit once per activation.
    /// </summary>
    public class FightGuyCycloneKick : ServerAbility
    {
        private readonly CookedCycloneKickCapabilityParameters _parameters;
        private ushort _ticks;
        private readonly HashSet<ulong> _hitEntities = new();

        public FightGuyCycloneKick(CookedCycloneKickCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _hitEntities.Clear();
            s.State = ActionState.Attacking;
            s.IsAiming = false;
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;
            SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
            if (_ticks <= _parameters.WindupTicks || _ticks > _parameters.HitboxEndTick)
                return;

            float cos = MathF.Cos(s.FacingYaw);
            float sin = MathF.Sin(s.FacingYaw);
            SpawnDynamicHitbox(ref s, _parameters.BodyRadius, 0f, _parameters.BodyY, 0f, true);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, _parameters.SideOffset * sin, _parameters.SideY, _parameters.SideOffset * cos, false);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, _parameters.SideOffset * cos, _parameters.SideY, -_parameters.SideOffset * sin, false);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, -_parameters.SideOffset * sin, _parameters.SideY, -_parameters.SideOffset * cos, false);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, -_parameters.SideOffset * cos, _parameters.SideY, _parameters.SideOffset * sin, false);
        }

        private void SpawnDynamicHitbox(ref CharacterState s, float radius, float x, float y, float z, bool freezesOwner)
        {
            Resolver.Spawn(new Hitbox
            {
                X = s.PX + x,
                Y = s.PY + y,
                Z = s.PZ + z,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = _parameters.Damage,
                BaseKnockback = _parameters.KnockbackBase,
                KnockbackGrowth = _parameters.KnockbackGrowth,
                KnockbackAngle = (sbyte)_parameters.KnockbackAngle,
                StunTicks = _parameters.StunTicks,
                DurationTicks = 2,
                OwnerId = s.EntityId,
                FreezesOwner = freezesOwner,
                HitsMultipleOpponents = true,
                HitEntities = _hitEntities,
            });
        }
    }

}
