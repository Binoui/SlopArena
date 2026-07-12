using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's E — Cyclone Kick: forward engage with stun.
    /// FightGuy spins forward with a powerful kick. High stun on hit sets up
    /// combo follow-ups. Risky: windup frames with no hitbox before the kick.
    /// Works both grounded and airborne.
    /// </summary>
    public class FightGuyCycloneKick : ServerAbility
    {
        private ushort _ticks;
        private ushort _windupTicks;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _windupTicks = (ushort)GetParam(def, "windup_ticks", 8f);

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            // Forward lunge
            float forwardSpeed = GetParam(def, "forward_speed", 14f);
            SetVelocityInFacing(ref s, forwardSpeed);

            ushort duration = (ushort)GetParam(def, "duration_ticks", 35f);
            s.AnimLockTicks = duration;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;

            // Maintain forward velocity during lunge
            float lungeDuration = GetParam(def, "lunge_duration", 10f);
            if (_ticks <= lungeDuration)
            {
                float forwardSpeed = GetParam(def, "forward_speed", 14f);
                SetVelocityInFacing(ref s, forwardSpeed);
            }

            // Spawn hitbox after windup
            if (_ticks > _windupTicks)
            {
                var stage = def.GetSlotAbility(Slot, false).Stages[0];
                foreach (var evt in stage.HitboxEvents)
                {
                    if (evt.TriggerTick == _ticks)
                        SpawnHitbox(ref s, evt);
                }
            }

            ushort duration = (ushort)GetParam(def, "duration_ticks", 35f);
            if (_ticks >= duration)
                EndAbility(ref s);
        }
    }
}
