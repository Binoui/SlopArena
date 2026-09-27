namespace SlopArena.Shared;

public enum PresentationEventSource : byte
{
    Timeline = 0,
    CapabilityExplosion = 1,
    BlockContact = 2,
}

public readonly record struct PresentationEventKey(
    uint MatchTick,
    ulong EntityId,
    byte AttackSequence,
    PresentationEventSource Source,
    int OperationIndex);

public readonly record struct TimelinePresentationEvent(
    uint MatchTick,
    ulong EntityId,
    int OperationIndex,
    string PresentationId,
    byte AttackSequence,
    PresentationEventSource Source,
    float WorldX,
    float WorldY,
    float WorldZ,
    float WorldYaw)
{
    public PresentationPlacement Placement { get; init; } = new PresentationPlacement();

    public PresentationEventKey Key =>
        new(MatchTick, EntityId, AttackSequence, Source, OperationIndex);
}
