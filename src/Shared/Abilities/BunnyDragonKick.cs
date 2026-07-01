using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Bunny's R — Dragon's Kick: conditional finisher.
    /// Two modes:
    ///   - No mark: straight forward lunge kick (SetVelocityInFacing).
    ///   - Marked target exists: homes toward the closest marked entity.
    /// On entity hit: consumes mark for 1.5× damage, spawns AoE explosion.
    /// Recast (press R again) cancels early after min_ticks_before_cancel.
    /// </summary>
    public class BunnyDragonKick : ServerAbility
    {
        private bool _hasImpacted;
        private ushort _flightTicks;
        private ushort _postImpactTicks;
        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _hasImpacted = false;
            _flightTicks = 0;
            _postImpactTicks = 0;
            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0; // spell_r_loop
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            // Start with forward lunge (will switch to homing if mark exists)
            float forwardSpeed = GetParam(def, "forward_speed", 20f);
            SetVelocityInFacing(ref s, forwardSpeed);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            if (_hasImpacted)
            {
                _postImpactTicks--;
                if (_postImpactTicks == 0)
                    EndAbility(ref s);
                return;
            }
            _flightTicks++;



            // ── Recast-to-cancel ──
            ushort minCancel = (ushort)GetParam(def, "min_ticks_before_cancel", 10f);
            if (_flightTicks >= minCancel && input.ActiveSlot == (Slot + 1))
            {
                input.ActiveSlot = 0;
                EndAbility(ref s);
                return;
            }

            // ── Duration expiration → spell_r_end ──
            ushort maxTicks = (ushort)GetParam(def, "max_flight_ticks", 180f);
            if (_flightTicks >= maxTicks)
            {
                AnimIndex = 2; // spell_r_end
                s.ComboStage = 2;
                _hasImpacted = true; // re-use impact timer for end anim
                _postImpactTicks = 10;
                return;
            }

            // ── Homing: find closest marked entity ──
            ulong closestMarked = 0;
            float closestDist = float.MaxValue;

            if (SimulationStates != null)
            {
                foreach (var kvp in SimulationStates)
                {
                    ulong otherId = kvp.Key;
                    var other = kvp.Value;
                    if (otherId == s.EntityId) continue;
                    if ((other.StatusFlags & (1 << 2)) == 0) continue; // not marked

                    float dx = other.PX - s.PX;
                    float dz = other.PZ - s.PZ;
                    float distSq = dx * dx + dz * dz;
                    if (distSq < closestDist)
                    {
                        closestDist = distSq;
                        closestMarked = otherId;
                    }
                }
            }

            if (closestMarked != 0)
            {
                // Homing mode: steer toward marked target
                var target = SimulationStates![closestMarked];
                float homingSpeed = GetParam(def, "homing_speed", 24f);
                float homingAccel = GetParam(def, "homing_accel", 2f);

                float dx = target.PX - s.PX;
                float dz = target.PZ - s.PZ;
                float dist = MathF.Sqrt(dx * dx + dz * dz);

                if (dist > 0.1f)
                {
                    float targetVX = (dx / dist) * homingSpeed;
                    float targetVZ = (dz / dist) * homingSpeed;

                    // Smooth steer toward target
                    s.VX += (targetVX - s.VX) * Math.Clamp(homingAccel * Simulation.TickDt, 0f, 1f);
                    s.VZ += (targetVZ - s.VZ) * Math.Clamp(homingAccel * Simulation.TickDt, 0f, 1f);
                }
            }

            // Spawn hitbox events from spec
            var stage = def.GetSlotAbility(Slot, false).Stages[0];
            foreach (var evt in stage.HitboxEvents)
            {
                if (evt.TriggerTick == _flightTicks)
                    SpawnHitbox(ref s, evt);
            }
        }

        /// <summary>
        /// On hit: consume mark, multiply damage, spawn AoE explosion at target position.
        /// </summary>
        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef,
            ref float damage, ref float knockbackForce)
        {
            if (_hasImpacted)
                return;

            bool isMarked = (target.StatusFlags & (1 << 2)) != 0;

            if (isMarked)
            {
                // Consume mark
                target.StatusFlags = (byte)(target.StatusFlags & ~(1 << 2));
                target.StatusRemainingTicks = 0;

                // Multiply damage
                float multiplier = GetParam(attackerDef, "mark_multiplier", 1.5f);
                damage *= multiplier;
            }

            // Spawn AoE explosion at target position
            float aoeRadius = GetParam(attackerDef, "impact_aoe_radius", 2f);
            float aoeDuration = GetParam(attackerDef, "impact_aoe_duration", 8f);
            float aoeDamage = GetParam(attackerDef, "impact_aoe_damage", 6f);
            float aoeKnockback = GetParam(attackerDef, "impact_aoe_knockback", 8f);
            float aoeUpward = GetParam(attackerDef, "impact_aoe_upward", 6f);
            ushort aoeStun = (ushort)GetParam(attackerDef, "impact_aoe_stun", 10f);

            Resolver.Spawn(new Hitbox
            {
                X = target.PX,
                Y = target.PY + 0.5f,
                Z = target.PZ,
                Radius = aoeRadius,
                Shape = HitboxShape.Sphere,
                DurationTicks = (ushort)aoeDuration,
                Damage = aoeDamage,
                KnockbackForce = aoeKnockback,
                KnockbackUpward = aoeUpward,
                StunTicks = aoeStun,
                OwnerId = attacker.EntityId,
            });
            // Switch to impact animation (spell_r_attack), stop forward motion
            AnimIndex = 1;
            attacker.ComboStage = 1;
            attacker.VX = 0f;
            attacker.VZ = 0f;
            attacker.VY = 0f;

            _hasImpacted = true;
            _postImpactTicks = 10; // play attack anim for ~0.17s then end
        }
    }
}
