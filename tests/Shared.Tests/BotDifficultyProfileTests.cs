using Xunit;
using SlopArena.Shared.AI;

namespace SlopArena.Shared.Tests;

public sealed class BotDifficultyProfileTests
{
    [Fact]
    public void NamedDifficultiesHaveExactReactionDelays()
    {
        Assert.Equal(24, BotDifficultyProfile.ForDifficulty(CpuDifficulty.Easy).ReactionDelayTicks);
        Assert.Equal(18, BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).ReactionDelayTicks);
        Assert.Equal(12, BotDifficultyProfile.ForDifficulty(CpuDifficulty.Hard).ReactionDelayTicks);
    }

    [Fact]
    public void NamedDifficultiesUseOnlyCanonicalSerializedValues()
    {
        Assert.Equal(0, (int)CpuDifficulty.Easy);
        Assert.Equal(1, (int)CpuDifficulty.Normal);
        Assert.Equal(2, (int)CpuDifficulty.Hard);
        Assert.Equal(CpuDifficulty.Normal, BotDifficultyProfile.Normalize((CpuDifficulty)(-1)));
        Assert.Equal(CpuDifficulty.Normal, BotDifficultyProfile.Normalize((CpuDifficulty)3));
    }
    [Fact]
    public void ProfilesBecomeFasterMoreAccurateAndMoreAggressive()
    {
        var easy = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Easy);
        var normal = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal);
        var hard = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Hard);

        Assert.True(easy.DecisionIntervalTicks > normal.DecisionIntervalTicks);
        Assert.True(normal.DecisionIntervalTicks > hard.DecisionIntervalTicks);
        Assert.True(easy.RangeError > normal.RangeError);
        Assert.True(normal.RangeError > hard.RangeError);
        Assert.True(easy.AttackChance < normal.AttackChance);
        Assert.True(normal.AttackChance < hard.AttackChance);
        Assert.True(easy.PunishChance < normal.PunishChance);
        Assert.True(normal.PunishChance < hard.PunishChance);
        Assert.True(easy.ComboChance < normal.ComboChance);
        Assert.True(normal.ComboChance < hard.ComboChance);
    }

    [Fact]
    public void HardRetainsNonZeroRangeError()
    {
        var hard = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Hard);

        Assert.InRange(hard.RangeError, float.Epsilon, 1f);
        Assert.True(hard.PunishChance > 0f);
        Assert.True(hard.ComboChance > 0f);
    }
}
