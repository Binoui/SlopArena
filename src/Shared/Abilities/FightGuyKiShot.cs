using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's A — Ki Shot: a fixed-startup camera-directed ki projectile.
    /// </summary>
    public sealed class FightGuyKiShot : ServerAbility
    {
        private ushort _ticks;
        private float _cachedAimYaw;
        private float _cachedAimPitch;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _cachedAimYaw = s.AimYaw;
            _cachedAimPitch = s.AimPitch;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            s.IsAiming = false;
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.AnimLockTicks = (ushort)GetParam(def, "duration_ticks", 24f);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;

            ushort startupTicks = (ushort)GetParam(def, "startup_ticks", 8f);
            if (_ticks == startupTicks)
            {
                AnimIndex = 1;

                float speed = GetParam(def, "projectile_speed", 25f);
                float cosPitch = MathF.Cos(_cachedAimPitch);
                float vx = speed * cosPitch * MathF.Sin(_cachedAimYaw);
                float vy = speed * MathF.Sin(_cachedAimPitch);
                float vz = speed * cosPitch * MathF.Cos(_cachedAimYaw);

                float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
                float radius = GetParam(def, "hitbox_radius", 0.5f);
                float damage = GetParam(def, "damage", 6f);
                ApplyBuffBonuses(ref s, ref damage, ref radius);

                Resolver.Spawn(new Hitbox
                {
                    X = s.PX,
                    Y = s.PY + launchOffsetY,
                    Z = s.PZ,
                    VX = vx,
                    VY = vy,
                    VZ = vz,
                    Radius = radius,
                    Shape = HitboxShape.Sphere,
                    Damage = damage,
                    BaseKnockback = GetParam(def, "knockback_base", 3f),
                    KnockbackGrowth = GetParam(def, "knockback_growth", 4.5f),
                    KnockbackAngle = (sbyte)GetParam(def, "kb_angle", 30f),
                    StunTicks = (ushort)GetParam(def, "stun_ticks", 12f),
                    DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 90f),
                    OwnerId = s.EntityId,
                    Gravity = GetParam(def, "gravity", 1f),
                });
            }

            if (_ticks >= (ushort)GetParam(def, "duration_ticks", 24f))
                EndAbility(ref s);
        }
    }
}
