using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SlopArena.Shared
{
    /// <summary>
    /// HTTP client for the SlopArena master server.
    /// Handles anonymous guest authentication and includes the JWT as a
    /// Bearer token in all subsequent master server requests.
    /// </summary>
    public class MasterServerClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly bool _ownsHttpClient;

        private string? _token;
        private long? _steamId;

        /// <summary>JWT bearer token, set after a successful AuthenticateGuestAsync.</summary>
        public string? Token => _token;

        /// <summary>Guest SteamId assigned by the master server, set after AuthenticateGuestAsync.</summary>
        public long? SteamId => _steamId;

        /// <summary>True after a successful guest auth call.</summary>
        public bool IsAuthenticated => _token != null;

        /// <summary>
        /// Create a client pointing at the given master server URL.
        /// </summary>
        public MasterServerClient(string masterServerUrl = "http://localhost:5000")
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(masterServerUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };
            _ownsHttpClient = true;
        }

        /// <summary>
        /// Create a client with a pre-configured HttpClient (for testing or DI).
        /// The caller owns the HttpClient lifetime.
        /// </summary>
        public MasterServerClient(HttpClient httpClient)
        {
            _http = httpClient;
            _ownsHttpClient = false;
        }

        /// <summary>
        /// Authenticate as a guest: POST /auth/guest → receive JWT + SteamId.
        /// Stores the token and attaches it as Bearer header for subsequent requests.
        /// Returns false on network error or non-2xx response.
        /// </summary>
        public async Task<bool> AuthenticateGuestAsync(CancellationToken ct = default)
        {
            try
            {
                using var response = await _http.PostAsync("auth/guest", content: null, ct);
                if (!response.IsSuccessStatusCode)
                    return false;

                var json = await response.Content.ReadAsStringAsync();
                var token = ExtractStringField(json, "token");
                var steamId = ExtractNumberField(json, "steamId");

                if (string.IsNullOrEmpty(token) || steamId == null)
                    return false;

                _token = token;
                _steamId = steamId;
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _token);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Fetch the current user's info: GET /auth/me (requires prior auth).
        /// Returns null if not authenticated, network error, or non-2xx response.
        /// </summary>
        public async Task<GuestUserInfo?> GetMeAsync(CancellationToken ct = default)
        {
            if (!IsAuthenticated)
                return null;

            try
            {
                using var response = await _http.GetAsync("auth/me", ct);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var steamId = ExtractNumberField(json, "steamId");
                var username = ExtractStringField(json, "username");
                var mmr = ExtractNumberField(json, "mmr");

                if (steamId == null || username == null || mmr == null)
                    return null;

                return new GuestUserInfo
                {
                    SteamId = steamId.Value,
                    Username = username,
                    Mmr = (int)mmr.Value
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        // ── Lightweight JSON field extraction (avoids System.Text.Json dependency) ──
        // Safe for controlled API responses: JWT tokens are base64url (no quotes),
        // guest usernames are alphanumeric+dashes, numbers are non-negative integers.

        private static string? ExtractStringField(string json, string fieldName)
        {
            var match = Regex.Match(json, $"\"{fieldName}\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static long? ExtractNumberField(string json, string fieldName)
        {
            var match = Regex.Match(json, $"\"{fieldName}\"\\s*:\\s*(\\d+)");
            return match.Success ? long.Parse(match.Groups[1].Value) : null;
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _http.Dispose();
        }
    }
}
