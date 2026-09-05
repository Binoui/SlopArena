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
    public sealed class MankiBazooka : ServerAbility, IAimHoldCapability
    {
        private readonly CookedMankiBazookaCapabilityParameters? _parameters;
        private enum BazookaPhase { Aiming, Firing, Recovery }
        private BazookaPhase _phase;
        private bool _projectileSpawned;

        public MankiBazooka() { }
        public MankiBazooka(CookedMankiBazookaCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        public MankiBazooka(MankiBazookaCapabilityParameters parameters)
            : this(parameters != null ? new CookedMankiBazookaCapabilityParameters(
                parameters.FireTriggerTick,
                parameters.ProjectileSpeed,
                parameters.HitboxRadius,
                parameters.Damage,
                parameters.Gravity,
                parameters.MaxFlightTicks,
                parameters.StunTicks,
                parameters.ExplosionRadius,
                parameters.KbAngle,
                parameters.ExplosionKbBase,
                parameters.ExplosionKbGrowth,
                parameters.ExplosionStunTicks,
                parameters.ExplosionDurationTicks,
                parameters.ExplosionKbAngle,
                parameters.CastDuration,
                parameters.RecoveryDuration) : throw new ArgumentNullException(nameof(parameters))) { }
        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _phase = BazookaPhase.Aiming;
            _projectileSpawned = false;

            // Hold = aim stance (fixed-stance friction in Simulation's movement gate).
            s.State = ActionState.Aiming;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;  // spell_r_loop (aim hold)
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
                // Firing is an action phase — re-enter Attacking (hold was Aiming).
                s.State = ActionState.Attacking;
                _phase = BazookaPhase.Firing;
                s.AttackElapsedTicks = 0;
                AnimIndex = 1;      // spell_r_attack
                s.ComboStage = 1;   // renderer switches loop -> attack clip
                s.AnimLockTicks = _parameters?.CastDuration ?? (ushort)GetParam(def, "cast_duration", 20f);
            }
        }

        private void TickFiring(ref CharacterState s, CharacterDefinition def)
        {
            ushort fireTriggerTick = _parameters?.FireTriggerTick ?? (ushort)GetParam(def, "fire_trigger_tick", 6f);
            ushort castDuration = _parameters?.CastDuration ?? (ushort)GetParam(def, "cast_duration", 20f);

            if (!_projectileSpawned && s.AttackElapsedTicks >= fireTriggerTick)
            {
                _projectileSpawned = true;
                SpawnRocket(ref s, def);
            }

            if (s.AttackElapsedTicks >= castDuration)
            {
                _phase = BazookaPhase.Recovery;
                s.AttackElapsedTicks = 0;
                s.AnimLockTicks = _parameters?.RecoveryDuration ?? (ushort)GetParam(def, "recovery_duration", 15f);
            }
        }

        private void TickRecovery(ref CharacterState s, CharacterDefinition def)
        {
            ushort recoveryDuration = _parameters?.RecoveryDuration ?? (ushort)GetParam(def, "recovery_duration", 15f);
            if (s.AttackElapsedTicks >= recoveryDuration)
            {
                EndAbility(ref s);
            }
        }

        private void SpawnRocket(ref CharacterState s, CharacterDefinition def)
        {
            float speed = _parameters?.ProjectileSpeed ?? GetParam(def, "projectile_speed", 40f);
            float pitch = s.AimPitch;
            float yaw = s.AimYaw;

            float cosPitch = MathF.Cos(pitch);
            float vx = speed * cosPitch * MathF.Sin(yaw);
            float vy = speed * MathF.Sin(pitch);
            float vz = speed * cosPitch * MathF.Cos(yaw);

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiBazooka] Firing! pitch={pitch:F3}({pitch*(180f/MathF.PI):F1}°) yaw={yaw:F3}({yaw*(180f/MathF.PI):F1}°) vy={vy:F2} vz={vz:F2}");

            float radius = _parameters?.HitboxRadius ?? GetParam(def, "hitbox_radius", 0.6f);
            float damage = _parameters?.Damage ?? GetParam(def, "damage", 15f);

            float kbBase = GetParam(def, "knockback_base", 6f);
            float kbGrowth = GetParam(def, "knockback_growth", 9f);
            float kbAngle = _parameters?.KbAngle ?? GetParam(def, "kb_angle", 25f);
            ushort stunTicks = _parameters?.StunTicks ?? (ushort)GetParam(def, "stun_ticks", 25f);
            ushort maxFlightTicks = _parameters?.MaxFlightTicks ?? (ushort)GetParam(def, "max_flight_ticks", 45f);
            float gravity = _parameters?.Gravity ?? GetParam(def, "gravity", 15f);

            float explosionRadius = _parameters?.ExplosionRadius ?? GetParam(def, "explosion_radius", 3f);
            float explosionDamage = GetParam(def, "explosion_damage", 10f);
            float explosionKbAngle = _parameters?.ExplosionKbAngle ?? GetParam(def, "explosion_kb_angle", 25f);
            float explosionKbBase = _parameters?.ExplosionKbBase ?? GetParam(def, "explosion_kb_base", 6f);
            float explosionKbGrowth = _parameters?.ExplosionKbGrowth ?? GetParam(def, "explosion_kb_growth", 9f);
            ushort explosionStunTicks = _parameters?.ExplosionStunTicks ?? (ushort)GetParam(def, "explosion_stun_ticks", 20f);
            ushort explosionDurationTicks = _parameters?.ExplosionDurationTicks ?? (ushort)GetParam(def, "explosion_duration_ticks", 6f);

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
                BaseKnockback = kbBase,
                KnockbackGrowth = kbGrowth,
                KnockbackAngle = (sbyte)kbAngle,
                StunTicks = stunTicks,
                DurationTicks = maxFlightTicks,
                OwnerId = s.EntityId,
                AttackSlot = (byte)(Slot + 1),
                Gravity = gravity,
                Explosion = new ProjectileExplosion
                {
                    Radius = explosionRadius,
                    Damage = explosionDamage,
                    Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)explosionKbAngle, BaseKnockback = explosionKbBase, KnockbackGrowth = explosionKbGrowth },
                    StunTicks = explosionStunTicks,
                    DurationTicks = explosionDurationTicks,
                    CanHitOwner = true,
                },
            });
        }

        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef, CharacterDefinition targetDef,
            ref float damage, ref float knockbackForce)
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
