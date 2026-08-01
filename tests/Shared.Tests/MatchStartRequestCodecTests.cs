using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

/// <summary>
/// Tests for <see cref="MatchStartRequestCodec"/>: the master server → game
/// server match-start body (ADR-0008, issue #35). This is the seam that proves
/// the character classes selected in the lobby reach the game server as the
/// correct <see cref="CharacterClass"/> values + dynamic entity IDs — the
/// ticket's core requirement ("not hardcoded Manki").
/// </summary>
public class MatchStartRequestCodecTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    private const string TwoPlayerBody = """
    {"matchId":"abc-123","arenaName":"split","players":[
        {"steamId":1,"characterClass":"Manki","entityId":1},
        {"steamId":2,"characterClass":"FightGuy","entityId":2}
    ]}
    """;

    [Fact]
    public void TryParse_TwoPlayers_DifferentClasses_PreservesEach()
    {
        var req = MatchStartRequestCodec.TryParse(Parse(TwoPlayerBody));

        Assert.NotNull(req);
        Assert.Equal("abc-123", req!.MatchId);
        Assert.Equal("split", req.ArenaName);
        Assert.Equal(2, req.Players.Count);
        Assert.Equal(1, req.Players[0].SteamId);
        Assert.Equal(CharacterClass.Manki, req.Players[0].CharacterClass);
        Assert.Equal(1, req.Players[0].EntityId);
        Assert.Equal(2, req.Players[1].SteamId);
        Assert.Equal(CharacterClass.FightGuy, req.Players[1].CharacterClass);
        Assert.Equal(2, req.Players[1].EntityId);
    }

    [Fact]
    public void TryParse_DynamicEntityIds_PreservesOrder()
    {
        // Join order maps to entity IDs 1..N — the host is not always entity 1
        // in the broadcast sense, but the master assigns 1..N by roster order.
        var json = """
        {"matchId":"m","arenaName":"split","players":[
            {"steamId":42,"characterClass":"Kistu","entityId":1},
            {"steamId":7,"characterClass":"Nilus","entityId":2},
            {"steamId":9,"characterClass":"Manki","entityId":3}
        ]}
        """;

        var req = MatchStartRequestCodec.TryParse(Parse(json));

        Assert.NotNull(req);
        Assert.Equal(3, req!.Players.Count);
        Assert.Equal(1, req.Players[0].EntityId);
        Assert.Equal(2, req.Players[1].EntityId);
        Assert.Equal(3, req.Players[2].EntityId);
        Assert.Equal(CharacterClass.Kistu, req.Players[0].CharacterClass);
        Assert.Equal(CharacterClass.Nilus, req.Players[1].CharacterClass);
        Assert.Equal(CharacterClass.Manki, req.Players[2].CharacterClass);
    }

    [Theory]
    [InlineData("""{"arenaName":"split","players":[{"steamId":1,"characterClass":"Manki","entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""")] // missing matchId
    [InlineData("""{"matchId":"","arenaName":"split","players":[{"steamId":1,"characterClass":"Manki","entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""")] // empty matchId
    [InlineData("""{"matchId":"m","arenaName":"split"}""")] // missing players
    [InlineData("""{"matchId":"m","arenaName":"split","players":[]}""")] // empty players (< 2)
    [InlineData("""{"matchId":"m","arenaName":"split","players":[{"steamId":1,"characterClass":"Manki","entityId":1}]}""")] // single player (< 2)
    public void TryParse_Malformed_ReturnsNull(string json)
    {
        Assert.Null(MatchStartRequestCodec.TryParse(Parse(json)));
    }

    [Theory]
    [InlineData("""{"matchId":"m","players":[{"steamId":1,"characterClass":"Manki","entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""")] // missing arenaName -> empty
    [InlineData("""{"matchId":"m","arenaName":"","players":[{"steamId":1,"characterClass":"Manki","entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""")] // empty arenaName -> empty
    public void TryParse_OmittedOrEmptyArena_Accepted_AsEmpty(string json)
    {
        // The game server applies its own default arena when the body omits one
        // (issue #35 review). The codec must NOT reject — it yields an empty
        // ArenaName so MatchControlServer can substitute its default.
        var req = MatchStartRequestCodec.TryParse(Parse(json));

        Assert.NotNull(req);
        Assert.Equal(string.Empty, req!.ArenaName);
    }

    [Fact]
    public void TryParse_ArenaNameNotString_ReturnsNull()
    {
        var json = """{"matchId":"m","arenaName":5,"players":[{"steamId":1,"characterClass":"Manki","entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""";

        Assert.Null(MatchStartRequestCodec.TryParse(Parse(json)));
    }

    [Fact]
    public void TryParse_UnknownCharacterClass_ReturnsNull()
    {
        // A typo must NOT silently fall back to Manki — that is the bug this fixes.
        var json = """
        {"matchId":"m","arenaName":"split","players":[
            {"steamId":1,"characterClass":"Manky","entityId":1},
            {"steamId":2,"characterClass":"FightGuy","entityId":2}
        ]}
        """;

        Assert.Null(MatchStartRequestCodec.TryParse(Parse(json)));
    }

    [Fact]
    public void TryParse_CharacterClassNone_ReturnsNull()
    {
        var json = """
        {"matchId":"m","arenaName":"split","players":[
            {"steamId":1,"characterClass":"None","entityId":1},
            {"steamId":2,"characterClass":"FightGuy","entityId":2}
        ]}
        """;

        Assert.Null(MatchStartRequestCodec.TryParse(Parse(json)));
    }

    [Theory]
    [InlineData("manki")]      // case-insensitive
    [InlineData("FIGHTGUY")]
    [InlineData("Kistu")]
    public void TryParse_CharacterClass_CaseInsensitive(string className)
    {
        var json = $$"""
        {"matchId":"m","arenaName":"split","players":[
            {"steamId":1,"characterClass":"{{className}}","entityId":1},
            {"steamId":2,"characterClass":"Manki","entityId":2}
        ]}
        """;

        var req = MatchStartRequestCodec.TryParse(Parse(json));

        Assert.NotNull(req);
        Assert.NotEqual(CharacterClass.None, req!.Players[0].CharacterClass);
    }

    [Theory]
    [InlineData("""{"matchId":"m","arenaName":"split","players":[{"steamId":1,"characterClass":"Manki","entityId":0},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""")] // entityId 0
    [InlineData("""{"matchId":"m","arenaName":"split","players":[{"steamId":1,"characterClass":"Manki","entityId":-1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""")] // entityId negative
    [InlineData("""{"matchId":"m","arenaName":"split","players":[{"steamId":"x","characterClass":"Manki","entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""")] // steamId not a number
    [InlineData("""{"matchId":"m","arenaName":"split","players":[{"steamId":1,"entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""")] // missing characterClass
    public void TryParse_BadPlayer_ReturnsNull(string json)
    {
        Assert.Null(MatchStartRequestCodec.TryParse(Parse(json)));
    }
}
