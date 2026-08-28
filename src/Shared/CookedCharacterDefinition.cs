using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SlopArena.Shared;
public enum CharacterDiagnosticSeverity : byte
{
    Warning = 0,
    Error = 1,
}

public sealed record CharacterDiagnostic(
    CharacterDiagnosticSeverity Severity,
    string Code,
    string Path,
    string Message);


public sealed class CookedCharacterPackage
{
    public CookedPackageMetadata Metadata { get; }
    public CookedCharacterDefinition Definition { get; }
    public CookedBudget Budget { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    private readonly byte[] _canonicalBytes;
    public byte[] CanonicalBytes => (byte[])_canonicalBytes.Clone();

    public CookedCharacterPackage(
        CookedPackageMetadata metadata,
        CookedCharacterDefinition definition,
        CookedBudget budget,
        IReadOnlyList<CharacterDiagnostic> diagnostics,
        byte[] canonicalBytes)
    {
        Metadata = metadata;
        Definition = definition;
        Budget = budget;
        Diagnostics = Copy(diagnostics);
        _canonicalBytes = (byte[])canonicalBytes.Clone();
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        => new ReadOnlyCollection<T>(new List<T>(values));
}

public sealed record CookedPackageMetadata(
    string PackageId,
    string Version,
    ushort CookedSchemaVersion,
    string RuntimeApiMin,
    string RuntimeApiMax);

public sealed class CookedCharacterDefinition
{
    public string DisplayName { get; }
    public float Weight { get; }
    public CookedMovement Movement { get; }
    public CookedPresentation Presentation { get; }
    public float CapsuleRadius { get; }
    public float CapsuleHeight { get; }
    public float HipHeight { get; }
    public float HurtboxRadius { get; }
    public IReadOnlyList<CookedHurtboxCapsule> HurtboxCapsules { get; }
    public IReadOnlyList<CookedHurtboxBone> HurtboxBoneDefs { get; }
    public IReadOnlyList<string> AttachmentBoneIds { get; }
    public IReadOnlyList<string> PresentationIds { get; }
    public IReadOnlyList<CookedCapabilityRequirement> CapabilityRequirements { get; }
    public IReadOnlyList<CookedSlotDefinition> Slots { get; }

    public CookedCharacterDefinition(
        string displayName,
        float weight,
        CookedMovement movement,
        CookedPresentation presentation,
        float capsuleRadius,
        float capsuleHeight,
        float hipHeight,
        float hurtboxRadius,
        IReadOnlyList<CookedHurtboxCapsule> hurtboxCapsules,
        IReadOnlyList<CookedHurtboxBone> hurtboxBoneDefs,
        IReadOnlyList<string> attachmentBoneIds,
        IReadOnlyList<string> presentationIds,
        IReadOnlyList<CookedCapabilityRequirement> capabilityRequirements,
        IReadOnlyList<CookedSlotDefinition> slots)
    {
        DisplayName = displayName;
        Weight = weight;
        Movement = movement;
        Presentation = presentation;
        CapsuleRadius = capsuleRadius;
        CapsuleHeight = capsuleHeight;
        HipHeight = hipHeight;
        HurtboxRadius = hurtboxRadius;
        HurtboxCapsules = Copy(hurtboxCapsules);
        HurtboxBoneDefs = Copy(hurtboxBoneDefs);
        AttachmentBoneIds = Copy(attachmentBoneIds);
        PresentationIds = Copy(presentationIds);
        CapabilityRequirements = Copy(capabilityRequirements);
        Slots = Copy(slots);
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        => new ReadOnlyCollection<T>(new List<T>(values));
}

public sealed record CookedMovement(
    float RunSpeed,
    float RunAccelerationA,
    float RunAccelerationB,
    float DashSpeed,
    float AirSpeedMax,
    float AirAccelStick,
    float AirAccelBase,
    float JumpForce,
    float ShortHopForce,
    float AirJumpVMultiplier,
    float AirJumpHMultiplier,
    float Gravity,
    float AirFloatGravity,
    ushort DashDurationTicks,
    ushort DashCooldownTicks,
    float GroundFriction,
    float AirFriction,
    float MaxFallSpeed,
    float FastFallSpeed,
    byte MaxJumps,
    ushort JumpSquatTicks,
    ushort FloatWindowTicks,
    ushort RushTicks);

public sealed record CookedPresentation(
    string Idle,
    string Run,
    string Dash,
    string Jump,
    string Fall,
    string HitSmall,
    string HitMedium,
    string HitHard,
    float LandStartOffsetSeconds,
    string ModelResourcePath = "",
    float VisualScale = 1f,
    float HurtboxBoneScale = 1f,
    float ModelYOffset = 0f,
    float ModelSoleOffset = 0f,
    bool AutoModelYOffset = false);

public sealed record CookedHurtboxCapsule(
    float StartX,
    float StartY,
    float StartZ,
    float EndX,
    float EndY,
    float EndZ,
    float Radius);

public sealed record CookedHurtboxBone(
    string BoneId,
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float Radius);
public sealed record CookedCapabilityRequirement(string CapabilityId, string CapabilityVersion);
public sealed class CookedSlotDefinition
{
    public int Ordinal { get; }
    public string Id { get; }
    public bool IsAir { get; }
    public string Name { get; }
    public string Description { get; }
    public string IconId { get; }
    public AuthoringAbilityBehavior Behavior { get; }
    public AuthoringAimMode AimMode { get; }
    public ushort CooldownTicks { get; }
    public bool IsRecoveryMove { get; }
    public bool PreserveMomentumOnStart { get; }
    public CookedChargePool? ChargePool { get; }
    public CookedTimeline Timeline { get; }

    public CookedSlotDefinition(
        int ordinal,
        string id,
        bool isAir,
        string name,
        string description,
        string iconId,
        AuthoringAbilityBehavior behavior,
        AuthoringAimMode aimMode,
        ushort cooldownTicks,
        bool isRecoveryMove,
        bool preserveMomentumOnStart,
        CookedTimeline timeline,
        CookedChargePool? chargePool = null)
    {
        Ordinal = ordinal;
        Id = id;
        IsAir = isAir;
        Name = name;
        Description = description;
        IconId = iconId;
        Behavior = behavior;
        AimMode = aimMode;
        CooldownTicks = cooldownTicks;
        IsRecoveryMove = isRecoveryMove;
        PreserveMomentumOnStart = preserveMomentumOnStart;
        Timeline = timeline;
        ChargePool = chargePool;
    }
}

public sealed record CookedChargePool(int MaxCharges, ushort RegenTicks);

public sealed class CookedTimeline
{
    public IReadOnlyList<CookedStage> Stages { get; }

    public CookedTimeline(IReadOnlyList<CookedStage> stages)
        => Stages = new ReadOnlyCollection<CookedStage>(new List<CookedStage>(stages));
}

public sealed class CookedStage
{
    public ushort DurationTicks { get; }
    public ushort IasaTicks { get; }
    public ushort LandingLagTicks { get; }
    public ushort AutoCancelBeforeTicks { get; }
    public ushort AutoCancelAfterTicks { get; }
    public IReadOnlyList<string> AnimationIds { get; }
    public IReadOnlyList<CookedTimelineOperation> Operations { get; }

    public CookedStage(
        ushort durationTicks,
        ushort iasaTicks,
        ushort landingLagTicks,
        ushort autoCancelBeforeTicks,
        ushort autoCancelAfterTicks,
        IReadOnlyList<string> animationIds,
        IReadOnlyList<CookedTimelineOperation> operations)
    {
        DurationTicks = durationTicks;
        IasaTicks = iasaTicks;
        LandingLagTicks = landingLagTicks;
        AutoCancelBeforeTicks = autoCancelBeforeTicks;
        AutoCancelAfterTicks = autoCancelAfterTicks;
        AnimationIds = new ReadOnlyCollection<string>(new List<string>(animationIds));
        Operations = new ReadOnlyCollection<CookedTimelineOperation>(new List<CookedTimelineOperation>(operations));
    }
}

public enum CookedOperationKind : byte
{
    SetVelocity = 0,
    SpawnHitbox = 1,
    SpawnProjectile = 2,
    SetAimState = 3,
    StartCapability = 4,
    EmitPresentation = 5,
    CompleteTimeline = 6,
}

public abstract class CookedTimelineOperation
{
    public ushort Tick { get; }
    public AuthoringUnit Unit { get; }
    public CookedOperationKind Kind { get; }

    protected CookedTimelineOperation(ushort tick, AuthoringUnit unit, CookedOperationKind kind)
    {
        Tick = tick;
        Unit = unit;
        Kind = kind;
    }
}

public sealed class CookedSetVelocityOperation : CookedTimelineOperation
{
    public AuthoringVelocityMode VelocityMode { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public CookedSetVelocityOperation(ushort tick, AuthoringUnit unit, AuthoringVelocityMode velocityMode, float x, float y, float z)
        : base(tick, unit, CookedOperationKind.SetVelocity)
    {
        VelocityMode = velocityMode;
        X = x;
        Y = y;
        Z = z;
    }
}

public sealed class CookedSpawnHitboxOperation : CookedTimelineOperation
{
    public CookedHitbox Hitbox { get; }
    public CookedSpawnHitboxOperation(ushort tick, AuthoringUnit unit, CookedHitbox hitbox)
        : base(tick, unit, CookedOperationKind.SpawnHitbox) => Hitbox = hitbox;
}

public sealed record CookedHitbox(
    AuthoringHitboxShape Shape,
    float Radius,
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float EndOffsetX,
    float EndOffsetY,
    float EndOffsetZ,
    string? StartBoneId,
    string? EndBoneId,
    float Damage,
    float Angle,
    float BaseKnockback,
    float KnockbackGrowth,
    ushort StunTicks,
    ushort DurationTicks,
    bool Interruptible,
    byte HitGroup);

public sealed class CookedSpawnProjectileOperation : CookedTimelineOperation
{
    public CookedProjectile Projectile { get; }
    public CookedSpawnProjectileOperation(ushort tick, AuthoringUnit unit, CookedProjectile projectile)
        : base(tick, unit, CookedOperationKind.SpawnProjectile) => Projectile = projectile;
}

public sealed record CookedProjectile(
    float LaunchOffsetX,
    float LaunchOffsetY,
    float LaunchOffsetZ,
    float Speed,
    float Gravity,
    float Radius,
    float Damage,
    float Angle,
    float BaseKnockback,
    float KnockbackGrowth,
    ushort StunTicks,
    ushort MaxFlightTicks,
    float YawOffsetDegrees = 0f);

public sealed class CookedSetAimStateOperation : CookedTimelineOperation
{
    public AuthoringAimMode AimState { get; }
    public CookedSetAimStateOperation(ushort tick, AuthoringUnit unit, AuthoringAimMode aimState)
        : base(tick, unit, CookedOperationKind.SetAimState) => AimState = aimState;
}

public sealed class CookedStartCapabilityOperation : CookedTimelineOperation
{
    public string CapabilityId { get; }
    public string CapabilityVersion { get; }
    public CookedCapabilityParameters Parameters { get; }

    public CookedStartCapabilityOperation(ushort tick, AuthoringUnit unit, string capabilityId, string capabilityVersion, CookedCapabilityParameters parameters)
        : base(tick, unit, CookedOperationKind.StartCapability)
    {
        CapabilityId = capabilityId;
        CapabilityVersion = capabilityVersion;
        Parameters = parameters;
    }
}

public sealed class CookedEmitPresentationOperation : CookedTimelineOperation
{
    public string PresentationId { get; }
    public int OperationIndex { get; }
    public CookedEmitPresentationOperation(ushort tick, AuthoringUnit unit, string presentationId, int operationIndex)
        : base(tick, unit, CookedOperationKind.EmitPresentation)
    {
        PresentationId = presentationId;
        OperationIndex = operationIndex;
    }
}

public sealed class CookedCompleteTimelineOperation : CookedTimelineOperation
{
    public CookedCompleteTimelineOperation(ushort tick, AuthoringUnit unit)
        : base(tick, unit, CookedOperationKind.CompleteTimeline) { }
}
public abstract record CookedCapabilityParameters;
public sealed record CookedKiShotCapabilityParameters(
    ushort StartupTicks, ushort DurationTicks, float LaunchOffsetY, float ProjectileSpeed, float Gravity,
    float HitboxRadius, float Damage, float KnockbackBase, float KnockbackGrowth, float KnockbackAngle,
    ushort StunTicks, ushort MaxFlightTicks) : CookedCapabilityParameters;
public sealed record CookedRisingDragonCapabilityParameters(float RiseSpeed, ushort RiseTicks, ushort RiseDelay) : CookedCapabilityParameters;
public sealed record CookedCycloneKickCapabilityParameters(
    float ForwardSpeed, ushort WindupTicks, ushort HitboxEndTick, ushort DurationTicks, float BodyRadius,
    float SideRadius, float SideOffset, float Damage, float KnockbackAngle, float KnockbackBase,
    float KnockbackGrowth, ushort StunTicks, float BodyY, float SideY) : CookedCapabilityParameters;
public sealed record CookedDragonBeamCapabilityParameters(
    ushort DurationTicks, ushort FireTick, float LaunchOffsetY, float BeamRange, float BeamRadius,
    float Damage, float KnockbackAngle, float KnockbackBase, float KnockbackGrowth, ushort StunTicks,
    ushort HitboxDurationTicks) : CookedCapabilityParameters;
public sealed record CookedKistuDashSlashCapabilityParameters(
    float DashDistance,
    ushort DashDurationTicks,
    ushort MaxAimTicks) : CookedCapabilityParameters;

public sealed record CookedKistuRisingSlashCapabilityParameters(
    float RiseSpeed,
    ushort RiseTicks,
    float HomingRange,
    float HomingSpeed) : CookedCapabilityParameters;

public sealed record CookedKistuBladeFlurryCapabilityParameters(
    float ForwardSpeed,
    ushort MoveTicks) : CookedCapabilityParameters;

public sealed record CookedBudget(
    int SlotCount,
    int StageCount,
    int OperationCount,
    int HitboxCount,
    int ProjectileCount,
    int CapabilityCount,
    int MaxTimelineDurationTicks)
{
    public const int MaxSlots = 16;
    public const int MaxStagesPerTimeline = 8;
    public const int MaxOperationsPerStage = 64;
    public const int MaxOperationsPerTimeline = 256;
    public const int MaxCapabilityRequirements = 32;
}
