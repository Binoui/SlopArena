using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Shared charge-lunge slot behaviour (Kistu RMB/E, Nilus RMB).
///
/// Reuses the ChargeAttackAbility hold-to-charge lifecycle. The only behaviour is
/// applying the chosen attack stage's LungeForce as a forward burst on release:
///   - tap    -> Stages[1]        (short poke / reposition)
///   - charge -> ChargedStages[0] (committed heavy)
///
/// All damage/knockback/hitbox geometry is data-driven from the spec.
/// </summary>
public sealed class LungeChargeAttack : ChargeAttackAbility
{
    protected override void OnAttackStart(ref CharacterState s, CharacterDefinition def, AttackStage stage)
    {
        if (stage.LungeForce != 0f)
            SetVelocityInFacing(ref s, stage.LungeForce);
    }
}
