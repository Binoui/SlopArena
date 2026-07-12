using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Base class for multi-stage chain abilities (LMB combos).
    /// Each stage has a duration, optional lunge, hitbox events, and chains to the
    /// next stage when the player presses LMB at any point during the stage.
    ///
    /// Key invariants:
    /// - Input is buffered immediately on press, chain fires on stage end.
    /// - AttackElapsedTicks is reset on each stage transition.
    /// - Last stage never chains (guard prevents setting _chainBuffered).
    /// </summary>
    public abstract class StageChainAbility : ServerAbility
    {
        private byte _stage;
        private ushort _stageTicks;
        private ushort _lungeDuration;
        private bool _chainBuffered;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _stage = 0;
            _stageTicks = 0;
            _chainBuffered = false;
            _lungeDuration = (ushort)GetParam(def, "lunge_duration", 10f);

            var stage = GetCurrentStage(def);

            s.State = ActionState.Attacking;
            AnimIndex = 0;
            s.AnimLockTicks = stage.DurationTicks;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            // Apply initial lunge velocity
            if (stage.LungeForce > 0f)
                SetVelocityInFacing(ref s, stage.LungeForce);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            var stage = GetCurrentStage(def);
            _stageTicks++;

			// Apply lunge velocity for first N ticks (skip when warp is active — warp handles movement)
			if (s.WarpSpeed <= 0f && _stageTicks <= _lungeDuration && stage.LungeForce > 0f)
				SetVelocityInFacing(ref s, stage.LungeForce);

            // Spawn hitboxes at their trigger ticks
            foreach (var evt in stage.HitboxEvents)
            {
                if (evt.TriggerTick == _stageTicks)
                    SpawnHitbox(ref s, evt);
            }

            // Buffer phase — any LMB press during the stage is buffered for chain.
            // Always consume the input to prevent leaking into the next tick
            // (which would create a fresh combo via PreTickAbilities).
            var stages = GetStages(def);
            if (input.ActiveSlot == (Slot + 1))
            {
                if (_stage < stages.Length - 1)
                {
                    _chainBuffered = true;
                }
                else if (Simulation.OnDebugLog != null)
                {
                    Simulation.OnDebugLog.Invoke(
                        $"[StageChainAbility] entity={s.EntityId} slot={Slot} stage={_stage} (last) — consumed LMB press without chaining");
                }
                input.ActiveSlot = 0;
            }

            // Early chain — input buffered and within chain window, chain immediately
            if (_chainBuffered && _stage < stages.Length - 1
                && stage.ChainWindowTicks > 0
                && _stageTicks >= (ushort)(stage.DurationTicks - stage.ChainWindowTicks))
            {
                _stage++;
                _stageTicks = 0;
                _chainBuffered = false;
                AnimIndex = _stage;
                s.ComboStage = _stage;
                s.AnimLockTicks = stages[_stage].DurationTicks;
                s.AttackElapsedTicks = 0;

                // Apply lunge velocity for new stage (skip when warp is active)
                if (s.WarpSpeed <= 0f && stages[_stage].LungeForce > 0f)
                    SetVelocityInFacing(ref s, stages[_stage].LungeForce);

                return;
            }

            // End phase — stage fully expired, chain if buffered
            if (_stageTicks >= stage.DurationTicks)
            {
                if (_chainBuffered && _stage < stages.Length - 1)
                {
                    _stage++;
                    _stageTicks = 0;
                    _chainBuffered = false;
                    AnimIndex = _stage;
                    s.ComboStage = _stage;
                    s.AnimLockTicks = stages[_stage].DurationTicks;
                    s.AttackElapsedTicks = 0;

                    // Apply lunge velocity for new stage (skip when warp is active)
                    if (s.WarpSpeed <= 0f && stages[_stage].LungeForce > 0f)
                        SetVelocityInFacing(ref s, stages[_stage].LungeForce);
                }
                else
                {
                    EndAbility(ref s);
                }
            }
        }

        /// <summary>Return the stage definitions for this ability.</summary>
        protected abstract AttackStage[] GetStages(CharacterDefinition def);

        private AttackStage GetCurrentStage(CharacterDefinition def)
        {
            var stages = GetStages(def);
            int idx = Math.Min(_stage, (byte)(stages.Length - 1));
            return stages[idx];
        }
    }
}
