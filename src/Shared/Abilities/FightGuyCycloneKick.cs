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
        private ushort _ticks;
        private readonly HashSet<ulong> _hitEntities = new();

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _hitEntities.Clear();

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            SetVelocityInFacing(ref s, GetParam(def, "forward_speed", 17f));
            s.AnimLockTicks = (ushort)GetParam(def, "duration_ticks", 40f);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;
            float forwardSpeed = GetParam(def, "forward_speed", 17f);
            SetVelocityInFacing(ref s, forwardSpeed);

            float windupTicks = GetParam(def, "windup_ticks", 6f);
            float hitboxEndTick = GetParam(def, "hitbox_end_tick", 34f);
            if (_ticks > windupTicks && _ticks <= hitboxEndTick)
            {
                float cos = MathF.Cos(s.FacingYaw);
                float sin = MathF.Sin(s.FacingYaw);
                float bodyRadius = GetParam(def, "body_radius", 0.8f);
                float sideRadius = GetParam(def, "side_radius", 0.4f);
                float sideOff = GetParam(def, "side_offset", 0.8f);
                float damage = GetParam(def, "damage", 7f);
                float kbBase = GetParam(def, "knockback_base", 8f);
                float kbGrowth = GetParam(def, "knockback_growth", 5f);
                sbyte kbAngle = (sbyte)GetParam(def, "knockback_angle", 15f);
                ushort stunTicks = (ushort)GetParam(def, "stun_ticks", 6f);
                float bodyY = s.PY + GetParam(def, "body_y", 0.8f);
                float sideY = s.PY + GetParam(def, "side_y", 0.3f);

                Resolver.Spawn(new Hitbox
                {
                    X = s.PX,
                    Y = bodyY,
                    Z = s.PZ,
                    Radius = bodyRadius,
                    Shape = HitboxShape.Sphere,
                    Damage = damage,
                    BaseKnockback = kbBase,
                    KnockbackGrowth = kbGrowth,
                    KnockbackAngle = kbAngle,
                    StunTicks = stunTicks,
                    DurationTicks = 2,
                    OwnerId = s.EntityId,
                    FreezesOwner = true,
                    HitsMultipleOpponents = true,
                    HitEntities = _hitEntities,
                });

                SpawnSideHitbox(s, sideRadius, sideOff, sideY, sin, cos, damage,
                    kbBase, kbGrowth, kbAngle, stunTicks, false);
                SpawnSideHitbox(s, sideRadius, sideOff, sideY, cos, -sin, damage,
                    kbBase, kbGrowth, kbAngle, stunTicks, false);
                SpawnSideHitbox(s, sideRadius, sideOff, sideY, -cos, sin, damage,
                    kbBase, kbGrowth, kbAngle, stunTicks, false);
            }

            if (_ticks >= (ushort)GetParam(def, "duration_ticks", 40f))
                EndAbility(ref s);
        }

        private void SpawnSideHitbox(CharacterState s, float radius, float offset, float y,
            float dirX, float dirZ, float damage, float kbBase, float kbGrowth,
            sbyte kbAngle, ushort stunTicks, bool freezesOwner)
        {
            Resolver.Spawn(new Hitbox
            {
                X = s.PX + (dirX * offset),
                Y = y,
                Z = s.PZ + (dirZ * offset),
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = damage,
                BaseKnockback = kbBase,
                KnockbackGrowth = kbGrowth,
                KnockbackAngle = kbAngle,
                StunTicks = stunTicks,
                DurationTicks = 2,
                OwnerId = s.EntityId,
                FreezesOwner = freezesOwner,
                HitsMultipleOpponents = true,
                HitEntities = _hitEntities,
            });
        }
    }
}
