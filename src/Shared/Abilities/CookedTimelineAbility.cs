using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities;

public sealed class CookedTimelineAbility : ServerAbility
{
    private readonly CookedSlotDefinition _slot;
    private readonly List<ServerAbility> _capabilities = new();
    private ushort _stageTick;
    private int _stageIndex;
    private int _operationCursor;
    private bool _completed;

    public CookedTimelineAbility(CookedSlotDefinition slot, string[] animationNames)
    {
        _slot = slot ?? throw new ArgumentNullException(nameof(slot));
        AnimationNames = animationNames ?? Array.Empty<string>();
    }

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _stageIndex = 0;
        _stageTick = 0;
        _operationCursor = 0;
        _completed = false;
        s.State = ActionState.Attacking;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.IsAiming = false;
        AnimIndex = 0;
        s.AnimLockTicks = CurrentStage.DurationTicks;
        ExecuteOperations(ref s, def);
        if (_completed)
            return;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_completed)
            return;

        _stageTick++;
        ExecuteOperations(ref s, def);
        if (_completed)
            return;

        for (var i = 0; i < _capabilities.Count; i++)
            _capabilities[i].Tick(ref s, ref input, def);

        if (_stageTick >= CurrentStage.DurationTicks)
        {
            if (_stageIndex + 1 >= _slot.Timeline.Stages.Count)
            {
                Complete(ref s);
                return;
            }

            _stageIndex++;
            _stageTick = 0;
            _operationCursor = 0;
            s.ComboStage = (byte)_stageIndex;
            AnimIndex = (byte)Math.Min(_stageIndex, Math.Max(0, AnimationNames.Length - 1));
            s.AnimLockTicks = CurrentStage.DurationTicks;
            ExecuteOperations(ref s, def);
        }
    }

    public override void OnEnd(ref CharacterState s)
    {
        CompleteCapabilities(ref s, cancel: false);
    }

    public override void OnCancel(ref CharacterState s)
    {
        CompleteCapabilities(ref s, cancel: true);
        s.IsAiming = false;
    }

    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, CharacterDefinition targetDef, ref float damage, ref float knockbackForce)
    {
        for (var i = 0; i < _capabilities.Count; i++)
            _capabilities[i].OnHitEntity(ref attacker, ref target, attackerDef, targetDef, ref damage, ref knockbackForce);
    }

    private CookedStage CurrentStage => _slot.Timeline.Stages[_stageIndex];

    private void ExecuteOperations(ref CharacterState s, CharacterDefinition def)
    {
        var operations = CurrentStage.Operations;
        while (_operationCursor < operations.Count && operations[_operationCursor].Tick == _stageTick)
        {
            var operation = operations[_operationCursor++];
            switch (operation)
            {
                case CookedSetVelocityOperation velocity:
                    if (velocity.VelocityMode == AuthoringVelocityMode.Absolute)
                    {
                        s.VX = velocity.X;
                        s.VY = velocity.Y;
                        s.VZ = velocity.Z;
                    }
                    else
                    {
                        s.VX += velocity.X;
                        s.VY += velocity.Y;
                        s.VZ += velocity.Z;
                    }
                    break;
                case CookedSpawnHitboxOperation hitbox:
                    SpawnCookedHitbox(ref s, hitbox.Hitbox);
                    break;
                case CookedSpawnProjectileOperation projectile:
                    SpawnCookedProjectile(ref s, projectile.Projectile);
                    break;
                case CookedSetAimStateOperation aim:
                    s.IsAiming = aim.AimState != AuthoringAimMode.None;
                    s.State = s.IsAiming ? ActionState.Aiming : ActionState.Attacking;
                    break;
                case CookedStartCapabilityOperation capability:
                    StartCapability(ref s, def, capability);
                    break;
                case CookedEmitPresentationOperation presentation:
                    PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, presentation.OperationIndex, presentation.PresentationId));
                    break;
                case CookedCompleteTimelineOperation:
                    Complete(ref s);
                    return;
            }
        }
    }

    private void SpawnCookedHitbox(ref CharacterState s, CookedHitbox cooked)
    {
        SpawnHitbox(ref s, new HitboxEvent
        {
            TriggerTick = _stageTick,
            DurationTicks = cooked.DurationTicks,
            Shape = cooked.Shape == AuthoringHitboxShape.Capsule ? HitboxShape.Capsule : HitboxShape.Sphere,
            Radius = cooked.Radius,
            OffX = cooked.OffsetX,
            OffY = cooked.OffsetY,
            OffZ = cooked.OffsetZ,
            EndOffX = cooked.EndOffsetX,
            EndOffY = cooked.EndOffsetY,
            EndOffZ = cooked.EndOffsetZ,
            BoneName = RuntimeBoneId(cooked.StartBoneId),
            EndBoneName = RuntimeBoneId(cooked.EndBoneId),
            Damage = cooked.Damage,
            Knockback = new KnockbackData
            {
                Profile = KnockbackProfile.Custom,
                Angle = (sbyte)cooked.Angle,
                BaseKnockback = cooked.BaseKnockback,
                KnockbackGrowth = cooked.KnockbackGrowth,
            },
            StunTicks = cooked.StunTicks,
            Interruptible = cooked.Interruptible,
            HitGroup = cooked.HitGroup,
        });
    }

    private void SpawnCookedProjectile(ref CharacterState s, CookedProjectile projectile)
    {
        float cosPitch = MathF.Cos(s.AimPitch);
        float dirX = cosPitch * MathF.Sin(s.AimYaw);
        float dirY = MathF.Sin(s.AimPitch);
        float dirZ = cosPitch * MathF.Cos(s.AimYaw);
        float cosYaw = MathF.Cos(s.FacingYaw);
        float sinYaw = MathF.Sin(s.FacingYaw);
        float offsetX = projectile.LaunchOffsetX * cosYaw + projectile.LaunchOffsetZ * sinYaw;
        float offsetZ = -projectile.LaunchOffsetX * sinYaw + projectile.LaunchOffsetZ * cosYaw;
        float damage = projectile.Damage;
        float radius = projectile.Radius;
        ApplyBuffBonuses(ref s, ref damage, ref radius);
        Resolver.Spawn(new Hitbox
        {
            X = s.PX + offsetX,
            Y = s.PY + projectile.LaunchOffsetY,
            Z = s.PZ + offsetZ,
            VX = dirX * projectile.Speed,
            VY = dirY * projectile.Speed,
            VZ = dirZ * projectile.Speed,
            Radius = radius,
            Shape = HitboxShape.Sphere,
            Damage = damage,
            BaseKnockback = projectile.BaseKnockback,
            KnockbackGrowth = projectile.KnockbackGrowth,
            KnockbackAngle = (sbyte)projectile.Angle,
            StunTicks = projectile.StunTicks,
            DurationTicks = projectile.MaxFlightTicks,
            OwnerId = s.EntityId,
            Gravity = projectile.Gravity,
        });
    }

    private void StartCapability(ref CharacterState s, CharacterDefinition def, CookedStartCapabilityOperation operation)
    {
        if (!InternalCapabilityRegistry.TryCreate(operation.CapabilityId, operation.CapabilityVersion, operation.Parameters, out var capability))
            throw new InvalidOperationException($"Capability '{operation.CapabilityId}' version '{operation.CapabilityVersion}' is not admitted.");

        capability.Resolver = Resolver;
        capability.SimulationStates = SimulationStates;
        capability.BakedData = BakedData;
        capability.CharacterDef = CharacterDef;
        capability.Arena = Arena;
        capability.Slot = Slot;
        capability.Cooldown = Cooldown;
        capability.AirborneAtStart = AirborneAtStart;
        capability.AnimationNames = AnimationNames;
        capability.PresentationSink = PresentationSink;
        _capabilities.Add(capability);
        capability.OnStart(ref s, def);
    }

    private void Complete(ref CharacterState s)
    {
        if (_completed)
            return;
        _completed = true;
        EndAbility(ref s);
    }

    private void CompleteCapabilities(ref CharacterState s, bool cancel)
    {
        for (var i = 0; i < _capabilities.Count; i++)
        {
            if (cancel)
                _capabilities[i].OnCancel(ref s);
            else
                _capabilities[i].OnEnd(ref s);
        }
        _capabilities.Clear();
    }
    private static string? RuntimeBoneId(string? value)
        => value switch
        {
            "bone.head" => "mixamorig:Head",
            "bone.hips" => "mixamorig:Hips",
            "bone.spine" => "mixamorig:Spine2",
            "bone.right-hand" => "mixamorig:RightHand",
            "bone.left-hand" => "mixamorig:LeftHand",
            "bone.right-foot" => "mixamorig:RightFoot",
            "bone.left-foot" => "mixamorig:LeftFoot",
            null => null,
            _ => value,
        };

}
