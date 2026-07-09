using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's R (slot 4): Bazooka — hold to aim (camera direction), release to fire.
    /// 
    /// Phases:
    ///   0 (Aiming): Hold key, show loop animation, camera controls aim direction.
    ///     Transition to Firing on release (input.IsAiming == false).
    ///   1 (Firing): Short cast animation, spawn rocket at trigger tick in AimYaw/AimPitch direction.
    ///   2 (Recovery): Endlag, then EndAbility.
    ///
    /// Projectile has gravity + ground collision + explosion with CanHitOwner=true.
    /// Rocket jump: aim at feet → projectile ground-collides near self → explosion.
    /// </summary>
    public sealed class MankiBazooka : ServerAbility
    {
        private enum BazookaPhase { Aiming, Firing, Recovery }
        private BazookaPhase _phase;
        private bool _projectileSpawned;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _phase = BazookaPhase.Aiming;
            _projectileSpawned = false;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;  // spell_r (loop or start)
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            // Lock input for max hold duration (charge_hold_ticks or 180 = 3s)
            s.AnimLockTicks = (ushort)GetParam(def, "charge_hold_ticks", 180f);
        }

        public override void OnEnd(ref CharacterState s)
        {
            s.IsAiming = false;
            s.VX = 0f;
            s.VY = 0f;
            s.VZ = 0f;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            switch (_phase)
            {
                case BazookaPhase.Aiming:
                    TickAiming(ref s, input, def);
                    break;
                case BazookaPhase.Firing:
                    TickFiring(ref s, def);
                    break;
                case BazookaPhase.Recovery:
                    TickRecovery(ref s, def);
                    break;
            }
        }

        private void TickAiming(ref CharacterState s, InputState input, CharacterDefinition def)
        {
            s.IsAiming = true;
            // Server no longer enforces facing = aim direction — character
            // faces naturally based on movement direction.

            if (!input.IsAiming)
            {
                _phase = BazookaPhase.Firing;
                s.AttackElapsedTicks = 0;
                AnimIndex = 0;
                s.AnimLockTicks = (ushort)GetParam(def, "cast_duration", 20f);
            }
        }

        private void TickFiring(ref CharacterState s, CharacterDefinition def)
        {
            ushort fireTriggerTick = (ushort)GetParam(def, "fire_trigger_tick", 6f);
            ushort castDuration = (ushort)GetParam(def, "cast_duration", 20f);

            if (!_projectileSpawned && s.AttackElapsedTicks >= fireTriggerTick)
            {
                _projectileSpawned = true;
                SpawnRocket(ref s, def);
            }

            if (s.AttackElapsedTicks >= castDuration)
            {
                _phase = BazookaPhase.Recovery;
                s.AttackElapsedTicks = 0;
                s.AnimLockTicks = (ushort)GetParam(def, "recovery_duration", 15f);
            }
        }

        private void TickRecovery(ref CharacterState s, CharacterDefinition def)
        {
            ushort recoveryDuration = (ushort)GetParam(def, "recovery_duration", 15f);
            if (s.AttackElapsedTicks >= recoveryDuration)
            {
                EndAbility(ref s);
            }
        }

        private void SpawnRocket(ref CharacterState s, CharacterDefinition def)
        {
            float speed = GetParam(def, "projectile_speed", 40f);
            float pitch = s.AimPitch;
            float yaw = s.AimYaw;

            float cosPitch = MathF.Cos(pitch);
            float vx = speed * cosPitch * MathF.Sin(yaw);
            float vy = speed * MathF.Sin(pitch);
            float vz = speed * cosPitch * MathF.Cos(yaw);

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiBazooka] Firing! pitch={pitch:F3}({pitch*(180f/MathF.PI):F1}°) yaw={yaw:F3}({yaw*(180f/MathF.PI):F1}°) vy={vy:F2} vz={vz:F2}");

            float radius = GetParam(def, "hitbox_radius", 0.6f);
            float damage = GetParam(def, "damage", 15f);
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            Resolver.Spawn(new Hitbox
            {
                X = s.PX,
                Y = s.PY + 1.0f,
                Z = s.PZ,
                VX = vx, VY = vy, VZ = vz,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                Damage = damage,
                BaseKnockback = GetParam(def, "knockback_base", 6f),
                KnockbackGrowth = GetParam(def, "knockback_growth", 9f),
                KnockbackUpward = GetParam(def, "knockback_upward", 12f),
                StunTicks = (ushort)GetParam(def, "stun_ticks", 25f),
                DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 45f),
                OwnerId = s.EntityId,
                Gravity = GetParam(def, "gravity", 15f),
                Explosion = new ProjectileExplosion
                {
                    Radius = GetParam(def, "explosion_radius", 3f),
                    Damage = GetParam(def, "explosion_damage", 10f),
                    BaseKnockback = GetParam(def, "explosion_kb_base", 6f),
                    KnockbackGrowth = GetParam(def, "explosion_kb_growth", 9f),
                    KnockbackUpward = GetParam(def, "explosion_knockback_upward", 14f),
                    StunTicks = (ushort)GetParam(def, "explosion_stun_ticks", 20f),
                    DurationTicks = (ushort)GetParam(def, "explosion_duration_ticks", 6f),
                    CanHitOwner = true,
                },
            });
        }

        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef, ref float damage, ref float knockbackForce)
        {
            if (target.EntityId == attacker.EntityId)
            {
                float selfDmg = GetParam(attackerDef, "self_damage", 4f);
                int corrected = target.DamagePercent - (ushort)damage + (ushort)selfDmg;
                target.DamagePercent = (ushort)Math.Clamp(corrected, 0, 999);
            }
        }
    }
}
