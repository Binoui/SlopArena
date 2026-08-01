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
    }
}
