using System.Collections.Generic;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

public class MatchRulesTests
{
    private const byte MaxDeaths = 3;

    private static Dictionary<ulong, CharacterState> States(params (ulong id, byte deaths)[] players)
    {
        var d = new Dictionary<ulong, CharacterState>();
        foreach (var (id, deaths) in players)
            d[id] = new CharacterState { EntityId = id, Deaths = deaths };
        return d;
    }

    [Fact]
    public void FindWinner_WhileTwoOrMoreAlive_ReturnsNull()
    {
        var states = States((1, 0), (2, 2), (3, 1));
        Assert.Null(MatchRules.FindWinner(states, MaxDeaths));
    }

    [Fact]
    public void FindWinner_OneSurvivor_ReturnsSurvivor()
    {
        var states = States((1, 3), (2, 1), (3, 3));
        Assert.Equal(2ul, MatchRules.FindWinner(states, MaxDeaths));
    }

    [Fact]
    public void FindWinner_AllEliminated_PicksFewestDeaths()
    {
        var states = States((1, 3), (2, 3), (3, 2));
        Assert.Equal(3ul, MatchRules.FindWinner(states, MaxDeaths));
    }

    [Fact]
    public void FindWinner_AllEliminated_TieBreaksByLowestEntityId()
    {
        var states = States((1, 3), (2, 3), (4, 3));
        Assert.Equal(1ul, MatchRules.FindWinner(states, MaxDeaths));
    }

    [Fact]
    public void IsEliminated_AtThreshold_IsTrue()
    {
        Assert.True(MatchRules.IsEliminated(new CharacterState { Deaths = MaxDeaths }, MaxDeaths));
        Assert.False(MatchRules.IsEliminated(new CharacterState { Deaths = (byte)(MaxDeaths - 1) }, MaxDeaths));
    }
}
