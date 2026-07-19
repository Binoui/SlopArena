using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's E — Cyclone Kick: forward lunge with per-tick hitboxes.
    /// FightGuy dashes forward at 17 m/s for 40 ticks (~11.3m) while spawning
    /// body + 4 side hitboxes each tick from tick 6-34. Stuns enemies passed through,
    /// no knockback — pure engage/disrupt tool.
    /// </summary>
    public class FightGuyCycloneKick : ServerAbility
    {
        private ushort _ticks;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            float forwardSpeed = GetParam(def, "forward_speed", 17f);
            SetVelocityInFacing(ref s, forwardSpeed);

            ushort duration = (ushort)GetParam(def, "duration_ticks", 40f);
            s.AnimLockTicks = duration;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;

            // Maintain forward velocity during lunge
            float forwardSpeed = GetParam(def, "forward_speed", 17f);
            SetVelocityInFacing(ref s, forwardSpeed);

            // Spawn hitboxes during the active window
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
                ushort stunTicks = (ushort)GetParam(def, "stun_ticks", 96f);
                float bodyY = s.PY + GetParam(def, "body_y", 0.8f);
                float sideY = s.PY + GetParam(def, "side_y", 0.3f);

                // Body hitbox — at character center
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX, Y = bodyY, Z = s.PZ,
                    Radius = bodyRadius,
                    Shape = HitboxShape.Sphere,
                    Damage = damage,
                    StunTicks = stunTicks,
                    DurationTicks = 2,
                    OwnerId = s.EntityId,
                });

                // 4 side hitboxes: front, back, left, right — covers the spin arc
                // Forward direction: (sin, cos), Right direction: (cos, -sin)
                // Front
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX + (sin * sideOff), Y = sideY, Z = s.PZ + (cos * sideOff),
                    Radius = sideRadius, Shape = HitboxShape.Sphere,
                    Damage = damage, StunTicks = stunTicks,
                    DurationTicks = 2, OwnerId = s.EntityId,
                });
                // Back
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX - (sin * sideOff), Y = sideY, Z = s.PZ - (cos * sideOff),
                    Radius = sideRadius, Shape = HitboxShape.Sphere,
                    Damage = damage, StunTicks = stunTicks,
                    DurationTicks = 2, OwnerId = s.EntityId,
                });
                // Right
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX + (cos * sideOff), Y = sideY, Z = s.PZ - (sin * sideOff),
                    Radius = sideRadius, Shape = HitboxShape.Sphere,
                    Damage = damage, StunTicks = stunTicks,
                    DurationTicks = 2, OwnerId = s.EntityId,
                });
                // Left
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX - (cos * sideOff), Y = sideY, Z = s.PZ + (sin * sideOff),
                    Radius = sideRadius, Shape = HitboxShape.Sphere,
                    Damage = damage, StunTicks = stunTicks,
                    DurationTicks = 2, OwnerId = s.EntityId,
                });
            }

            ushort duration = (ushort)GetParam(def, "duration_ticks", 40f);
            if (_ticks >= duration)
                EndAbility(ref s);
        }
    }
}
