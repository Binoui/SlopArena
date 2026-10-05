using System;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class NetplayClockTests
{
    [Fact]
    public void ClockUsesAuthorityElapsedTimeAndBoundedLeadCatchup()
    {
        var clock = new NetplayClock();
        Assert.True(clock.Observe(120, 420, 10, 100));
        Assert.Equal(5u, clock.LeadTicks);
        Assert.Equal(123u, clock.EstimateTick(10));
        Assert.Equal(183u, clock.EstimateTick(11));
        Assert.Equal(188u, clock.DesiredLocalTick(11));
        Assert.Equal(4u, clock.CatchUpSteps(120, 11));
    }
    [Fact]
    public void ClockEstimateDoesNotRegressWhenLatencyEstimateShrinks()
    {
        var clock = new NetplayClock();
        Assert.True(clock.Observe(100, 301, 1, 200));
        Assert.True(clock.Observe(106, 301, 1.1, 20));
        Assert.Equal(112u, clock.EstimateTick(1.1));
        Assert.Equal(3u, clock.LeadTicks);
    }


    [Fact]
    public void ClockRejectsTimeOrTickRegressionAndBoundsLead()
    {
        var clock = new NetplayClock();
        Assert.True(clock.Observe(10, 310, 2, 20_000));
        Assert.Equal(12u, clock.LeadTicks);
        Assert.False(clock.Observe(9, 310, 3, 0));
        Assert.False(clock.Observe(11, 310, 1, 0));
        Assert.False(clock.Observe(11, 0, 3, 0));
        Assert.Equal(610u, clock.EstimateTick(1));
    }

    [Fact]
    public void ControlCodecRequiresExactVersionKindAndEntity()
    {
        var expected = new NetplayControlPacket(NetplayControlKind.Clock, 42, 500, 800);
        var bytes = new byte[NetplayControlPacket.Size];
        expected.Serialize(bytes);
        Assert.True(NetplayControlPacket.TryDeserialize(bytes, out var actual));
        Assert.Equal(expected, actual);
        Assert.False(NetplayControlPacket.TryDeserialize(bytes.AsSpan(0, bytes.Length - 1), out _));
        bytes[1]++;
        Assert.False(NetplayControlPacket.TryDeserialize(bytes, out _));
    }
}
