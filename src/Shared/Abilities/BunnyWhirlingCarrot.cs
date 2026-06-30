using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Bunny's Q — Whirling Carrot: aimed projectile that applies Marked status on hit.
    /// Hold to aim (up to charge_hold_ticks), release throws a parabolic boomerang.
    /// On entity hit: applies Marked status (StatusType.Marked = bit 2).
    /// On ground impact: spawns AoE explosion.
    /// </summary>
    public sealed class BunnyWhirlingCarrot : ServerAbility
    {
        private bool _projectileSpawned;
        private float _cachedAimDistance;
        private float _cachedAimYaw;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _projectileSpawned = false;
            _cachedAimDistance = 0f;
            _cachedAimYaw = 0f;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            s.AnimLockTicks = 8;
            s.ChargeTicks = 0;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort maxHoldTicks = (ushort)GetParam(def, "charge_hold_ticks", 180f);

            // ── Charge / aim phase ──
            if (s.ComboStage == 0)
            {
                if (s.AttackElapsedTicks > 8 && s.AnimIndex != 1)
                {
                    AnimIndex = 1;
                }

                if (s.AttackElapsedTicks > 8 && (!input.IsAiming || (maxHoldTicks > 0 && s.ChargeTicks >= maxHoldTicks)))
                {
                    _cachedAimDistance = s.AimTargetDistance;
                    _cachedAimYaw = s.AimYaw;
                    s.ComboStage = 1;
                    AnimIndex = 2;
                    s.AttackElapsedTicks = 0;
                }
                return;
            }

            // ── Throw phase ──
            ushort throwTick = (ushort)GetParam(def, "throw_trigger_tick", 10f);

            if (!_projectileSpawned && s.AttackElapsedTicks >= throwTick)
            {
                _projectileSpawned = true;
                s.IsAiming = false;

                float D = Math.Clamp(_cachedAimDistance, 0.5f, GetParam(def, "max_range", 15f));
                float launchAngleDeg = GetParam(def, "launch_angle", 30f);
                float g = GetParam(def, "gravity", 30f);
                float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
                float dY = -def.CapsuleHeight * 0.5f - launchOffsetY;

                float launchRad = launchAngleDeg * (MathF.PI / 180f);
                CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
                    out float _, out float hSpeed, out float vSpeed);

                float aimCos = MathF.Cos(_cachedAimYaw);
                float aimSin = MathF.Sin(_cachedAimYaw);

                float projRadius = GetParam(def, "hitbox_radius", 0.5f);
                float projDamage = GetParam(def, "damage", 6f);
                ApplyBuffBonuses(ref s, ref projDamage, ref projRadius);

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
                    KnockbackForce = GetParam(def, "knockback_force", 3f),
                    KnockbackUpward = GetParam(def, "knockback_upward", 2f),
                    StunTicks = (ushort)GetParam(def, "stun_ticks", 6f),
                    DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 90f),
                    OwnerId = s.EntityId,
                    Gravity = g,
                    Explosion = new ProjectileExplosion
                    {
                        Radius = GetParam(def, "explosion_radius", 2.5f),
                        Damage = GetParam(def, "explosion_damage", 4f),
                        KnockbackForce = GetParam(def, "explosion_knockback_force", 3f),
                        KnockbackUpward = GetParam(def, "explosion_knockback_upward", 2f),
                        StunTicks = (ushort)GetParam(def, "explosion_stun_ticks", 4f),
                        DurationTicks = (ushort)GetParam(def, "explosion_duration_ticks", 6f),
                    },
                });
            }

            ushort duration = (ushort)GetParam(def, "throw_duration", 60f);
            if (s.AttackElapsedTicks >= duration)
                EndAbility(ref s);
        }

        /// <summary>
        /// On hit: apply Marked status to target (bit 2 = StatusType.Marked).
        /// Ignores self-hit (no self-mark).
        /// </summary>
        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef,
            ref float damage, ref float knockbackForce)
        {
            // Skip self-mark
            if (attacker.EntityId == target.EntityId)
                return;

            ushort markDuration = (ushort)GetParam(attackerDef, "mark_duration_ticks", 300f);

            // Apply Marked status (bit 2 = 1 << 2 = 4)
            target.StatusFlags |= (1 << 2);
            target.StatusRemainingTicks = Math.Max(target.StatusRemainingTicks, markDuration);
        }
    }
}
