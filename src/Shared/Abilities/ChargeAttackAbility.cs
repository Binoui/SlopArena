using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Base class for hold-to-charge melee abilities (RMB slot).
///
/// Two-phase lifecycle with built-in release detection:
///   Phase 0 (Hold):  AnimIndex=0. Accumulates charge while input.IsAiming.
///   Phase 1 (Attack): AnimIndex=1. Uses ChargedStages[0] if charge >= threshold,
///                     else spec's attack stage (usually Stages[1]).
///
/// Release conditions (checked each tick during hold):
///   - Manual: !input.IsAiming after 5-tick debounce
///   - Auto: charge >= ChargeHoldTicks or 5s failsafe (300 ticks)
///
/// Subclasses override hooks for per-character effects (lunge, etc.):
///   OnChargeStart — called at end of OnStart, after base state is set
///   OnAttackStart — called on release transition to attack phase, with the chosen stage
/// </summary>
public abstract class ChargeAttackAbility : ServerAbility
{
    private enum Phase { Hold, Attack }
    private Phase _phase;
    private ushort _phaseTicks;
    private ushort _chargeTicks;
    private bool _wasCharged;
    private ushort _chargeHoldTicks;
    private ushort _attackDuration;

    private const ushort DebounceTicks = 5;
    private const ushort MaxHoldTicks = 300;

    /// <summary>
    /// True when this charge attack reads the AIRBORNE slot spec (AirRMB) instead of the
    /// ground spec. Slot lookup in <see cref="ChargeAttackAbility"/> routes through this,
    /// so air variants reuse the exact same hold/release lifecycle.
    /// </summary>
    protected virtual bool IsAirborne => false;

    /// <summary>Override to apply charge-phase lunge or other per-character effects.</summary>
    protected virtual void OnChargeStart(ref CharacterState s, CharacterDefinition def) { }

    /// <summary>Override to apply attack-phase lunge on release transition.</summary>
    protected virtual void OnAttackStart(ref CharacterState s, CharacterDefinition def, AttackStage stage) { }

    /// <summary>
    /// Override to apply per-tick effects during the attack phase (e.g. driving
    /// <c>AttackStage.MoveX/MoveY/MoveZ</c> velocity — air slams). Called every tick of the
    /// attack phase with the resolved stage, after hitbox triggers are processed.
    /// </summary>
    protected virtual void OnAttackTick(ref CharacterState s, CharacterDefinition def, AttackStage stage) { }

    /// <summary>Get the charge hold threshold from the spec. Subclasses can override for a custom default.</summary>
    protected virtual ushort GetChargeHoldTicks(CharacterDefinition def, ushort fallback)
    {
        var spec = def.GetSlotAbility(Slot, IsAirborne);
        return spec?.ChargeHoldTicks ?? fallback;
    }

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Hold;
        _phaseTicks = 0;
        _chargeTicks = 0;
        _wasCharged = false;
        _chargeHoldTicks = GetChargeHoldTicks(def, 45);

        s.State = ActionState.Attacking;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        AnimIndex = 0;

        OnChargeStart(ref s, def);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _phaseTicks++;
        // Sync internal charge state to struct for test/debug visibility
        s.ChargeTicks = _chargeTicks;

        if (_phase == Phase.Hold)
        {
            // Accumulate charge while holding
            if (input.IsAiming && _chargeTicks < _chargeHoldTicks)
                _chargeTicks++;

            // Check release conditions
            bool shouldRelease = false;
            if (!input.IsAiming && _phaseTicks >= DebounceTicks)
                shouldRelease = true;
            if (_chargeTicks >= _chargeHoldTicks || _phaseTicks >= MaxHoldTicks)
                shouldRelease = true;

            if (shouldRelease)
            {
                _phase = Phase.Attack;
                _phaseTicks = 0;
                _wasCharged = _chargeTicks >= _chargeHoldTicks;
                s.ComboStage = 1;
                s.AttackElapsedTicks = 0;
                AnimIndex = 1;

                // Resolve attack stage and duration
                var spec = def.GetSlotAbility(Slot, IsAirborne);
                AttackStage? atkStage = null;
                if (_wasCharged && spec?.ChargedStages != null && spec.ChargedStages.Length > 0)
                    atkStage = spec.ChargedStages[0];
                else if (spec?.Stages is { Length: > 1 })
                    atkStage = spec.Stages[1];

                if (atkStage.HasValue)
                {
                    _attackDuration = atkStage.Value.DurationTicks;
                    OnAttackStart(ref s, def, atkStage.Value);
                }
                else
                {
                    _attackDuration = 58;
                }
            }
        }

        if (_phase == Phase.Attack)
        {
            var spec = def.GetSlotAbility(Slot, IsAirborne);
            if (spec == null) { EndAbility(ref s); return; }

            AttackStage stage;
            if (_wasCharged && spec.ChargedStages != null && spec.ChargedStages.Length > 0)
                stage = spec.ChargedStages[0];
            else
                stage = spec.Stages[Math.Min(s.ComboStage, spec.Stages.Length - 1)];

            // Spawn hitboxes at trigger ticks
            foreach (var evt in stage.HitboxEvents)
            {
                if (evt.TriggerTick == _phaseTicks)
                    SpawnHitbox(ref s, evt);
            }

            // Per-tick stage effects (air slams drive MoveX/MoveY/MoveZ here)
            OnAttackTick(ref s, def, stage);

            if (_phaseTicks >= _attackDuration)
                EndAbility(ref s);
        }
    }

    public override void OnEnd(ref CharacterState s)
    {
        s.ChargeTicks = 0;
    }
}
