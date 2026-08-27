namespace SlopArena.Shared;

public readonly record struct PresentationEventKey(
    uint MatchTick,
    ulong EntityId,
    int OperationIndex);

public readonly record struct TimelinePresentationEvent(
    uint MatchTick,
    ulong EntityId,
    int OperationIndex,
    string PresentationId)
{
    public PresentationEventKey Key => new(MatchTick, EntityId, OperationIndex);
}
