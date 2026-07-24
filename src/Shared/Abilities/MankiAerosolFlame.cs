using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Manki RMB (slot 1): Aerosol + Lighter — hold-to-charge flame burst.
/// Charge threshold: 45 ticks (0.75s). No lunge.
/// </summary>
public sealed class MankiAerosolFlame : ChargeAttackAbility
{
    protected override ushort GetChargeHoldTicks(CharacterDefinition def, ushort fallback)
        => base.GetChargeHoldTicks(def, 45);
}
