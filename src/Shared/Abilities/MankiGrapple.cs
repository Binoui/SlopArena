using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's E (slot 3): Grapple Gun — hold to aim (camera direction), release to fire tether.
    ///
    /// Phases:
    ///   0 (Aiming): Hold key, show loop animation, camera controls aim direction.
    ///     Transition to Firing on release (input.IsAiming == false).
    ///   1 (Firing): Fire tether projectile in AimYaw/AimPitch direction.
    ///   2 (Reeling): Entity hit → track target, terrain hit → reel to anchor.
    ///
    /// Terrain detection uses SpellResolver.OnHitboxRemoved hook.
    /// </summary>
    public sealed class MankiGrapple : ServerAbility
    {
        private enum GrapplePhase { Aiming, Firing, Reeling }

        private GrapplePhase _phase;
        private float _anchorX, _anchorY, _anchorZ;
        private ulong _anchorEntityId;
        private bool _projectileSpawned;
        private ulong _ownerEntityId;

        // Cached params
        private ushort _fireTriggerTick;
        private ushort _maxFlightTicks;
        private float _tetherSpeed;
        private float _reelSpeed;
        private float _arrivalThreshold;
        private float _hitboxRadius;
        private float _grappleDamage;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _phase = GrapplePhase.Aiming;
            _projectileSpawned = false;
            _anchorEntityId = 0;
            _ownerEntityId = s.EntityId;

            _fireTriggerTick = (ushort)GetParam(def, "fire_trigger_tick", 8f);
            _maxFlightTicks = (ushort)GetParam(def, "max_flight_ticks", 30f);
            _tetherSpeed = GetParam(def, "tether_speed", 40f);
            _reelSpeed = GetParam(def, "reel_speed", 25f);
            _arrivalThreshold = GetParam(def, "arrival_threshold", 0.5f);
            _hitboxRadius = GetParam(def, "hitbox_radius", 0.3f);
            _grappleDamage = GetParam(def, "damage", 3f);

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;  // spell_e (loop or start)
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            s.AnimLockTicks = (ushort)GetParam(def, "charge_hold_ticks", 180f);

            if (Resolver is SpellResolver resolver)
                resolver.OnHitboxRemoved += OnTetherRemoved;
        }

        public override void OnEnd(ref CharacterState s)
        {
            s.VX = 0f;
            s.VY = 0f;
            s.VZ = 0f;
            s.IsAiming = false;

            if (Resolver is SpellResolver resolver)
                resolver.OnHitboxRemoved -= OnTetherRemoved;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            switch (_phase)
            {
                case GrapplePhase.Aiming:
                    TickAiming(ref s, input, def);
                    break;
                case GrapplePhase.Firing:
                    TickFiring(ref s);
                    break;
                case GrapplePhase.Reeling:
                    TickReeling(ref s);
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
                _phase = GrapplePhase.Firing;
                s.AttackElapsedTicks = 0;
                s.AnimLockTicks = _maxFlightTicks;
            }
        }

        private void TickFiring(ref CharacterState s)
        {
            if (!_projectileSpawned && s.AttackElapsedTicks >= _fireTriggerTick)
            {
                _projectileSpawned = true;
                SpawnTether(ref s);
            }

            if (s.AttackElapsedTicks >= _maxFlightTicks && _phase == GrapplePhase.Firing)
            {
                EndAbility(ref s);
            }
        }

        private void TickReeling(ref CharacterState s)
        {
            if (_anchorEntityId > 0)
            {
                if (SimulationStates != null && SimulationStates.TryGetValue(_anchorEntityId, out var targetState))
                {
                    _anchorX = targetState.PX;
                    _anchorY = targetState.PY;
                    _anchorZ = targetState.PZ;
                }
            }

            float dx = _anchorX - s.PX;
            float dy = _anchorY - s.PY;
            float dz = _anchorZ - s.PZ;
            float dist = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (dist <= _arrivalThreshold)
            {
                EndAbility(ref s);
                return;
            }

            if (dist > 0.001f)
            {
                s.VX = (dx / dist) * _reelSpeed;
                s.VY = (dy / dist) * _reelSpeed;
                s.VZ = (dz / dist) * _reelSpeed;
            }
        }

        private void SpawnTether(ref CharacterState s)
        {
            float pitch = s.AimPitch;
            float yaw = s.AimYaw;
            float cosPitch = MathF.Cos(pitch);

            float vx = _tetherSpeed * cosPitch * MathF.Sin(yaw);
            float vy = _tetherSpeed * MathF.Sin(pitch);
            float vz = _tetherSpeed * cosPitch * MathF.Cos(yaw);

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiGrapple] Firing tether! pitch={pitch:F3}({pitch*(180f/MathF.PI):F1}°) yaw={yaw:F3}({yaw*(180f/MathF.PI):F1}°) vy={vy:F2} vz={vz:F2}");

            Resolver.Spawn(new Hitbox
            {
                X = s.PX, Y = s.PY + 1.0f, Z = s.PZ,
                VX = vx, VY = vy, VZ = vz,
                Radius = _hitboxRadius,
                Shape = HitboxShape.Sphere,
                EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                Damage = 0f,
                BaseKnockback = 0f, KnockbackGrowth = 0f,
                KnockbackUpward = 0f, StunTicks = 0,
                DurationTicks = _maxFlightTicks,
                OwnerId = s.EntityId,
                Gravity = 2f,
                Explosion = new ProjectileExplosion
                {
                    Radius = 0.1f,
                    Damage = 0f,
                    BaseKnockback = 0f, KnockbackGrowth = 0f,
                    KnockbackUpward = 0f, StunTicks = 0,
                    DurationTicks = 1,
                },
            });
        }

        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef, ref float damage, ref float knockbackForce)
        {
            if (_phase != GrapplePhase.Firing) return;
            if (target.EntityId == attacker.EntityId) return;

            _phase = GrapplePhase.Reeling;
            _anchorEntityId = target.EntityId;
            _anchorX = target.PX;
            _anchorY = target.PY;
            _anchorZ = target.PZ;

            target.DamagePercent += (ushort)_grappleDamage;
            if (target.DamagePercent > 999) target.DamagePercent = 999;

            knockbackForce = 0f;

            float dx = target.PX - attacker.PX;
            float dy = target.PY - attacker.PY;
            float dz = target.PZ - attacker.PZ;
            float dist = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (dist > 0.001f)
            {
                attacker.VX = (dx / dist) * _reelSpeed;
                attacker.VY = (dy / dist) * _reelSpeed;
                attacker.VZ = (dz / dist) * _reelSpeed;
            }
        }

        private void OnTetherRemoved(Hitbox hb, float lx, float ly, float lz)
        {
            if (_phase != GrapplePhase.Firing) return;
            if (hb.OwnerId != _ownerEntityId) return;
            if (_anchorEntityId != 0) return;

            if (hb.Active && hb.AgeTicks < _maxFlightTicks - 1)
            {
                _phase = GrapplePhase.Reeling;
                _anchorX = lx;
                _anchorY = ly;
                _anchorZ = lz;
            }
        }
    }
}
