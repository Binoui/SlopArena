using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's Q: throw a bomb projectile in a parabolic arc.
    /// Reads ProjectileConfig from AbilitySpec.Params at runtime.
    /// </summary>
    public sealed class MankiRoundBomb : ServerAbility
    {
        private bool _projectileSpawned;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _projectileSpawned = false;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            s.AnimIndex = 0;
            s.AnimLockTicks = (ushort)GetParam(def, "throw_duration", 60f);
            s.IsAiming = true; // show aim indicator
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort throwTick = (ushort)GetParam(def, "throw_trigger_tick", 10f);

            // Spawn projectile at throw_trigger_tick (mid-animation)
            if (!_projectileSpawned && s.AttackElapsedTicks >= throwTick)
            {
                _projectileSpawned = true;
                s.IsAiming = false;

                // Read params
                float D = Math.Clamp(s.AimTargetDistance, 0.5f, GetParam(def, "max_range", 12f));
                float launchAngleDeg = GetParam(def, "launch_angle", 30f);
                float g = GetParam(def, "gravity", 30f);
                float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
                float dY = -def.CapsuleHeight * 0.5f - launchOffsetY;

                // Compute ballistic launch velocity
                float launchRad = launchAngleDeg * (MathF.PI / 180f);
                CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
                    out float _, out float hSpeed, out float vSpeed);

                float aimCos = MathF.Cos(s.AimYaw);
                float aimSin = MathF.Sin(s.AimYaw);

                // Spawn projectile hitbox
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX,
                    Y = s.PY + launchOffsetY,
                    Z = s.PZ,
                    VX = hSpeed * aimSin,
                    VY = vSpeed,
                    VZ = hSpeed * aimCos,
                    Radius = GetParam(def, "hitbox_radius", 0.6f),
                    Shape = HitboxShape.Sphere,
                    EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                    Damage = GetParam(def, "damage", 8f),
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

            // End ability when animation lock expires
            if (s.AttackElapsedTicks >= s.AnimLockTicks)
                EndAbility(ref s);
        }
    }
}
