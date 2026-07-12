using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's Q — Ki Shot: aimed projectile that applies Marked status on hit.
    /// Hold to aim (TPS camera), release fires a fast ki blast in aim direction.
    /// Minimal gravity — true ki-blast floating projectile.
    /// On entity hit: applies Marked status (StatusType.Marked = bit 2).
    /// </summary>
    public sealed class FightGuyKiShot : ServerAbility
    {
        private bool _projectileSpawned;
        private float _cachedAimYaw;
        private float _cachedAimPitch;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _projectileSpawned = false;
            _cachedAimYaw = 0f;
            _cachedAimPitch = 0f;

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
                    _cachedAimYaw = s.AimYaw;
                    _cachedAimPitch = s.AimPitch;
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

                float speed = GetParam(def, "projectile_speed", 25f);
                float pitch = _cachedAimPitch;
                float cosPitch = MathF.Cos(pitch);
                float vx = speed * cosPitch * MathF.Sin(_cachedAimYaw);
                float vy = speed * MathF.Sin(pitch);
                float vz = speed * cosPitch * MathF.Cos(_cachedAimYaw);

                float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
                float projRadius = GetParam(def, "hitbox_radius", 0.5f);
                float projDamage = GetParam(def, "damage", 6f);
                ApplyBuffBonuses(ref s, ref projDamage, ref projRadius);

                float kbBase = GetParam(def, "knockback_base", 1.2f);
                float kbGrowth = GetParam(def, "knockback_growth", 1.8f);
                float kbUpward = GetParam(def, "knockback_upward", 2f);
                ushort stunTicks = (ushort)GetParam(def, "stun_ticks", 6f);
                ushort maxFlight = (ushort)GetParam(def, "max_flight_ticks", 90f);

                Resolver.Spawn(new Hitbox
                {
                    X = s.PX,
                    Y = s.PY + launchOffsetY,
                    Z = s.PZ,
                    VX = vx, VY = vy, VZ = vz,
                    Radius = projRadius,
                    Shape = HitboxShape.Sphere,
                    EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                    Damage = projDamage,
                    BaseKnockback = kbBase,
                    KnockbackGrowth = kbGrowth,
                    KnockbackUpward = kbUpward,
                    StunTicks = stunTicks,
                    DurationTicks = maxFlight,
                    OwnerId = s.EntityId,
                    Gravity = GetParam(def, "gravity", 1f),
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
