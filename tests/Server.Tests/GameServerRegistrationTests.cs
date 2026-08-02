using System.Net;
using System.Text;
using System.Text.Json;
using SlopArena.Server;
using Xunit;

namespace SlopArena.Server.Tests;

/// <summary>
/// GameServerRegistration shutdown behavior (issue #49): cancelling the
/// heartbeat loop must deregister the server with the master server
/// (DELETE /servers/{id}, bearer apiToken), and a failed deregistration must
/// never crash shutdown — the heartbeat TTL remains the fallback.
/// </summary>
public class GameServerRegistrationTests
{
    private static ServerConfig TestConfig() => new()
    {
        ServerName = "Test Server",
        Region = "EU",
        Port = 9876,
        MaxConcurrentMatches = 15,
        MasterServerUrl = "http://master.test"
    };

    /// <summary>Records every request; routes register to a 200 with a token.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
        {
            _respond = respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK));
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage RegisterOkResponse(Guid serverId) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { serverId, apiToken = "tok-1" }),
            Encoding.UTF8,
            "application/json")
    };

    private static StubHandler RegisterThenOkHandler(Guid serverId) => new(req =>
        req.RequestUri!.AbsolutePath == "/servers/register"
            ? RegisterOkResponse(serverId)
            : new HttpResponseMessage(HttpStatusCode.OK));

    [Fact]
    public async Task CancellingHeartbeatLoop_Deregisters_WithServerIdAndToken()
    {
        var serverId = Guid.NewGuid();
        var handler = RegisterThenOkHandler(serverId);
        var registration = new GameServerRegistration(TestConfig(), new MultiMatchOrchestrator(TestConfig()), handler);

        Assert.True(await registration.RegisterAsync());

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await registration.RunHeartbeatLoopAsync(cts.Token);

        var deregister = Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Delete));
        Assert.Equal($"/servers/{serverId}", deregister.RequestUri!.AbsolutePath);
        Assert.Equal("tok-1", deregister.Headers.Authorization!.Parameter);
        Assert.False(registration.IsRegistered);
    }

    [Fact]
    public async Task DeregisterRejectedByMaster_DoesNotThrow()
    {
        var serverId = Guid.NewGuid();
        var handler = new StubHandler(req =>
            req.Method == HttpMethod.Delete
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : RegisterOkResponse(serverId));
        var registration = new GameServerRegistration(TestConfig(), new MultiMatchOrchestrator(TestConfig()), handler);
        Assert.True(await registration.RegisterAsync());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await registration.RunHeartbeatLoopAsync(cts.Token); // must not throw
    }

    [Fact]
    public async Task DeregisterMasterUnreachable_DoesNotThrow()
    {
        var serverId = Guid.NewGuid();
        var handler = new StubHandler(req =>
            req.Method == HttpMethod.Delete
                ? throw new HttpRequestException("connection refused")
                : RegisterOkResponse(serverId));
        var registration = new GameServerRegistration(TestConfig(), new MultiMatchOrchestrator(TestConfig()), handler);
        Assert.True(await registration.RegisterAsync());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await registration.RunHeartbeatLoopAsync(cts.Token); // must not throw
    }

    [Fact]
    public async Task NotRegistered_NoDeregisterCall()
    {
        var handler = new StubHandler();
        var registration = new GameServerRegistration(TestConfig(), new MultiMatchOrchestrator(TestConfig()), handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await registration.RunHeartbeatLoopAsync(cts.Token);

        Assert.Empty(handler.Requests);
    }
}
