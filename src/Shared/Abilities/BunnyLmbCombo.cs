using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Bunny's LMB combo: 3-hit melee chain ("Rabbit Combo") with forward lunge.
    /// Follows MankiLmbCombo pattern — each stage applies lunge velocity,
    /// chains to next stage on buffered input during chain window.
    /// Stage 3 is final (ChainWindowTicks=0).
    /// </summary>
    public class BunnyLmbCombo : ServerAbility
    {
        private byte _stage;
        private ushort _stageTicks;
        private ushort _lungeDuration;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _stage = 0;
            _stageTicks = 0;
            _lungeDuration = (ushort)GetParam(def, "lunge_duration", 8f);

            var stage = GetCurrentStage(def);

            s.State = ActionState.Attacking;
            AnimIndex = 0;
            s.AnimLockTicks = stage.DurationTicks;
            s.ComboStage = 0;

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
                AnimIndex = _stage;
                s.ComboStage = _stage;
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
            var stages = def.GetSlotAbility(Slot, false).Stages;
            int idx = Math.Min(_stage, (byte)(stages.Length - 1));
            return stages[idx];
        }
    }
}
