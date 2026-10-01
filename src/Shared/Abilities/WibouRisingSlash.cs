using System;

namespace SlopArena.Shared.Abilities;

/// Wibou's E — Rising Slash (signature). A camera-directed rising recovery.
///
/// - Rises vertically for "rise_ticks", carrying Wibou up (doubles as vertical recovery).
/// - Moves horizontally in the camera direction captured when the move starts, so the
///   recovery remains controllable and does not pull Wibou toward a target.
/// - Spawns its launcher hitbox from the spec's Stages[0].HitboxEvents (authored in Wibou's
///   package), like every other slot — the launch angle/damage/knockback live in the spec,
///   not in code.
/// - Limited by a refundable charge pool (see ServerSimulation charge-stock gate): each cast
///   spends one charge; landing a hit refunds it (OnHitEntity) so a connected juggle sustains,
///   while whiffing in empty air burns charges — capping recovery to the pool size.
///
/// Charge-pool params (read by the sim): "max_charges", "charge_regen_ticks".
/// Movement params: "rise_speed", "rise_ticks", "homing_speed" (horizontal recovery speed).
/// "homing_range" remains in the cooked compatibility schema but is no longer used for targeting.
/// </summary>
public sealed class WibouRisingSlash : ServerAbility
{
    public override bool OwnsVerticalMotion => true;

    private readonly CookedWibouRisingSlashCapabilityParameters _parameters;
    private ushort _ticks;
    private ushort _duration;
    private float _recoveryYaw;

    public WibouRisingSlash(CookedWibouRisingSlashCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        _ticks = 0;
        _recoveryYaw = s.AimYaw;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        s.FacingYaw = _recoveryYaw;
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;

        SetVelocity(ref s, 0f, _parameters.RiseSpeed, 0f);
        s.IsGrounded = false; // launch off the ground so the rise isn't clamped

        var spec = def.GetSlotAbility(Slot, airborne: false);
        _duration = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)24;
        s.AnimLockTicks = _duration;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        _ticks++;

        float riseSpeed = _parameters.RiseSpeed;
        ushort riseTicks = _parameters.RiseTicks;

        // Vertical rise for the rise window, then hold (gravity resumes when the ability ends).
        s.VY = _ticks <= riseTicks ? riseSpeed : 0f;

        // Recovery direction is chosen from the camera at activation, never from target position.
        float horizontalSpeed = _parameters.HomingSpeed;
        s.VX = MathF.Sin(_recoveryYaw) * horizontalSpeed;
        s.VZ = MathF.Cos(_recoveryYaw) * horizontalSpeed;

        var spec = def.GetSlotAbility(Slot, airborne: false);
        var events = spec?.Stages is { Length: > 0 } && spec.Stages[0].HitboxEvents is { Length: > 0 }
            ? spec.Stages[0].HitboxEvents
            : new[]
            {
                new HitboxEvent
                {
                    TriggerTick = 5, DurationTicks = 8, Shape = HitboxShape.Sphere, Radius = 0.9f, OffY = 1f, OffZ = 0.6f,
                    Damage = 7f, Knockback = new KnockbackData { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 7f, KnockbackGrowth = 5f },
                    StunTicks = 22, Interruptible = true
                }
            };
        foreach (var evt in events)
            if (evt.TriggerTick == _ticks)
                SpawnHitbox(ref s, evt);

        if (_ticks >= _duration)
            EndAbility(ref s);
    }

    /// <summary>Refund the spent charge on a connect so a landed juggle keeps its charges.</summary>
    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, CharacterDefinition targetDef,
        ref float damage, ref float knockbackForce)
    {
        if (attacker.ChargeStockSpent > 0)
        {
            attacker.ChargeStockSpent--;
            // Refunding to a full pool stops regen; clear the stale timer so the NEXT spend
            // starts a fresh full regen period rather than reusing the partial countdown.
            if (attacker.ChargeStockSpent == 0)
                attacker.ChargeStockRegenTicks = 0;
        }
    }
    public override void OnCancel(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
    }
}
