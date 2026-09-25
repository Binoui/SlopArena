using System.Net;
using System.Text;
using System.Text.Json;
using SlopArena.Server;
using Xunit;

namespace SlopArena.Server.Tests;

public class GameServerRegistrationTests
{
    private static ServerConfig TestConfig(bool vps = false) => new()
    {
        DeploymentProfile = vps ? "vps" : "development",
        HostId = vps ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null,
        RegistrationKey = vps ? "registration-secret-0123456789abcdef" : null,
        MatchControlKey = vps ? "control-secret-0123456789abcdef01234567" : null,
        PublicIp = "127.0.0.1",
        Port = 9876,
        MaxConcurrentMatches = 5,
        MasterServerUrl = "http://master.test"
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Path, HttpMethod Method, string? Token)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Method, request.Headers.Authorization?.Parameter));
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Registered(Guid id, string token = "api-token") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { serverId = id, apiToken = token }), Encoding.UTF8, "application/json")
    };

    private sealed class TestClock : TimeProvider
    {
        private long _timestamp = 1;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
    }

    [Theory]
    [InlineData("server-error")]
    [InlineData("connection-failure")]
    [InlineData("timeout")]
    public async Task ReadinessExpiresAfterHeartbeatOutageAndRecoversWithoutLosingSession(string failure)
    {
        using var reservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var config = TestConfig(vps: true);
        config.Port = port;
        var id = Guid.NewGuid();
        var clock = new TestClock();
        var failedHeartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reportedToken = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = 0;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/servers/register") return Registered(id);
            if (request.RequestUri.AbsolutePath.EndsWith("/heartbeat", StringComparison.Ordinal))
            {
                if (Volatile.Read(ref restored) == 0)
                {
                    failedHeartbeat.TrySetResult();
                    return failure switch
                    {
                        "connection-failure" => throw new HttpRequestException("unavailable"),
                        "timeout" => throw new TaskCanceledException("timed out"),
                        _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (request.RequestUri.AbsolutePath == "/match/result")
                reportedToken.TrySetResult(request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var orchestrator = new MultiMatchOrchestrator(config);
        var registration = new GameServerRegistration(config, orchestrator, handler, clock);
        using var control = new MatchControlServer(orchestrator, port, "slop_court",
            isReady: () => registration.HasFreshRegistration);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        control.Start();
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        try
        {
            Assert.True(SpinWait.SpinUntil(() => registration.HasFreshRegistration, 3000));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/ready")).StatusCode);
            await failedHeartbeat.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/ready")).StatusCode);

            clock.Advance(TimeSpan.FromSeconds(16));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/ready")).StatusCode);
            await registration.ReportMatchResultAsync(Guid.NewGuid(), 1);
            Assert.Equal("api-token", await reportedToken.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(registration.IsRegistered);
            Assert.Equal(id, registration.ServerId);

            Volatile.Write(ref restored, 1);
            Assert.True(SpinWait.SpinUntil(() => registration.HasFreshRegistration, 3000));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/ready")).StatusCode);
        }
        finally
        {
            cts.Cancel();
            await run;
            control.Stop();
            orchestrator.Shutdown();
        }
    }

    [Fact]
    public async Task MasterUnavailableAtStartup_EventuallyRegistersAndDeregisters()
    {
        var id = Guid.NewGuid();
        var registrations = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/servers/register")
            {
                if (++registrations == 1) throw new HttpRequestException("unavailable");
                ready.TrySetResult();
                return Registered(id);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var config = TestConfig(vps: true);
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, initialRetryDelay: TimeSpan.FromMilliseconds(10));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(SpinWait.SpinUntil(() => registration.IsRegistered, 3000));
        cts.Cancel();
        await run;

        Assert.Equal(2, registrations);
        Assert.Equal(config.RegistrationKey, handler.Requests[0].Token);
        Assert.Equal($"/servers/{id}", Assert.Single(handler.Requests.Where(x => x.Method == HttpMethod.Delete)).Path);
        Assert.False(registration.IsRegistered);
    }

    [Fact]
    public async Task MasterLosesRegistration_HeartbeatReRegistersWithoutRestart()
    {
        var id = Guid.NewGuid();
        var registrations = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/servers/register")
            {
                if (++registrations == 2) ready.TrySetResult();
                return Registered(id, $"api-token-{registrations}");
            }
            if (path.EndsWith("/heartbeat", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var config = TestConfig(vps: true);
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(SpinWait.SpinUntil(() => registration.IsRegistered && registration.ServerId == id, 3000));
        cts.Cancel();
        await run;

        Assert.Equal(2, registrations);
        Assert.Equal("api-token-1", Assert.Single(handler.Requests.Where(x => x.Path.EndsWith("/heartbeat"))).Token);
        Assert.Equal("api-token-2", Assert.Single(handler.Requests.Where(x => x.Method == HttpMethod.Delete)).Token);
    }

    [Fact]
    public async Task TemporaryHeartbeatFailure_KeepsApprovedRegistrationAndResultToken()
    {
        var id = Guid.NewGuid();
        var heartbeats = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/servers/register") return Registered(id);
            if (path.EndsWith("/heartbeat", StringComparison.Ordinal))
            {
                if (++heartbeats == 2) ready.TrySetResult();
                return new HttpResponseMessage(heartbeats == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var config = TestConfig(vps: true);
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await registration.ReportMatchResultAsync(Guid.NewGuid(), 1);
        cts.Cancel();
        await run;

        Assert.Single(handler.Requests.Where(x => x.Path == "/servers/register"));
        Assert.Equal("api-token", Assert.Single(handler.Requests.Where(x => x.Path == "/match/result")).Token);
        Assert.Equal(2, heartbeats);
    }

    [Fact]
    public async Task OnlyOneRegistrationLoopCanRun()
    {
        var id = Guid.NewGuid();
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath == "/servers/register"
                ? Registered(id) : new HttpResponseMessage(HttpStatusCode.OK));
        var config = TestConfig();
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var run = registration.RunAsync(cts.Token);
        Assert.True(SpinWait.SpinUntil(() => registration.IsRegistered, 3000));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RunAsync(cts.Token));
        cts.Cancel();
        await run;
        Assert.Single(handler.Requests.Where(x => x.Path == "/servers/register"));
    }

    [Fact]
    public async Task InvalidApprovedHostCredential_FailsWithoutRetry()
    {
        var config = TestConfig(vps: true);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RunAsync(cts.Token));
        Assert.Equal(config.RegistrationKey, Assert.Single(handler.Requests).Token);
        Assert.False(registration.IsRegistered);
    }

    [Fact]
    public void VpsProfile_RequiresDistinctProvisionedCredentials()
    {
        var config = TestConfig(vps: true);
        config.HostId = null;
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.HostId = Guid.NewGuid();
        config.RegistrationKey = null;
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.RegistrationKey = "registration-secret-0123456789abcdef";
        config.PublicIp = "http://bad-host";
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.PublicIp = "game.example.test";
        config.MatchControlKey = config.RegistrationKey;
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.MatchControlKey = "different-control-secret-0123456789abcdef";
        config.RegistrationKey = "invalid space secret-0123456789abcdef";
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.RegistrationKey = "registration-secret-0123456789abcdef";
        config.Validate();
        config.DeploymentProfile = "";
        Assert.Throws<InvalidOperationException>(config.Validate);
    }
}
