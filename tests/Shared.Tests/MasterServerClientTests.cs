using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests
{
    /// <summary>
    /// A delegating HTTP handler that returns canned responses for testing
    /// without a real server.
    /// </summary>
    internal class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    public class MasterServerClientTests
    {
        private const string SampleToken =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWJfaWQiOiIxMjM0NTYif2.abc123";
        private const long SampleSteamId = 42L;

        private const string GuestAuthJson =
            "{\"token\":\"" + SampleToken + "\",\"steamId\":42}";
        private const string EscapedServerListJson =
            "[{\"id\":\"a1b2c3d4-e5f6-7890-abcd-ef1234567890\"," +
            "\"name\":\"EU \\\"Prime\\\" & <1>\"," +
            "\"ipAddress\":\"127.0.0.1\",\"port\":9876,\"region\":\"eu-west\"," +
            "\"currentMatches\":0,\"maxConcurrentMatches\":4,\"isOfficial\":true}]";

        private const string UserInfoJson =
            "{\"steamId\":42,\"username\":\"Guest-12345\",\"mmr\":1000}";

        // ── Helpers ──

        private static MasterServerClient MakeClient(
            Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var handler = new MockHttpHandler(responder);
            return new MasterServerClient(new HttpClient(handler)
            {
                BaseAddress = new Uri("http://test/")
            });
        }

        private static HttpResponseMessage JsonOk(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

        private static HttpResponseMessage GuestAuthResponse() => JsonOk(GuestAuthJson);

        // ── AuthenticateGuestAsync ──

        [Fact]
        public async Task AuthenticateGuest_StoresTokenAndSteamId()
        {
            using var client = MakeClient(req =>
            {
                Assert.Equal("http://test/auth/guest", req.RequestUri!.ToString());
                Assert.Equal(HttpMethod.Post, req.Method);
                return GuestAuthResponse();
            });

            bool ok = await client.AuthenticateGuestAsync();

            Assert.True(ok);
            Assert.Equal(SampleToken, client.Token);
            Assert.Equal(SampleSteamId, client.SteamId);
            Assert.True(client.IsAuthenticated);
        }

        [Fact]
        public async Task AuthenticateGuest_ReturnsFalseOnServerError()
        {
            using var client = MakeClient(_ =>
                new HttpResponseMessage(HttpStatusCode.InternalServerError));

            bool ok = await client.AuthenticateGuestAsync();

            Assert.False(ok);
            Assert.Null(client.Token);
            Assert.False(client.IsAuthenticated);
        }

        [Fact]
        public async Task AuthenticateGuest_ReturnsFalseOnMissingToken()
        {
            using var client = MakeClient(_ =>
                JsonOk($"{{\"steamId\":{SampleSteamId}}}"));

            bool ok = await client.AuthenticateGuestAsync();

            Assert.False(ok);
            Assert.Null(client.Token);
        }
        [Fact]
        public async Task AuthenticateGuest_ReturnsFalseOnMalformedJson()
        {
            using var client = MakeClient(_ => JsonOk("{"));

            Assert.False(await client.AuthenticateGuestAsync());
        }

        [Fact]
        public async Task NamingRetry_KeepsGuestIdentityAndUsesChosenProfile()
        {
            int guestsCreated = 0;
            int nameAttempts = 0;
            using var client = MakeClient(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/auth/guest")
                {
                    guestsCreated++;
                    return JsonOk($"{{\"token\":\"guest-token\",\"steamId\":{42 + guestsCreated - 1}}}");
                }
                if (++nameAttempts == 1)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                return JsonOk("{\"playerId\":\"42\",\"displayName\":\"Chosen name\",\"sessionTag\":\"16\"}");
            });

            Assert.False(await client.AuthenticateGuestAsync("Chosen name"));
            Assert.True(await client.AuthenticateGuestAsync("Chosen name"));
            var profile = await client.SetDisplayNameAsync("Chosen name");

            Assert.Equal(1, guestsCreated);
            Assert.Equal(42L, client.SteamId);
            Assert.NotNull(profile);
            Assert.Equal(42L, profile!.SteamId);
            Assert.Equal("Chosen name", profile.Username);
            Assert.Equal("16", profile.SessionTag);
        }

        [Fact]
        public async Task Refresh_RejectsIdentitySubstitutionAndKeepsExistingCredential()
        {
            using var client = MakeClient(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/auth/guest")
                    return GuestAuthResponse();
                if (request.RequestUri.AbsolutePath == "/auth/refresh")
                    return JsonOk("{\"token\":\"other-player-token\",\"steamId\":99,\"expiresAt\":\"2099-01-01T00:00:00Z\"}");
                return request.Headers.Authorization?.Parameter == SampleToken
                    ? JsonOk(UserInfoJson)
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            });

            Assert.True(await client.AuthenticateGuestAsync());
            Assert.False(await client.RefreshAsync());
            var self = await client.GetMeAsync();

            Assert.NotNull(self);
            Assert.Equal(SampleSteamId, self!.SteamId);
            Assert.Equal(SampleSteamId, client.SteamId);
        }

        [Fact]
        public async Task Refresh_RotatesCredentialWithoutGuestSignIn()
        {
            int guestRequests = 0;
            using var client = MakeClient(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/auth/guest")
                {
                    guestRequests++;
                    return GuestAuthResponse();
                }
                if (request.RequestUri.AbsolutePath == "/auth/refresh")
                    return JsonOk("{\"token\":\"renewed-token\",\"steamId\":42,\"expiresAt\":\"2099-01-01T00:00:00Z\"}");
                return request.Headers.Authorization?.Parameter == "renewed-token"
                    ? JsonOk(UserInfoJson)
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            });

            Assert.True(await client.AuthenticateGuestAsync());
            Assert.True(await client.RefreshAsync());
            var self = await client.GetMeAsync();

            Assert.NotNull(self);
            Assert.Equal(SampleSteamId, self!.SteamId);
            Assert.Equal(1, guestRequests);
            Assert.Equal(DateTimeOffset.Parse("2099-01-01T00:00:00Z"), client.TokenExpiresAt);
        }

        // ── GetMeAsync ──

        [Fact]
        public async Task GetMe_ReturnsUserInfoAfterAuth()
        {
            string? capturedAuthHeader = null;
            using var client = MakeClient(req =>
            {
                if (req.RequestUri!.ToString().Contains("auth/guest"))
                    return GuestAuthResponse();

                capturedAuthHeader = req.Headers.Authorization?.ToString();
                return JsonOk(UserInfoJson);
            });

            await client.AuthenticateGuestAsync();
            var info = await client.GetMeAsync();

            Assert.NotNull(info);
            Assert.Equal(SampleSteamId, info!.SteamId);
            Assert.Equal("Guest-12345", info.Username);
            Assert.Equal(1000, info.Mmr);
            Assert.Equal($"Bearer {SampleToken}", capturedAuthHeader);
        }

        [Fact]
        public async Task GetMe_ReturnsNullBeforeAuth()
        {
            using var client = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

            var info = await client.GetMeAsync();

            Assert.Null(info);
        }

        [Fact]
        public async Task GetMe_ReturnsNullOnUnauthorized()
        {
            using var client = MakeClient(req =>
                req.RequestUri!.ToString().Contains("auth/guest")
                    ? GuestAuthResponse()
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized));

            await client.AuthenticateGuestAsync();
            var info = await client.GetMeAsync();

            Assert.Null(info);
        }
        [Fact]
        public async Task GetMe_ReturnsNullOnMalformedJson()
        {
            using var client = MakeClient(req =>
                req.RequestUri!.ToString().Contains("auth/guest")
                    ? GuestAuthResponse()
                    : JsonOk("{"));

            await client.AuthenticateGuestAsync();

            Assert.Null(await client.GetMeAsync());
        }

        // ── Bearer header propagation ──

        [Fact]
        public async Task AuthenticatedRequestsIncludeBearerHeader()
        {
            string? authHeader = null;
            using var client = MakeClient(req =>
            {
                if (req.RequestUri!.ToString().Contains("auth/guest"))
                    return GuestAuthResponse();

                authHeader = req.Headers.Authorization?.ToString();
                return JsonOk(UserInfoJson);
            });

            await client.AuthenticateGuestAsync();
            await client.GetMeAsync();

            Assert.Equal($"Bearer {SampleToken}", authHeader);
        }

        [Fact]
        public async Task AuthenticateGuest_TokenIsNullBeforeAuth()
        {
            using var client = MakeClient(_ => GuestAuthResponse());

            Assert.Null(client.Token);
            Assert.False(client.IsAuthenticated);

            await client.AuthenticateGuestAsync();

            Assert.Equal(SampleToken, client.Token);
            Assert.True(client.IsAuthenticated);
        }

        // ── Constructor ──

        [Fact]
        public async Task Constructor_SetsBaseAddressWithTrailingSlash()
        {
            var handler = new MockHttpHandler(req =>
            {
                Assert.Equal("http://localhost:5000/auth/guest", req.RequestUri!.ToString());
                return GuestAuthResponse();
            });

            using var client = new MasterServerClient(new HttpClient(handler)
            {
                BaseAddress = new Uri("http://localhost:5000/")
            });
            Assert.True(await client.AuthenticateGuestAsync());
        }

        // ── GetServersAsync ──

        private const string ServerListJson =
            "[{\"id\":\"a1b2c3d4-e5f6-7890-abcd-ef1234567890\"," +
            "\"name\":\"Test EU Server\"," +
            "\"ipAddress\":\"127.0.0.1\"," +
            "\"port\":9876," +
            "\"region\":\"eu-west\"," +
            "\"currentMatches\":2," +
            "\"maxConcurrentMatches\":15," +
            "\"isOfficial\":true}," +
            "{\"id\":\"b2c3d4e5-f6a7-8901-bcde-f12345678901\"," +
            "\"name\":\"Community US\"," +
            "\"ipAddress\":\"10.0.0.5\"," +
            "\"port\":9877," +
            "\"region\":\"us-east\"," +
            "\"currentMatches\":0," +
            "\"maxConcurrentMatches\":8," +
            "\"isOfficial\":false}]";

        [Fact]
        public async Task GetServers_ReturnsServerListAfterAuth()
        {
            string? capturedAuthHeader = null;
            using var client = MakeClient(req =>
            {
                if (req.RequestUri!.ToString().Contains("auth/guest"))
                    return GuestAuthResponse();

                Assert.Equal("http://test/servers", req.RequestUri!.ToString());
                Assert.Equal(HttpMethod.Get, req.Method);
                capturedAuthHeader = req.Headers.Authorization?.ToString();
                return JsonOk(ServerListJson);
            });

            await client.AuthenticateGuestAsync();
            var servers = await client.GetServersAsync();

            Assert.NotNull(servers);
            Assert.Equal(2, servers!.Count);
            Assert.Equal($"Bearer {SampleToken}", capturedAuthHeader);

            var s0 = servers[0];
            Assert.Equal("a1b2c3d4-e5f6-7890-abcd-ef1234567890", s0.Id.ToString());
            Assert.Equal("Test EU Server", s0.Name);
            Assert.Equal("127.0.0.1", s0.IpAddress);
            Assert.Equal(9876, s0.Port);
            Assert.Equal("eu-west", s0.Region);
            Assert.Equal(2, s0.CurrentMatches);
            Assert.Equal(15, s0.MaxConcurrentMatches);
            Assert.True(s0.IsOfficial);

            var s1 = servers[1];
            Assert.Equal("b2c3d4e5-f6a7-8901-bcde-f12345678901", s1.Id.ToString());
            Assert.False(s1.IsOfficial);
            Assert.Equal("us-east", s1.Region);
        }
        [Fact]
        public async Task GetServers_DeserializesEscapedServerName()
        {
            using var client = MakeClient(req =>
                req.RequestUri!.ToString().Contains("auth/guest")
                    ? GuestAuthResponse()
                    : JsonOk(EscapedServerListJson));

            await client.AuthenticateGuestAsync();
            var servers = await client.GetServersAsync();

            Assert.NotNull(servers);
            Assert.Single(servers!);
            Assert.Equal("EU \"Prime\" & <1>", servers[0].Name);
        }

        [Fact]
        public async Task GetServers_ReturnsNullBeforeAuth()
        {
            using var client = MakeClient(_ => JsonOk("[]"));
            var servers = await client.GetServersAsync();
            Assert.Null(servers);
        }

        [Fact]
        public async Task GetServers_ReturnsEmptyListWhenNoServers()
        {
            using var client = MakeClient(req =>
                req.RequestUri!.ToString().Contains("auth/guest")
                    ? GuestAuthResponse()
                    : JsonOk("[]"));

            await client.AuthenticateGuestAsync();
            var servers = await client.GetServersAsync();

            Assert.NotNull(servers);
            Assert.Empty(servers!);
        }

        [Fact]
        public async Task GetServers_ReturnsNullOnServerError()
        {
            using var client = MakeClient(req =>
                req.RequestUri!.ToString().Contains("auth/guest")
                    ? GuestAuthResponse()
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError));

            await client.AuthenticateGuestAsync();
            var servers = await client.GetServersAsync();
            Assert.Null(servers);
        }
        [Fact]
        public async Task GetServers_ReturnsNullOnMalformedJson()
        {
            using var client = MakeClient(req =>
                req.RequestUri!.ToString().Contains("auth/guest")
                    ? GuestAuthResponse()
                    : JsonOk("{"));

            await client.AuthenticateGuestAsync();

            Assert.Null(await client.GetServersAsync());
        }
    }
}
