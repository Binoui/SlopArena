using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Bunny's E — Flip Kick: backward disengage.
    /// Single-stage ability that applies backward + upward self-velocity
    /// (a backflip), then spawns a hitbox behind the character.
    /// OnStart applies the launch velocity; Tick waits for duration+hitbox, then ends.
    /// </summary>
    public class BunnyFlipKick : ServerAbility
    {
        private ushort _ticks;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            // Backward velocity (negative facing direction) + upward
            float backwardSpeed = GetParam(def, "self_backward_speed", 12f);
            float upwardVelocity = GetParam(def, "self_upward_velocity", 4f);
            SetVelocityInFacing(ref s, -backwardSpeed, upwardVelocity);

            ushort duration = (ushort)GetParam(def, "duration_ticks", 40f);
            s.AnimLockTicks = duration;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;

            // Spawn hitbox at trigger tick
            var stage = def.GetSlotAbility(Slot, false).Stages[0];
            foreach (var evt in stage.HitboxEvents)
            {
                if (evt.TriggerTick == _ticks)
                    SpawnHitbox(ref s, evt);
            }

            ushort duration = (ushort)GetParam(def, "duration_ticks", 40f);
            if (_ticks >= duration)
                EndAbility(ref s);
        }
    }
}
