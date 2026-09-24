using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Base class for LMB melee abilities (issue #115 — single moves, no chains).
    /// Pressing the slot performs exactly one move: stage 0 plays start to finish
    /// (lunge window, hitbox triggers, duration), then the ability ends. Repeated
    /// presses during the move are ignored by the chain machinery — the general
    /// input buffer (Simulation.cs) may queue a fresh activation after the move
    /// ends, which is the standard fighting-game press-buffer, not a chain.
    ///
    /// Momentum is preserved by default (ADR-0015): the lunge sets a velocity the
    /// character coasts on; nothing is zeroed mid-move or at end — ground friction
    /// and air control resume when the ability returns to Idle.
    /// </summary>
    public abstract class StageChainAbility : ServerAbility
    {
        private ushort _ticks;
        private float _currentLungeForce;

        public override bool OwnsVerticalMotion => _currentLungeForce > 0f;

        private ushort _lungeDuration;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _lungeDuration = (ushort)GetParam(def, "lunge_duration", 10f);

            var stage = GetStages(def)[0];
            _currentLungeForce = stage.LungeForce;

            s.State = ActionState.Attacking;
            AnimIndex = 0;
            s.AnimLockTicks = stage.DurationTicks;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            if (stage.LungeForce > 0f)
            {
                s.IsFastFalling = false;
                ClearVelocityOwnership(ref s);
                SetVelocityInFacing(ref s, stage.LungeForce);
            }
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            var stage = GetStages(def)[0];
            _ticks++;
            _currentLungeForce = stage.LungeForce;

            if (s.WarpSpeed <= 0f && _ticks <= _lungeDuration && stage.LungeForce > 0f)
            {
                s.IsFastFalling = false;
                ClearVelocityOwnership(ref s);
                SetVelocityInFacing(ref s, stage.LungeForce);
            }

            foreach (var evt in stage.HitboxEvents)
            {
                if (evt.TriggerTick == _ticks)
                    SpawnHitbox(ref s, evt);
            }

            if (_ticks >= stage.DurationTicks)
                EndAbility(ref s);
        }

        public override void OnEnd(ref CharacterState s)
            => _currentLungeForce = 0f;

        public override void OnCancel(ref CharacterState s)
            => _currentLungeForce = 0f;

        /// <summary>Return the stage definitions for this ability.</summary>
        protected abstract AttackStage[] GetStages(CharacterDefinition def);
    }
}
