using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's R — Dragon's Kick: dash forward with 3-phase ability.
    /// Phase Loop: dash forward with forward capsule hitbox to detect enemies.
    /// Phase Attack: (on enemy hit) 3-hit aerial combo; hits 1-2 no knockback, hit 3 big knockback.
    /// Phase End: (whiff) recovery animation, then end.
    /// </summary>
    public class FightGuyDragonKick : ServerAbility
    {
        private enum Phase { Loop, Attack, End }
        private Phase _phase;
        private ushort _phaseTicks;
        private bool _hitDuringLoop;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _phase = Phase.Loop;
            _phaseTicks = 0;
            _hitDuringLoop = false;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            float forwardSpeed = GetParam(def, "forward_speed", 20f);
            SetVelocityInFacing(ref s, forwardSpeed);

            ushort maxTicks = (ushort)GetParam(def, "max_flight_ticks", 60f);
            s.AnimLockTicks = maxTicks;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _phaseTicks++;

            switch (_phase)
            {
                case Phase.Loop: TickLoop(ref s, ref input, def); break;
                case Phase.Attack: TickAttack(ref s, def); break;
                case Phase.End: TickEnd(ref s, def); break;
            }
        }

        private void TickLoop(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            // Homing: steer toward closest marked entity (applied by Q)
            // If no marked target, maintain forward speed
            ulong closestMarked = 0;
            float closestDist = float.MaxValue;
            if (SimulationStates != null)
            {
                foreach (var kvp in SimulationStates)
                {
                    ulong otherId = kvp.Key;
                    var other = kvp.Value;
                    if (otherId == s.EntityId) continue;
                    if ((other.StatusFlags & (1 << 2)) == 0) continue;
                    float dx = other.PX - s.PX;
                    float dz = other.PZ - s.PZ;
                    float distSq = dx * dx + dz * dz;
                    if (distSq < closestDist) { closestDist = distSq; closestMarked = otherId; }
                }
            }
            if (closestMarked != 0)
            {
                var target = SimulationStates![closestMarked];
                float speed = GetParam(def, "forward_speed", 20f);
                float dx = target.PX - s.PX;
                float dz = target.PZ - s.PZ;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > 0.1f)
                {
                    s.VX = (dx / dist) * speed;
                    s.VZ = (dz / dist) * speed;
                }
            }
            else
            {
                // No marked target: maintain forward speed
                float forwardSpeed = GetParam(def, "forward_speed", 20f);
                float currentHSpeed = MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
                if (currentHSpeed < forwardSpeed && currentHSpeed > 0.01f)
                {
                    float scale = forwardSpeed / currentHSpeed;
                    s.VX *= scale; s.VZ *= scale;
                }
                else if (currentHSpeed < 0.01f)
                {
                    SetVelocityInFacing(ref s, forwardSpeed);
                }
            }

            // Hit detected during loop → just wait (OnHitEntity already transitioned)
            if (_hitDuringLoop)
                return;

            // Recast-to-cancel
            ushort minCancel = (ushort)GetParam(def, "min_ticks_before_cancel", 10f);
            if (_phaseTicks >= minCancel && input.ActiveSlot == (Slot + 1))
            {
                input.ActiveSlot = 0;
                EndAbility(ref s);
                return;
            }

            // Timeout → end phase
            ushort maxTicks = (ushort)GetParam(def, "max_flight_ticks", 60f);
            if (_phaseTicks >= maxTicks)
            {
                TransitionToEnd(ref s, def);
                return;
            }

            // Spawn forward capsule hitbox each tick (moves with player velocity)
            float cos = MathF.Cos(s.FacingYaw);
            float sin = MathF.Sin(s.FacingYaw);
            Resolver.Spawn(new Hitbox
            {
                X = s.PX + (sin * 0.5f), Y = s.PY + 0.5f, Z = s.PZ + (cos * 0.5f),
                VX = s.VX, VY = s.VY, VZ = s.VZ,
                EndX = s.PX + (sin * 1.5f), EndY = s.PY + 0.5f, EndZ = s.PZ + (cos * 1.5f),
                Radius = 0.6f, Shape = HitboxShape.Capsule,
                Damage = 5f, StunTicks = 14,
                DurationTicks = 2, OwnerId = s.EntityId,
            });
        }

        private void TickAttack(ref CharacterState s, CharacterDefinition def)
        {
            // Zero velocity — in-place combo
            s.VX = 0f; s.VZ = 0f; s.VY = 0f;

            float cos = MathF.Cos(s.FacingYaw);
            float sin = MathF.Sin(s.FacingYaw);
            float fwdX = sin, fwdZ = cos;

            float hit1Tick = GetParam(def, "hit1_tick", 4f);
            float hit2Tick = GetParam(def, "hit2_tick", 10f);
            float hit3Tick = GetParam(def, "hit3_tick", 26f);

            if (_phaseTicks == hit1Tick)
            {
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX + fwdX * 1.2f, Y = s.PY + 0.9f, Z = s.PZ + fwdZ * 1.2f,
                    Radius = 0.6f, Shape = HitboxShape.Sphere,
                    Damage = GetParam(def, "hit1_damage", 6f),
                    StunTicks = (ushort)GetParam(def, "hit1_stun", 16f),
                    DurationTicks = 4, OwnerId = s.EntityId,
                });
            }
            else if (_phaseTicks == hit2Tick)
            {
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX + fwdX * 1.4f, Y = s.PY + 1.2f, Z = s.PZ + fwdZ * 1.4f,
                    Radius = 0.65f, Shape = HitboxShape.Sphere,
                    Damage = GetParam(def, "hit2_damage", 8f),
                    StunTicks = (ushort)GetParam(def, "hit2_stun", 20f),
                    DurationTicks = 4, OwnerId = s.EntityId,
                });
            }
            else if (_phaseTicks == hit3Tick)
            {
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX + fwdX * 1.6f, Y = s.PY + 1.0f, Z = s.PZ + fwdZ * 1.6f,
                    Radius = 0.7f, Shape = HitboxShape.Sphere,
                    Damage = GetParam(def, "hit3_damage", 16f),
                    BaseKnockback = GetParam(def, "hit3_base", 16f),
                    KnockbackGrowth = GetParam(def, "hit3_growth", 18f),
                    KnockbackUpward = GetParam(def, "hit3_upward", 8f),
                    StunTicks = (ushort)GetParam(def, "hit3_stun", 24f),
                    DurationTicks = 4, OwnerId = s.EntityId,
                });
            }

            float attackDuration = GetParam(def, "attack_duration", 36f);
            if (_phaseTicks >= attackDuration)
                EndAbility(ref s);
        }

        private void TickEnd(ref CharacterState s, CharacterDefinition def)
        {
            s.VX = 0f; s.VZ = 0f;

            float endDuration = GetParam(def, "end_duration", 15f);
            if (_phaseTicks >= endDuration)
                EndAbility(ref s);
        }

        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef,
            ref float damage, ref float knockbackForce)
        {
            if (_phase != Phase.Loop || _hitDuringLoop) return;
            _hitDuringLoop = true;

            // Transition to attack phase immediately
            _phase = Phase.Attack;
            _phaseTicks = 0;
            AnimIndex = 1;
            attacker.ComboStage = 1;
            attacker.VX = 0f;
            attacker.VZ = 0f;
            attacker.VY = 0f;

            ushort attackDur = (ushort)GetParam(attackerDef, "attack_duration", 36f);
            attacker.AnimLockTicks = attackDur;
        }

        private void TransitionToEnd(ref CharacterState s, CharacterDefinition def)
        {
            _phase = Phase.End;
            _phaseTicks = 0;
            AnimIndex = 2;
            s.ComboStage = 2;
            s.VX = 0f; s.VZ = 0f;

            ushort endDur = (ushort)GetParam(def, "end_duration", 15f);
            s.AnimLockTicks = endDur;
        }
    }
}
