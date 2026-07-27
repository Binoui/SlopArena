using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Kistu's charge-lunge slots (RMB Charged Spin, E Charged Dash Slash).
///
/// Reuses the ChargeAttackAbility hold-to-charge lifecycle. The only per-character
/// behaviour is applying the chosen attack stage's LungeForce as a forward burst on
/// release, giving:
///   - RMB: tap = quick horizontal poke, hold = charged spin (bigger LungeForce/hitbox).
///   - E:   tap = short reposition, hold = full gap-close (LungeForce doubles as recovery).
///
/// All damage/knockback/hitbox geometry is data-driven from the spec's Stages[1]
/// (tap) and ChargedStages[0] (charged).
/// </summary>
public sealed class KistuChargeAttack : ChargeAttackAbility
{
    protected override void OnAttackStart(ref CharacterState s, CharacterDefinition def, AttackStage stage)
    {
        if (stage.LungeForce != 0f)
            SetVelocityInFacing(ref s, stage.LungeForce);
    }
}
