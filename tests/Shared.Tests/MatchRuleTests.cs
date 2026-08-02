using System.Collections.Generic;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

public class MatchRuleTests
{
    private const byte MaxStocks = 3;

    private static Dictionary<ulong, CharacterState> States(params (ulong id, byte deaths)[] players)
    {
        var d = new Dictionary<ulong, CharacterState>();
        foreach (var (id, deaths) in players)
            d[id] = new CharacterState { EntityId = id, Deaths = deaths };
        return d;
    }

    private static StockMatchRule Stock() => new(MaxStocks);

    [Fact]
    public void Stock_WhileTwoOrMoreAlive_NotEnded()
    {
        var outcome = Stock().Evaluate(States((1, 0), (2, 2), (3, 1)));
        Assert.False(outcome.IsEnded);
        Assert.False(outcome.IsSharedVictory);
    }

    [Fact]
    public void Stock_OneSurvivor_ReturnsSurvivor()
    {
        var outcome = Stock().Evaluate(States((1, 3), (2, 1), (3, 3)));
        Assert.True(outcome.IsEnded);
        Assert.Equal(2ul, outcome.WinnerEntityId);
        Assert.False(outcome.IsSharedVictory);
    }

    [Fact]
    public void Stock_AllEliminated_DifferentDeaths_MostStocksWins()
    {
        var outcome = Stock().Evaluate(States((1, 3), (2, 3), (3, 2)));
        Assert.True(outcome.IsEnded);
        Assert.Equal(3ul, outcome.WinnerEntityId); // fewest deaths = most stocks
        Assert.False(outcome.IsSharedVictory);
    }

    [Fact]
    public void Stock_AllEliminated_EqualDeaths_SharedVictory()
    {
        // Simultaneous last-stock trade, still tied → shared victory (issue #37).
        var outcome = Stock().Evaluate(States((1, 3), (2, 3), (4, 3)));
        Assert.True(outcome.IsEnded);
        Assert.True(outcome.IsSharedVictory);
        Assert.Equal(0ul, outcome.WinnerEntityId);
    }

    [Fact]
    public void Stock_IsEliminated_AtThreshold_IsTrue()
    {
        var rule = Stock();
        Assert.True(rule.IsEliminated(new CharacterState { Deaths = MaxStocks }));
        Assert.False(rule.IsEliminated(new CharacterState { Deaths = (byte)(MaxStocks - 1) }));
    }

    [Fact]
    public void NoWin_NeverEliminated_AndNeverEnds()
    {
        // Training mode: no elimination, no win condition — Esc exit only.
        var rule = NoWinMatchRule.Instance;
        Assert.False(rule.IsEliminated(new CharacterState { Deaths = 250 }));
        var outcome = rule.Evaluate(States((1, 0), (2, 250), (3, 250)));
        Assert.False(outcome.IsEnded);
        Assert.False(outcome.IsSharedVictory);
    }
}
