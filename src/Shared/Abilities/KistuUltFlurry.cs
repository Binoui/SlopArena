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
    private readonly KistuBladeFlurryCapabilityParameters _parameters;
    private ushort _ticks;

    public KistuUltFlurry(KistuBladeFlurryCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    public KistuUltFlurry(CookedKistuBladeFlurryCapabilityParameters parameters)
        : this(new KistuBladeFlurryCapabilityParameters(parameters.ForwardSpeed, parameters.MoveTicks))
    {
    }
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

        SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        var spec = def.GetSlotAbility(Slot, airborne: false);
        ushort duration = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)64;
        if (_ticks <= _parameters.MoveTicks)
            SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
        else { s.VX = 0f; s.VZ = 0f; }

        var events = spec?.Stages is { Length: > 0 } ? spec.Stages[0].HitboxEvents : null;
        if (events == null || events.Length == 0)
            events = new[]
            {
                new HitboxEvent { TriggerTick = 8, DurationTicks = 4, Shape = HitboxShape.Capsule, Radius = 0.5f, OffY = 0.7f, OffZ = 0.6f, EndOffY = 0.7f, EndOffZ = 1.9f, Damage = 3f, Knockback = new KnockbackData { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = false },
                new HitboxEvent { TriggerTick = 16, DurationTicks = 4, Shape = HitboxShape.Capsule, Radius = 0.5f, OffY = 0.7f, OffZ = 0.6f, EndOffY = 0.7f, EndOffZ = 1.9f, Damage = 3f, Knockback = new KnockbackData { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = false },
                new HitboxEvent { TriggerTick = 24, DurationTicks = 4, Shape = HitboxShape.Capsule, Radius = 0.5f, OffY = 0.7f, OffZ = 0.6f, EndOffY = 0.7f, EndOffZ = 1.9f, Damage = 3f, Knockback = new KnockbackData { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = false },
                new HitboxEvent { TriggerTick = 32, DurationTicks = 4, Shape = HitboxShape.Capsule, Radius = 0.5f, OffY = 0.7f, OffZ = 0.6f, EndOffY = 0.7f, EndOffZ = 1.9f, Damage = 3f, Knockback = new KnockbackData { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = false },
                new HitboxEvent { TriggerTick = 44, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.6f, OffY = 0.8f, OffZ = 0.6f, EndOffY = 0.8f, EndOffZ = 2f, Damage = 12f, Knockback = new KnockbackData { Profile = KnockbackProfile.Custom, Angle = 20, BaseKnockback = 14f, KnockbackGrowth = 8f }, StunTicks = 24, Interruptible = false }
            };
        foreach (var evt in events)
            if (evt.TriggerTick == _ticks)
                SpawnHitbox(ref s, evt);

        if (_ticks >= duration)
            EndAbility(ref s);
    }
    public override void OnCancel(ref CharacterState s)
    {
        s.VX = 0f;
        s.VZ = 0f;
    }
}
