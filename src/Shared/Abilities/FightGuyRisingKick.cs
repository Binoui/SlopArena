using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's E-slot upward-mobility move (issue #117) — the ADR-0015 up-B analog.
    ///
    /// A rising kick: upward velocity burst (adds to current VY — momentum-preserve) with
    /// optional hitboxes from the spec's stage. The spec carries <c>IsRecoveryMove</c>, so
    /// the engine resets AirTimeTicks (FloatWindow) when activated airborne — the move is
    /// an anti-air launcher on the ground and a recovery burst in the air, one shared spec.
    ///
    /// Data (AbilitySpec):
    ///   - Params["burst_vy"]      — vertical impulse added to current VY (required, default 12)
    ///   - Stages[0].DurationTicks — active duration (default 20)
    ///   - Stages[0].HitboxEvents  — optional hitboxes (anti-air on the ground spec)
    ///   - CooldownTicks           — long per-entity cooldown (applied by the simulation on end)
    /// </summary>
    public class FightGuyRisingKick : ServerAbility
    {
        private ushort _ticks;
        private ushort _duration;
        private bool _airborne;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _airborne = !s.IsGrounded;
            _duration = ResolveDuration(def);

            var spec = def.GetSlotAbility(Slot, _airborne);
            float burstVy = (spec?.Params != null && spec.Params.TryGetValue("burst_vy", out var bv)) ? bv : 12f;

            s.State = ActionState.Attacking;
            AnimIndex = 0;
            s.AnimLockTicks = _duration;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            // Momentum-preserve: the burst ADDS to current velocity — drift and fall carry in.
            s.VY += burstVy;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;

            var spec = def.GetSlotAbility(Slot, _airborne);
            if (spec != null && spec.Stages != null && spec.Stages.Length > 0)
            {
                var stage = spec.Stages[0];
                foreach (var evt in stage.HitboxEvents)
                {
                    if (evt.TriggerTick == _ticks)
                        SpawnHitbox(ref s, evt);
                }
            }

            if (_ticks >= _duration)
                EndAbility(ref s);
        }

        private ushort ResolveDuration(CharacterDefinition def)
        {
            var spec = def.GetSlotAbility(Slot, _airborne);
            if (spec != null && spec.Stages != null && spec.Stages.Length > 0 && spec.Stages[0].DurationTicks > 0)
                return spec.Stages[0].DurationTicks;
            return 20;
        }
    }
}
