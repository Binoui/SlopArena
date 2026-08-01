using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

/// <summary>
/// Tests for <see cref="LobbyPayloadCodec"/>: the only testable seam of the
/// SignalR lobby client (issue #33). The codec maps the master server's
/// camelCase JSON payloads (System.Text.Json default for ASP.NET Core SignalR)
/// to the Shared DTOs. These pin the wire contract the Unity LobbyClient relies on.
/// </summary>
public class LobbyPayloadCodecTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ── Player ──

    [Fact]
    public void TryParsePlayer_HostWithNullSelection_ReturnsPlayer()
    {
        var el = Parse("""{"steamId":12345,"name":"Guest-12345","characterSelection":null,"isHost":true}""");

        var p = LobbyPayloadCodec.TryParsePlayer(el);

        Assert.NotNull(p);
        Assert.Equal(12345, p!.SteamId);
        Assert.Equal("Guest-12345", p.Name);
        Assert.Null(p.CharacterSelection);
        Assert.True(p.IsHost);
    }

    [Fact]
    public void TryParsePlayer_NonHostWithSelection_ReturnsPlayer()
    {
        var el = Parse("""{"steamId":99,"name":"Guest-99","characterSelection":"Manki","isHost":false}""");

        var p = LobbyPayloadCodec.TryParsePlayer(el);

        Assert.NotNull(p);
        Assert.Equal(99, p!.SteamId);
        Assert.Equal("Manki", p.CharacterSelection);
        Assert.False(p.IsHost);
    }

    [Fact]
    public void TryParsePlayer_MissingCharacterSelection_TreatedAsNull()
    {
        var el = Parse("""{"steamId":1,"name":"Guest-1","isHost":true}""");

        var p = LobbyPayloadCodec.TryParsePlayer(el);

        Assert.NotNull(p);
        Assert.Null(p!.CharacterSelection);
        Assert.True(p.IsHost);
    }

    [Theory]
    [InlineData("""{"steamId":"x","name":"n","isHost":true}""")]            // steamId not a number
    [InlineData("""{"steamId":1,"name":5,"isHost":true}""")]                  // name not a string
    [InlineData("""{"steamId":1,"name":"n"}""")]                              // missing isHost
    [InlineData("""{"name":"n","isHost":true}""")]                            // missing steamId
    [InlineData("""[]""")]                                                     // not an object
    public void TryParsePlayer_Malformed_ReturnsNull(string json)
    {
        Assert.Null(LobbyPayloadCodec.TryParsePlayer(Parse(json)));
    }

    // ── Snapshot ──

    [Fact]
    public void TryParseSnapshot_TwoPlayers_PreservesOrder()
    {
        var json = """
        {"serverId":"33333333-3333-3333-3333-333333333333","players":[
            {"steamId":1,"name":"Host","characterSelection":null,"isHost":true},
            {"steamId":2,"name":"Guest","characterSelection":null,"isHost":false}
        ]}
        """;

        var snap = LobbyPayloadCodec.TryParseSnapshot(Parse(json));

        Assert.NotNull(snap);
        Assert.Equal(new System.Guid("33333333-3333-3333-3333-333333333333"), snap!.ServerId);
        Assert.Equal(2, snap.Players.Count);
        Assert.Equal("Host", snap.Players[0].Name);
        Assert.True(snap.Players[0].IsHost);
        Assert.Equal("Guest", snap.Players[1].Name);
        Assert.False(snap.Players[1].IsHost);
    }

    [Fact]
    public void TryParseSnapshot_EmptyPlayers_IsValid()
    {
        var json = """{"serverId":"33333333-3333-3333-3333-333333333333","players":[]}""";

        var snap = LobbyPayloadCodec.TryParseSnapshot(Parse(json));

        Assert.NotNull(snap);
        Assert.Empty(snap!.Players);
    }

    [Fact]
    public void TryParseSnapshot_BadGuid_ReturnsNull()
    {
        var json = """{"serverId":"not-a-guid","players":[]}""";

        Assert.Null(LobbyPayloadCodec.TryParseSnapshot(Parse(json)));
    }

    [Fact]
    public void TryParseSnapshot_MalformedPlayer_ReturnsNull()
    {
        var json = """{"serverId":"33333333-3333-3333-3333-333333333333","players":[{"steamId":1}]}""";

        Assert.Null(LobbyPayloadCodec.TryParseSnapshot(Parse(json)));
    }

    // ── MatchStarting (same shape as snapshot) ──

    [Fact]
    public void TryParseMatchStarting_TwoPlayers_ReturnsConfig()
    {
        var json = """
        {"serverId":"11111111-1111-1111-1111-111111111111","players":[
            {"steamId":7,"name":"A","characterSelection":null,"isHost":true},
            {"steamId":8,"name":"B","characterSelection":"FightGuy","isHost":false}
        ]}
        """;

        var cfg = LobbyPayloadCodec.TryParseMatchStarting(Parse(json));

        Assert.NotNull(cfg);
        Assert.Equal(new System.Guid("11111111-1111-1111-1111-111111111111"), cfg!.ServerId);
        Assert.Equal(2, cfg.Players.Count);
        Assert.Equal("FightGuy", cfg.Players[1].CharacterSelection);
    }

    [Fact]
    public void TryParseMatchStarting_Malformed_ReturnsNull()
    {
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarting(Parse("""{"players":[]}""")));
    }
}
