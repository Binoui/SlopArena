using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Buffers.Binary;
using System.Text.Json;
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

    [Fact]
    public async Task ConcurrentDevelopmentMatches_IsolateUdpRostersAndReleaseCapacityOnAbort()
    {
        using var controlReservation = new TcpListener(IPAddress.Loopback, 0);
        controlReservation.Start();
        int controlPort = ((IPEndPoint)controlReservation.LocalEndpoint).Port;
        controlReservation.Stop();
        int udpPort = FindUdpPortRange(2);
        var config = new ServerConfig
        {
            DeploymentProfile = "development",
            Port = udpPort,
            MaxConcurrentMatches = 2,
            MasterServerUrl = "http://127.0.0.1:1"
        };
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        var orchestrator = new MultiMatchOrchestrator(config);
        using var control = new MatchControlServer(orchestrator, controlPort, "slop_court");
        control.Start();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{controlPort}") };
        var matchA = Guid.NewGuid();
        var matchB = Guid.NewGuid();

        async Task<int> StartMatch(Guid matchId, params MatchPlayer[] players)
        {
            using var response = await client.PostAsync("/match/start",
                new StringContent(StartBody(matchId, players), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var content = document.RootElement.GetProperty("content");
            Assert.Equal(JsonValueKind.Object, content.ValueKind);
            Assert.True(content.GetProperty("schemaVersion").GetInt32() > 0);
            Assert.True(content.GetProperty("entries").GetArrayLength() > 0);
            Assert.True(MatchContentHandleMapCodec.TryParse(content, out var contentMap));
            Assert.Equal(SteamMatchDescriptor.HashContent(contentMap!),
                document.RootElement.GetProperty("contentHash").GetString());
            return document.RootElement.GetProperty("port").GetInt32();
        }

        try
        {
            int portA = await StartMatch(matchA,
                new MatchPlayer(101, CharacterClass.Manki, 11),
                new MatchPlayer(102, CharacterClass.FightGuy, 12));
            int portB = await StartMatch(matchB,
                new MatchPlayer(201, CharacterClass.FightGuy, 21),
                new MatchPlayer(202, CharacterClass.Manki, 22));
            Assert.NotEqual(portA, portB);
            Assert.Equal(2, orchestrator.CurrentMatchCount);

            using var peerA = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.True(await ProbeMatchAsync(peerA, portA, 11, 0x101, TimeSpan.FromSeconds(3)),
                "Match A did not route its roster entity over its assigned UDP port.");

            using var peerB = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var targetB = new IPEndPoint(IPAddress.Loopback, portB);
            long wrongNonce = 0x202;
            peerB.Send(InputFrame(11), 12 + InputState.Size, targetB);
            var wrongPing = PingFrame(wrongNonce);
            peerB.Send(wrongPing, wrongPing.Length, targetB);
            bool wrongRosterReceivedPong = false;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            {
                try
                {
                    var received = await peerB.ReceiveAsync(timeout.Token);
                    wrongRosterReceivedPong = IsPong(received.Buffer, wrongNonce);
                }
                catch (OperationCanceledException) { }
            }
            Assert.False(wrongRosterReceivedPong, "Match B accepted an entity from Match A's roster.");
            Assert.True(await ProbeMatchAsync(peerB, portB, 21, 0x203, TimeSpan.FromSeconds(3)),
                "Match B did not route its own roster entity over its assigned UDP port.");
            using var peerA2 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var peerB2 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var playingA = AwaitPlayingAsync(peerA, 11, peerA2, 12, portA);
            var playingB = AwaitPlayingAsync(peerB, 21, peerB2, 22, portB);
            Assert.True(await playingA, "Match A did not enter authoritative Playing state.");
            Assert.True(await playingB, "Match B did not enter authoritative Playing state.");

            var thirdMatch = Guid.NewGuid();
            using (var full = await client.PostAsync("/match/start",
                new StringContent(StartBody(thirdMatch,
                    new[] { new MatchPlayer(301, CharacterClass.Manki, 31),
                        new MatchPlayer(302, CharacterClass.FightGuy, 32) }),
                    Encoding.UTF8, "application/json")))
                Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
            Assert.Equal(2, orchestrator.CurrentMatchCount);

            using (var abort = await client.PostAsync("/match/abort",
                new StringContent(JsonSerializer.Serialize(new { matchId = matchA.ToString("D") }),
                    Encoding.UTF8, "application/json")))
                Assert.Equal(HttpStatusCode.OK, abort.StatusCode);
            Assert.Equal(1, orchestrator.CurrentMatchCount);

            int portC = await StartMatch(thirdMatch,
                new MatchPlayer(301, CharacterClass.Manki, 31),
                new MatchPlayer(302, CharacterClass.FightGuy, 32));
            Assert.Equal(portA, portC);
            Assert.Equal(2, orchestrator.CurrentMatchCount);
        }
        finally
        {
            control.Stop();
            orchestrator.Shutdown();
        }
    }

    private static string StartBody(Guid matchId, IReadOnlyList<MatchPlayer> players) =>
        JsonSerializer.Serialize(new
        {
            matchId = matchId.ToString("D"),
            arenaName = "slop_court",
            admissionExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(60),
            players = players.Select(player => new
            {
                steamId = player.SteamId,
                characterClass = player.CharacterClass.ToString(),
                entityId = player.EntityId
            })
        });

    private static int FindUdpPortRange(int count)
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            using var first = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            int start = ((IPEndPoint)first.Client.LocalEndPoint!).Port;
            if (start + count - 1 > 65535) continue;
            var adjacent = new List<UdpClient>();
            try
            {
                for (int offset = 1; offset < count; offset++)
                    adjacent.Add(new UdpClient(new IPEndPoint(IPAddress.Any, start + offset)));
                return start;
            }
            catch (SocketException) { }
            finally
            {
                foreach (var reservation in adjacent) reservation.Dispose();
            }
        }
        throw new InvalidOperationException("Could not reserve a free UDP port range.");
    }

    private static byte[] InputFrame(ulong entityId)
    {
        var frame = new byte[12 + InputState.Size];
        BinaryPrimitives.WriteUInt64LittleEndian(frame, entityId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), 1);
        new InputState().Write(frame.AsSpan(12));
        return frame;
    }

    private static byte[] PingFrame(long nonce)
    {
        var frame = new byte[12];
        frame[0] = (byte)'P';
        frame[1] = (byte)'I';
        frame[2] = (byte)'N';
        frame[3] = (byte)'G';
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(4), nonce);
        return frame;
    }

    private static bool IsPong(byte[] frame, long nonce) =>
        frame.Length == 16 && frame[0] == (byte)'P' && frame[1] == (byte)'O' &&
        frame[2] == (byte)'N' && frame[3] == (byte)'G' &&
        BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(4, 8)) == nonce;

    private static async Task<bool> AwaitPlayingAsync(
        UdpClient first, ulong firstEntity, UdpClient second, ulong secondEntity, int port)
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        var firstInput = InputFrame(firstEntity);
        var secondInput = InputFrame(secondEntity);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(9));
        var nextKeepAlive = DateTime.MinValue;
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= nextKeepAlive)
                {
                    first.Send(firstInput, firstInput.Length, endpoint);
                    second.Send(secondInput, secondInput.Length, endpoint);
                    nextKeepAlive = DateTime.UtcNow.AddMilliseconds(250);
                }
                var packet = await first.ReceiveAsync(timeout.Token);
                if (packet.Buffer.Length is not (ServerEntityPacket.NoInputSize or ServerEntityPacket.MaxSize))
                    continue;
                var state = ServerEntityPacket.Deserialize(packet.Buffer);
                if (state.EntityId == firstEntity && state.State.MatchState == MatchState.Playing)
                    return true;
            }
        }
        catch (OperationCanceledException) { }
        return false;
    }

    private static async Task<bool> ProbeMatchAsync(UdpClient peer, int port, ulong entityId,
        long nonce, TimeSpan timeout)
    {
        var target = new IPEndPoint(IPAddress.Loopback, port);
        byte[] input = InputFrame(entityId);
        byte[] ping = PingFrame(nonce);
        using var cancellation = new CancellationTokenSource(timeout);
        while (!cancellation.IsCancellationRequested)
        {
            peer.Send(input, input.Length, target);
            peer.Send(ping, ping.Length, target);
            try
            {
                var received = await peer.ReceiveAsync(cancellation.Token);
                if (IsPong(received.Buffer, nonce)) return true;
            }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }
}
