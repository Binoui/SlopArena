using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Generic combo ability that reads from AttackStage data.
    /// Chains through stages when the player inputs the same slot during the chain window.
    /// </summary>
    public class MeleeCombo : ServerAbility
    {
        private byte _stage;
        private ushort _stageTicks;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            var stage = GetCurrentStage(def);

            _stage = 0;
            _stageTicks = 0;

            s.State = ActionState.Attacking;
            s.AnimIndex = 0;
            s.AnimLockTicks = stage.DurationTicks;

            if (stage.LungeForce > 0f)
                SetVelocityInFacing(ref s, stage.LungeForce);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            var stage = GetCurrentStage(def);
            _stageTicks++;

            // Spawn hitboxes at their trigger ticks
            foreach (var evt in stage.HitboxEvents)
            {
                if (evt.TriggerTick == _stageTicks)
                    SpawnHitbox(ref s, evt);
            }

            // Chain check: advance to next stage if input matches and within chain window
            var stages = def.GetSlotAbility(Slot, false).Stages;
            if (input.ActiveSlot == (Slot + 1)
                && _stageTicks >= stage.ChainWindowTicks
                && _stage < stages.Length - 1)
            {
                // Consume the buffered input
                input.ActiveSlot = 0;

                // Advance to next stage
                _stage++;
                _stageTicks = 0;
                s.AnimIndex = _stage;
                s.AnimLockTicks = stages[_stage].DurationTicks;

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
