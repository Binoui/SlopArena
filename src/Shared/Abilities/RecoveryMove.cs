using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Shared recovery-move capability (ADR-0015, issue #115 / #108) — the Smash up-B analog.
    ///
    /// An upward/diagonal velocity burst with a long cooldown, meant to be spent once per
    /// life-or-death to recover from off-stage. It is the ONLY move type that resets the
    /// FloatWindow: the engine restores AirTimeTicks = 0 at activation when the spec's
    /// <c>IsRecoveryMove</c> flag is set (see <c>ServerSimulation.ActivateAbility</c>) —
    /// normal air attacks ride their trajectory with no hover.
    ///
    /// Data (AbilitySpec):
    ///   - Params["burst_vy"]      — vertical impulse added to current VY (required, default 12)
    ///   - Params["burst_forward"] — facing-relative horizontal impulse (default 0 = pure vertical;
    ///                               the diagonal emerges from the player's drift + this nudge)
    ///   - Stages[0].DurationTicks — active duration (default 20)
    ///   - CooldownTicks           — long per-entity cooldown (applied by the simulation on end)
    ///
    /// No hitboxes — this is a movement tool, not an attack.
    /// </summary>
    public class RecoveryMove : ServerAbility
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
            float burstForward = (spec?.Params != null && spec.Params.TryGetValue("burst_forward", out var bf)) ? bf : 0f;

            s.State = ActionState.Attacking;
            AnimIndex = 0;
            s.AnimLockTicks = _duration;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            // Momentum-preserve: the burst ADDS to current velocity — drift and fall carry in.
            s.VY += burstVy;
            if (burstForward > 0f)
            {
                s.VX += MathF.Sin(s.FacingYaw) * burstForward;
                s.VZ += MathF.Cos(s.FacingYaw) * burstForward;
            }
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;
            if (_ticks >= _duration)
                EndAbility(ref s);
        }

        private ushort ResolveDuration(CharacterDefinition def)
        {
            // Airborne flag must match the slot the move was activated with (issue #115:
            // ground LMB and AirLMB are separate specs).
            var spec = def.GetSlotAbility(Slot, _airborne);
            if (spec != null && spec.Stages != null && spec.Stages.Length > 0 && spec.Stages[0].DurationTicks > 0)
                return spec.Stages[0].DurationTicks;
            return 20;
        }
    }
}
