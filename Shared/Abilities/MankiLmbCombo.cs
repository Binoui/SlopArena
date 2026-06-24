using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's LMB combo: 3-hit melee chain with forward lunge.
    /// Each stage applies lunge velocity for the first N ticks to close gaps up to 5m.
    /// Chains to next stage when player buffers input during chain window.
    /// </summary>
    public class MankiLmbCombo : ServerAbility
    {
        private byte _stage;
        private ushort _stageTicks;
        private ushort _lungeDuration;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _stage = 0;
            _stageTicks = 0;
            _lungeDuration = (ushort)GetParam(def, "lunge_duration", 10f);

            var stage = GetCurrentStage(def);

            s.State = ActionState.Attacking;
            s.AnimIndex = 0;
            s.AnimLockTicks = stage.DurationTicks;

            // Apply initial lunge velocity
            if (stage.LungeForce > 0f)
                SetVelocityInFacing(ref s, stage.LungeForce);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            var stage = GetCurrentStage(def);
            _stageTicks++;

            // Apply lunge velocity for first N ticks
            if (_stageTicks <= _lungeDuration && stage.LungeForce > 0f)
                SetVelocityInFacing(ref s, stage.LungeForce);

            // Spawn hitboxes at their trigger ticks
            foreach (var evt in stage.HitboxEvents)
            {
                if (evt.TriggerTick == _stageTicks)
                    SpawnHitbox(ref s, evt);
            }

            // Chain check: advance to next stage if input matches and within chain window
            var stages = def.GetSlotAbility(Slot, false).Stages;
            if (input.ActiveSlot == (Slot + 1)
                && _stageTicks >= stage.DurationTicks - stage.ChainWindowTicks
                && _stage < stages.Length - 1)
            {
                // Consume the buffered input
                input.ActiveSlot = 0;

                // Advance to next stage
                _stage++;
                _stageTicks = 0;
                s.AnimIndex = _stage;
                s.AnimLockTicks = stages[_stage].DurationTicks;

                // Apply lunge velocity for new stage
                if (stages[_stage].LungeForce > 0f)
                    SetVelocityInFacing(ref s, stages[_stage].LungeForce);

                return; // don't end this tick
            }

            // End check: duration expired and no chain triggered
            if (_stageTicks >= stage.DurationTicks)
                EndAbility(ref s);
        }

        private AttackStage GetCurrentStage(CharacterDefinition def)
        {
            return def.GetSlotAbility(Slot, false).Stages[_stage];
        }
    }
}
