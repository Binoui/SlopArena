using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

/// <summary>
/// Tests for <see cref="HostedServerConfig"/>: the pure builder that emits the
/// <c>server.json</c> the embedded host-and-play flow (ADR-0005, issue #39)
/// feeds to <c>SlopArena.Server</c>. Pins the contract the server's
/// <c>ServerConfig.Load</c> deserializes.
/// </summary>
public class HostedServerConfigTests
{
    // ── JSON shape the server's ServerConfig.Load expects ──

    [Fact]
    public void ToJson_EmitsAllRequiredKeys_InCamelCase()
    {
        var cfg = new HostedServerConfig
        {
            ServerName = "Binoui's Server",
            Region = "EU",
            Port = 7777,
            MaxConcurrentMatches = 1,
            MasterServerUrl = "http://localhost:5000",
            ArenaDataDir = "/abs/data/arenas"
        };

        var doc = JsonDocument.Parse(cfg.ToJson()).RootElement;

        Assert.Equal("Binoui's Server", doc.GetProperty("serverName").GetString());
        Assert.Equal("EU", doc.GetProperty("region").GetString());
        Assert.Equal(7777, doc.GetProperty("port").GetInt32());
        Assert.Equal(1, doc.GetProperty("maxConcurrentMatches").GetInt32());
        Assert.Equal("http://localhost:5000", doc.GetProperty("masterServerUrl").GetString());
        Assert.False(doc.GetProperty("isOfficial").GetBoolean());
        Assert.Equal("/abs/data/arenas", doc.GetProperty("arenaDataDir").GetString());
    }

    [Fact]
    public void ToJson_Defaults_AreDemoSafe()
    {
        var cfg = new HostedServerConfig { ArenaDataDir = "/x" };

        var doc = JsonDocument.Parse(cfg.ToJson()).RootElement;

        // A demo host: one match slot, not official, sensible port.
        Assert.Equal(1, doc.GetProperty("maxConcurrentMatches").GetInt32());
        Assert.False(doc.GetProperty("isOfficial").GetBoolean());
        Assert.Equal(9876, doc.GetProperty("port").GetInt32());
    }

    [Fact]
    public void ToJson_RoundTrips_ThroughServerConfigShape()
    {
        // The server deserializes with PropertyNameCaseInsensitive = true, so
        // our camelCase keys must populate its PascalCase properties. We can't
        // reference SlopArena.Server here, so mirror its shape and prove the
        // round-trip via a case-insensitive deserialize.
        var cfg = new HostedServerConfig
        {
            ServerName = "H",
            Region = "US",
            Port = 8000,
            MaxConcurrentMatches = 2,
            MasterServerUrl = "http://m:5000",
            IsOfficial = true,
            ArenaDataDir = "/arenas"
        };

        var server = JsonSerializer.Deserialize<ServerConfigMirror>(
            cfg.ToJson(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(server);
        Assert.Equal("H", server!.ServerName);
        Assert.Equal("US", server.Region);
        Assert.Equal(8000, server.Port);
        Assert.Equal(2, server.MaxConcurrentMatches);
        Assert.Equal("http://m:5000", server.MasterServerUrl);
        Assert.True(server.IsOfficial);
        Assert.Equal("/arenas", server.ArenaDataDir);
    }

    // Mirrors SlopArena.Server.ServerConfig's property names/types so the test
    // can prove HostedServerConfig's JSON deserializes into the server's config
    // without referencing the server project.
    private sealed class ServerConfigMirror
    {
        public string ServerName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public int Port { get; set; }
        public int MaxConcurrentMatches { get; set; }
        public string MasterServerUrl { get; set; } = string.Empty;
        public bool IsOfficial { get; set; }
        public string ArenaDataDir { get; set; } = string.Empty;
    }
}
