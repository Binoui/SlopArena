using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Kistu's F — Blade Flurry (ult). A committed forward-moving multi-slash that ends in a
/// hard launch. Kept deliberately simple: moves forward for "move_ticks", spawns the spec's
/// HitboxEvents at their TriggerTicks (the final event carries the big launch knockback),
/// then ends. All hit geometry/damage/knockback is data-driven from the F spec's Stages[0].
///
/// Params: "forward_speed", "move_ticks".
/// </summary>
public sealed class KistuUltFlurry : ServerAbility
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

        var spec = def.GetSlotAbility(Slot, airborne: false);
        s.AnimLockTicks = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)60;

        SetVelocityInFacing(ref s, GetParam(def, "forward_speed", 6f));
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        var spec = def.GetSlotAbility(Slot, airborne: false);
        if (spec?.Stages is not { Length: > 0 }) { EndAbility(ref s); return; }
        var stage = spec.Stages[0];

        // Drive forward during the flurry, then plant for the finisher.
        ushort moveTicks = (ushort)GetParam(def, "move_ticks", 30f);
        if (_ticks <= moveTicks)
            SetVelocityInFacing(ref s, GetParam(def, "forward_speed", 6f));
        else { s.VX = 0f; s.VZ = 0f; }

        foreach (var evt in stage.HitboxEvents)
        {
            if (evt.TriggerTick == _ticks)
                SpawnHitbox(ref s, evt);
        }

        if (_ticks >= stage.DurationTicks)
            EndAbility(ref s);
    }
}
