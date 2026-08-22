using System.Collections.Generic;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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

        private static readonly JsonSerializerOptions ServerJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };
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
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("token", out var tokenElement) ||
                    tokenElement.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("steamId", out var steamIdElement) ||
                    steamIdElement.ValueKind != JsonValueKind.Number ||
                    !steamIdElement.TryGetInt64(out var steamId))
                    return false;
                var token = tokenElement.GetString();
                if (string.IsNullOrEmpty(token))
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
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("steamId", out var steamIdElement) ||
                    steamIdElement.ValueKind != JsonValueKind.Number ||
                    !steamIdElement.TryGetInt64(out var steamId) ||
                    !root.TryGetProperty("username", out var usernameElement) ||
                    usernameElement.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("mmr", out var mmrElement) ||
                    mmrElement.ValueKind != JsonValueKind.Number ||
                    !mmrElement.TryGetInt64(out var mmr))
                    return null;
                var username = usernameElement.GetString();
                if (username == null)
                    return null;

                return new GuestUserInfo
                {
                    SteamId = steamId,
                    Username = username,
                    Mmr = (int)mmr
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

        /// <summary>
        /// Fetch the server browser list: GET /servers (requires prior auth).
        /// Returns null if not authenticated, network error, or non-2xx response.
        /// Returns an empty list if the server is up but has no servers to list.
        /// </summary>
        public async Task<List<ServerInfo>?> GetServersAsync(CancellationToken ct = default)
        {
            if (!IsAuthenticated)
                return null;

            try
            {
                using var response = await _http.GetAsync("servers", ct);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var servers = JsonSerializer.Deserialize<List<ServerInfo>>(json, ServerJsonOptions);
                return servers;
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


        public void Dispose()
        {
            if (_ownsHttpClient)
                _http.Dispose();
        }
    }
}
