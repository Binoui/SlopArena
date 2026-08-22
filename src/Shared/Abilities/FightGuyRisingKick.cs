using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's E-slot upward-mobility move (issue #117) — the ADR-0015 up-B analog.
    ///
    /// A rising punch: Melee Falcon-Dive-style recovery. VY is SET to <c>rise_speed</c>
    /// (cancels fall/drift — reliable recovery, not additive) and held at that constant speed
    /// for <c>rise_ticks</c> (overrides gravity: TickAbilities runs after ApplyGravity), then
    /// gravity resumes. Optional hitboxes from the spec's stage.
    ///
    /// Grounded cast (anti-air launcher) delays the rise by <c>rise_delay</c> (default 8 —
    /// matches the animation windup), so the authored hitboxes at trigger ticks 6-10 connect
    /// while the body is still low, before the launch. Airborne cast (recovery burst) rises
    /// immediately: a windup there would eat ticks of fall before the burst.
    ///
    /// The spec carries <c>IsRecoveryMove</c>, so the engine resets AirTimeTicks (FloatWindow)
    /// when activated airborne — but the ability closes that float window once the rise ends, so
    /// gravity bounds the arc into a Melee up-B height instead of rocketing on the float.
    ///
    /// Data (AbilitySpec):
    ///   - Params["rise_speed"]     — vertical speed held during the rise (required, default 11)
    ///   - Params["rise_ticks"]     — ticks the rise is sustained (required, default 12)
    ///   - Params["rise_delay"]     — grounded-only windup before the rise (default 8; air = 0)
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
            float riseSpeed = (spec?.Params != null && spec.Params.TryGetValue("rise_speed", out var rs)) ? rs : 11f;

            // E commits to the camera direction at activation. Recovery movement must not
            // inherit target-lock rotation; ProcessTargetLock is disabled for this stage.
            s.FacingYaw = s.AimYaw;

            s.State = ActionState.Attacking;
            AnimIndex = 0;
            s.AnimLockTicks = _duration;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;

            // Airborne cast: SET (not add) — cancels incoming fall/drift, the reliable-recovery
            // contract (Falcon Dive / Dolphin Slash overwrite VY; they don't stack on it).
            // Grounded cast: stay planted for the windup (rise_delay) — the rise starts in Tick
            // so the hitboxes at tick 6-10 connect while the body is still low.
            if (_airborne)
            {
                s.VY = riseSpeed;
                s.IsGrounded = false; // launch off the ground so the rise isn't clamped
            }
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _ticks++;

            var spec = def.GetSlotAbility(Slot, _airborne);

            // Sustained constant rise for the rise window (runs after ApplyGravity, so this
            // overrides gravity each tick — Falcon-Dive hold). After the window, gravity must
            // brake the rise into a bounded arc: the engine's IsRecoveryMove reset opened a
            // 0-gravity FloatWindow at activation, so close it here or the rise rockets on the
            // float (AirTimeTicks >= FloatWindowTicks → full Gravity applies next tick).
            float riseSpeed = (spec?.Params != null && spec.Params.TryGetValue("rise_speed", out var rs)) ? rs : 11f;
            ushort riseTicks = (spec?.Params != null && spec.Params.TryGetValue("rise_ticks", out var rt)) ? (ushort)rt : (ushort)12;
            // Grounded-only windup (animation-authored): the air cast rises immediately.
            ushort riseDelay = _airborne ? (ushort)0
                : (spec?.Params != null && spec.Params.TryGetValue("rise_delay", out var rd)) ? (ushort)rd : (ushort)8;
            if (_ticks >= riseDelay)
            {
                if (_ticks <= riseDelay + riseTicks)
                {
                    s.VY = riseSpeed;
                    if (s.IsGrounded)
                        s.IsGrounded = false; // leave the ground when the rise starts
                }
                else if (s.AirTimeTicks < def.Movement.FloatWindowTicks)
                    s.AirTimeTicks = def.Movement.FloatWindowTicks;
            }

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
