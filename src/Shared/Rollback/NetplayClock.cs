namespace SlopArena.Shared.Rollback;

/// <summary>Estimates the authoritative 60 Hz timeline and bounds local catch-up.</summary>
public sealed class NetplayClock
{
    public const uint HistoryTicks = 30;
    public const uint MaximumLeadTicks = 12;
    public const uint MaximumCatchUpSteps = 4;
    private uint _authorityTick;
    private uint _lastAuthorityTick;
    private double _observedAt;
    private uint _startTick;
    private bool _seeded;

    public uint StartTick => _startTick;
    public uint LeadTicks { get; private set; } = 2;
    public bool IsSeeded => _seeded;

    /// <summary>Estimate server time at receipt; RTT compensation is split between clock and input lead.</summary>
    public bool Observe(uint authorityTick, uint startTick, double receivedAtSeconds, double roundTripMilliseconds)
    {
        if (!double.IsFinite(receivedAtSeconds) || receivedAtSeconds < 0 ||
            !double.IsFinite(roundTripMilliseconds) || roundTripMilliseconds < 0 ||
            (_seeded && (receivedAtSeconds < _observedAt || authorityTick < _lastAuthorityTick ||
                (_startTick != 0 && (startTick == 0 || startTick < _startTick)))))
            return false;

        double oneWayTicksValue = System.Math.Ceiling(roundTripMilliseconds * 0.03);
        uint oneWayTicks = oneWayTicksValue >= uint.MaxValue ? uint.MaxValue : (uint)oneWayTicksValue;
        uint estimateAtReceipt = oneWayTicks > uint.MaxValue - authorityTick
            ? uint.MaxValue
            : authorityTick + oneWayTicks;
        if (_seeded)
        {
            uint previousEstimate = EstimateTick(receivedAtSeconds);
            if (estimateAtReceipt < previousEstimate)
                estimateAtReceipt = previousEstimate;
        }
        _authorityTick = estimateAtReceipt;
        _lastAuthorityTick = authorityTick;
        _startTick = startTick;
        _observedAt = receivedAtSeconds;
        LeadTicks = oneWayTicks >= MaximumLeadTicks - 2 ? MaximumLeadTicks : oneWayTicks + 2;
        _seeded = true;
        return true;
    }

    /// <summary>Estimated authority tick, including elapsed time since the latest observation.</summary>
    public uint EstimateTick(double nowSeconds)
    {
        if (!_seeded || !double.IsFinite(nowSeconds) || nowSeconds < _observedAt)
            return _authorityTick;
        double elapsedTicks = (nowSeconds - _observedAt) * 60d;
        if (elapsedTicks >= uint.MaxValue - _authorityTick) return uint.MaxValue;
        return _authorityTick + (uint)System.Math.Floor(elapsedTicks);
    }

    /// <summary>Desired prediction frontier; callers advance at most four steps per update.</summary>
    public uint DesiredLocalTick(double nowSeconds)
    {
        uint estimate = EstimateTick(nowSeconds);
        return estimate > uint.MaxValue - LeadTicks ? uint.MaxValue : estimate + LeadTicks;
    }

    public uint CatchUpSteps(uint localTick, double nowSeconds)
    {
        uint desired = DesiredLocalTick(nowSeconds);
        if (desired <= localTick) return 0;
        return System.Math.Min(desired - localTick, MaximumCatchUpSteps);
    }
}
