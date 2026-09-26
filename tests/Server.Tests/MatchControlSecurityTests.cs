using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class MatchControlSecurityTests
{
    [Fact]
    public async Task MatchStart_RequiresPrivateCredentialBeforeAdmittingMatch()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var config = new ServerConfig { Port = port, MaxConcurrentMatches = 1 };
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        var orchestrator = new MultiMatchOrchestrator(config);
        using var control = new MatchControlServer(orchestrator, port, "slop_court", "control-secret-0123456789abcdef01234567");
        control.Start();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        const string body = """{"matchId":"auth-check","arenaName":"slop_court","players":[{"steamId":1,"characterClass":"Manki","entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}""";
        try
        {
            using var missing = await client.PostAsync("/match/start", new StringContent(body));
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
            using var wrong = await client.PostAsync("/match/start", new StringContent(body));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(0, orchestrator.CurrentMatchCount);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "control-secret-0123456789abcdef01234567");
            using var oversized = await client.PostAsync("/match/start", new StringContent(new string('x', 70_000), Encoding.UTF8));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
            Assert.Equal(0, orchestrator.CurrentMatchCount);
            using var accepted = await client.PostAsync("/match/start", new StringContent(body));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Contains($"\"port\":{port}", await accepted.Content.ReadAsStringAsync());
            const string abortBody = """{"matchId":"22222222-2222-2222-2222-222222222222"}""";
            client.DefaultRequestHeaders.Authorization = null;
            using var abortMissing = await client.PostAsync("/match/abort", new StringContent(abortBody));
            Assert.Equal(HttpStatusCode.Unauthorized, abortMissing.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
            using var abortWrong = await client.PostAsync("/match/abort", new StringContent(abortBody));
            Assert.Equal(HttpStatusCode.Unauthorized, abortWrong.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "control-secret-0123456789abcdef01234567");
            using var abort = await client.PostAsync("/match/abort", new StringContent(abortBody));
            Assert.Equal(HttpStatusCode.OK, abort.StatusCode);
            using var duplicateAbort = await client.PostAsync("/match/abort", new StringContent(abortBody));
            Assert.Equal(HttpStatusCode.OK, duplicateAbort.StatusCode);
        }
        finally
        {
            control.Stop();
            orchestrator.Shutdown();
        }
    }
}
