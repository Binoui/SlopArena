using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's F — Dragon Beam: a fixed-startup camera-directed beam.
    /// </summary>
    public sealed class FightGuyDragonBeam : ServerAbility
    {
        private ushort _ticks;
        private float _cachedAimYaw;
        private float _cachedAimPitch;
        private readonly HashSet<ulong> _hitEntities = new();

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _cachedAimYaw = s.AimYaw;
            _cachedAimPitch = s.AimPitch;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.AnimLockTicks = (ushort)GetParam(def, "duration_ticks", 28f);
            s.VX = 0f;
            s.VZ = 0f;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;
            s.VX = 0f;
            s.VZ = 0f;

            ushort fireTick = (ushort)GetParam(def, "fire_tick", 24f);
            if (_ticks == fireTick)
            {
                float pitch = _cachedAimPitch;
                float cosPitch = MathF.Cos(pitch);
                float dirX = cosPitch * MathF.Sin(_cachedAimYaw);
                float dirY = MathF.Sin(pitch);
                float dirZ = cosPitch * MathF.Cos(_cachedAimYaw);
                float startY = s.PY + GetParam(def, "launch_offset_y", 1.2f);
                float range = GetParam(def, "beam_range", 18f);

                float damage = GetParam(def, "damage", 14f);
                float radius = GetParam(def, "beam_radius", 0.45f);
                ApplyBuffBonuses(ref s, ref damage, ref radius);

                Resolver.Spawn(new Hitbox
                {
                    X = s.PX,
                    Y = startY,
                    Z = s.PZ,
                    EndX = s.PX + (dirX * range),
                    EndY = startY + (dirY * range),
                    EndZ = s.PZ + (dirZ * range),
                    Radius = radius,
                    Shape = HitboxShape.Capsule,
                    Damage = damage,
                    BaseKnockback = GetParam(def, "knockback_base", 18f),
                    KnockbackGrowth = GetParam(def, "knockback_growth", 10f),
                    KnockbackAngle = (sbyte)GetParam(def, "knockback_angle", 20f),
                    StunTicks = (ushort)GetParam(def, "stun_ticks", 24f),
                    DurationTicks = (ushort)GetParam(def, "hitbox_duration_ticks", 2f),
                    OwnerId = s.EntityId,
                    FreezesOwner = false,
                    HitsMultipleOpponents = true,
                    HitEntities = _hitEntities,
                });
            }

            if (_ticks >= (ushort)GetParam(def, "duration_ticks", 28f))
                EndAbility(ref s);
        }
    }
}
