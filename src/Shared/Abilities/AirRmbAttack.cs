using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// AirRMB (slot 1, airborne): single-hit aerial spike attack shared by all characters.
/// Reads stage data from the character's AirRMB spec (CharacterDefinition.AirRMB).
///
/// Single stage: spawns hitbox at TriggerTick, applies lunge if configured,
/// ends naturally after DurationTicks.
/// </summary>
public sealed class AirRmbAttack : ServerAbility
{
    private ushort _ticks;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;

        var spec = def.GetSlotAbility(Slot, airborne: true);
        s.State = ActionState.Attacking;
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;

        // Apply lunge from spec's first stage
        if (spec?.Stages is { Length: > 0 } && spec.Stages[0].LungeForce > 0f)
            SetVelocityInFacing(ref s, spec.Stages[0].LungeForce);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        var spec = def.GetSlotAbility(Slot, airborne: true);
        if (spec?.Stages is not { Length: > 0 }) { EndAbility(ref s); return; }

        var stage = spec.Stages[0];

        // Spawn hitboxes at trigger ticks
        foreach (var evt in stage.HitboxEvents)
        {
            if (evt.TriggerTick == _ticks)
                SpawnHitbox(ref s, evt);
        }

        if (_ticks >= stage.DurationTicks)
            EndAbility(ref s);
    }
}
