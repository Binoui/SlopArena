using Xunit;

namespace SlopArena.Shared.Tests;

public class TickInputBufferTests
{
    private static InputState In(byte slot) => new InputState { ActiveSlot = slot };

    [Fact]
    public void Push_KeepsEntriesSortedByTick()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(5, In(5));
        buf.Push(3, In(3));
        buf.Push(4, In(4));
        Assert.Equal(3, buf.Count);
        Assert.True(buf.TryTake(3, out var a));
        Assert.True(buf.TryTake(4, out var b));
        Assert.True(buf.TryTake(5, out var c));
        Assert.Equal((byte)3, a.ActiveSlot);
        Assert.Equal((byte)4, b.ActiveSlot);
        Assert.Equal((byte)5, c.ActiveSlot);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void Push_SameTick_ReplacesExisting()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(4, In(1));
        buf.Push(4, In(2));
        Assert.Equal(1, buf.Count);
        Assert.True(buf.TryTake(4, out var input));
        Assert.Equal((byte)2, input.ActiveSlot);
    }

    [Fact]
    public void TryTake_MissingTick_ReturnsFalse_LeavesBufferIntact()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(4, In(1));
        Assert.False(buf.TryTake(5, out _));
        Assert.Equal(1, buf.Count);
    }

    [Fact]
    public void Prune_RemovesEntriesAtOrBelow()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(3, In(3));
        buf.Push(4, In(4));
        buf.Push(5, In(5));
        buf.Prune(4);
        Assert.Equal(1, buf.Count);
        Assert.True(buf.TryTake(5, out _));
    }

    [Fact]
    public void MaxTick_NullWhenEmpty()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        Assert.Null(buf.MaxTick);
        buf.Push(7, In(7));
        Assert.Equal((uint)7, buf.MaxTick);
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(1, In(1));
        buf.Clear();
        Assert.Equal(0, buf.Count);
        Assert.Null(buf.MaxTick);
    }

    [Fact]
    public void Burst_IntermediateTicksSurvive_NoNewestOnlyDrop()
    {
        // Regression for the old FlushQueue "newest only" bug: a 5-tick burst arriving
        // before the next sim tick must keep every intermediate input consumable —
        // a single-tick jump or slot press must never be silently discarded.
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        for (uint t = 1; t <= 5; t++) buf.Push(t, In((byte)t));
        for (uint t = 1; t <= 5; t++)
        {
            Assert.True(buf.TryTake(t, out var input), $"tick {t} was dropped");
            Assert.Equal((byte)t, input.ActiveSlot);
        }
        Assert.Equal(0, buf.Count);
    }
}
