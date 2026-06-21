using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Backflip roll: leaps forward 8 ticks, then backflips up+away for 10 ticks.
    /// Spawns a melee hitbox at tick 6 during the transition.
    /// </summary>
    public class BackflipRoll : ServerAbility
    {
        private const ushort LeapTicks = 8;
        private const ushort FlipTicks = 10;

        private bool _hitboxSpawned;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            s.State = ActionState.Attacking;
            s.AnimIndex = 0;
            s.AnimLockTicks = (ushort)(LeapTicks + FlipTicks);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort totalDuration = (ushort)(LeapTicks + FlipTicks);

            // ── Phase 1: spring forward ──
            if (s.AttackElapsedTicks < LeapTicks)
            {
                float fwd = GetParam(def, "backflip_leap_speed", 8f);
                float up  = GetParam(def, "backflip_jump_height", 6f);
                SetVelocityInFacing(ref s, fwd, up);
            }
            // ── Phase 2: backflip away ──
            else
            {
                float back = GetParam(def, "backflip_flip_speed", 6f);
                float up   = GetParam(def, "backflip_flip_height", 8f);
                SetVelocityInFacing(ref s, -back, up);
            }

            // ── One-shot hitbox at tick 6 ──
            if (!_hitboxSpawned && s.AttackElapsedTicks >= 6)
            {
                _hitboxSpawned = true;

                SpawnHitbox(ref s, new HitboxEvent
                {
                    TriggerTick = 6,
                    DurationTicks = 4,
                    Shape = HitboxShape.Sphere,
                    Radius = GetParam(def, "backflip_radius", 1f),
                    Damage = GetParam(def, "backflip_damage", 8f),
                    KnockbackForce = GetParam(def, "backflip_knockback", 5f),
                    StunTicks = 10,
                    Interruptible = true,
                });
            }

            // ── End when full duration has elapsed ──
            if (s.AttackElapsedTicks >= totalDuration)
                EndAbility(ref s);
        }
    }
}
