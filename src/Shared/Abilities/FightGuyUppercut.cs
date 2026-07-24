using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// FightGuy RMB (slot 1): Charged Uppercut — hold-to-charge, releases on button up.
/// Charge threshold: 180 ticks (3s). Gentle lunge forward while charging,
/// stronger lunge on attack release.
/// </summary>
public sealed class FightGuyUppercut : ChargeAttackAbility
{
    protected override ushort GetChargeHoldTicks(CharacterDefinition def, ushort fallback)
        => base.GetChargeHoldTicks(def, 180);

    protected override void OnChargeStart(ref CharacterState s, CharacterDefinition def)
    {
        // Gentle lunge forward while charging
        var spec = def.GetSlotAbility(Slot, airborne: false);
        if (spec?.Stages is { Length: > 0 } && spec.Stages[0].LungeForce > 0f)
            SetVelocityInFacing(ref s, spec.Stages[0].LungeForce);
    }

    protected override void OnAttackStart(ref CharacterState s, CharacterDefinition def, AttackStage stage)
    {
        // Stronger lunge on attack release
        if (stage.LungeForce > 0f)
            SetVelocityInFacing(ref s, stage.LungeForce);
    }
}
