using Xunit;
using SlopArena.Shared.AI;

namespace SlopArena.Shared.Tests;

public sealed class BotDifficultyProfileTests
{
    [Fact]
    public void ForLevel_ClampsToSupportedRange()
    {
        Assert.Equal(BotDifficultyProfile.ForLevel(1).DecisionIntervalTicks,
            BotDifficultyProfile.ForLevel(0).DecisionIntervalTicks);
        Assert.Equal(BotDifficultyProfile.ForLevel(9).DecisionIntervalTicks,
            BotDifficultyProfile.ForLevel(10).DecisionIntervalTicks);
    }

    [Fact]
    public void Profiles_BecomeFasterMoreAccurateAndMoreAggressive()
    {
        var low = BotDifficultyProfile.ForLevel(1);
        var mid = BotDifficultyProfile.ForLevel(5);
        var high = BotDifficultyProfile.ForLevel(9);

        Assert.True(low.DecisionIntervalTicks > mid.DecisionIntervalTicks);
        Assert.True(mid.DecisionIntervalTicks > high.DecisionIntervalTicks);
        Assert.True(low.ReactionDelayTicks > mid.ReactionDelayTicks);
        Assert.True(mid.ReactionDelayTicks > high.ReactionDelayTicks);
        Assert.True(low.RangeError > mid.RangeError);
        Assert.True(mid.RangeError > high.RangeError);
        Assert.True(low.AttackChance < mid.AttackChance);
        Assert.True(mid.AttackChance < high.AttackChance);
        Assert.True(low.PunishChance < mid.PunishChance);
        Assert.True(mid.PunishChance < high.PunishChance);
        Assert.True(low.ComboChance < mid.ComboChance);
        Assert.True(mid.ComboChance < high.ComboChance);
    }

    [Fact]
    public void LevelNine_RetainsNonZeroRangeError()
    {
        var high = BotDifficultyProfile.ForLevel(9);

        Assert.InRange(high.RangeError, float.Epsilon, 1f);
        Assert.True(high.PunishChance > 0f);
        Assert.True(high.ComboChance > 0f);
    }
}
