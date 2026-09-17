using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        private string? _token;
        private long? _steamId;
        private DateTimeOffset? _expiresAt;

        /// <summary>JWT bearer token, set after a successful AuthenticateGuestAsync.</summary>
        public string? Token => _token;

        /// <summary>Guest SteamId assigned by the master server, set after AuthenticateGuestAsync.</summary>
        public long? SteamId => _steamId;

        /// <summary>UTC expiry returned by the master for the current token.</summary>
        public DateTimeOffset? TokenExpiresAt => _expiresAt;

        /// <summary>True after a successful guest auth call.</summary>
        public bool IsAuthenticated => !string.IsNullOrEmpty(_token);

        /// <summary>HTTP status from the latest failed request, when available.</summary>
        public int? LastStatusCode { get; private set; }

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
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = false;
        }

        /// <summary>
        /// Authenticate as a fresh guest: POST /auth/guest. The returned
        /// identity is intentionally new for each game launch.
        /// </summary>
        public Task<bool> AuthenticateGuestAsync(CancellationToken ct = default) =>
            AuthenticateGuestCoreAsync(ct);

        /// <summary>
        /// Authenticate a fresh guest and apply its chosen display name before
        /// returning. This convenience overload still performs exactly one
        /// guest creation request.
        /// </summary>
        public async Task<bool> AuthenticateGuestAsync(string? displayName, CancellationToken ct = default)
        {
            if (!await AuthenticateGuestCoreAsync(ct))
                return false;
            return string.IsNullOrWhiteSpace(displayName) ||
                await SetDisplayNameAsync(displayName, ct) != null;
        }

        private async Task<bool> AuthenticateGuestCoreAsync(CancellationToken ct)
        {
            if (IsAuthenticated)
                return true;
            try
            {
                LastStatusCode = null;
                using var response = await _http.PostAsync("auth/guest", content: null, ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return false;

                var json = await response.Content.ReadAsStringAsync();
                var auth = JsonSerializer.Deserialize<GuestAuthResponse>(json, ServerJsonOptions);
                if (auth == null || string.IsNullOrEmpty(auth.Token) || auth.SteamId <= 0)
                    return false;

                _token = auth.Token;
                _steamId = auth.SteamId;
                _expiresAt = auth.ExpiresAt == default ? null : auth.ExpiresAt;
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
        /// Apply or replace the authenticated guest's display name:
        /// PUT /auth/name. A null result represents validation, membership,
        /// authentication, network, or server failure.
        /// </summary>
        public async Task<GuestUserInfo?> SetDisplayNameAsync(
            string displayName, CancellationToken ct = default)
        {
            if (!IsAuthenticated)
                return null;
            try
            {
                LastStatusCode = null;
                var payload = JsonSerializer.Serialize(
                    new SetDisplayNamePayload(displayName ?? string.Empty), ServerJsonOptions);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await _http.PutAsync("auth/name", content, ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return null;
                var json = await response.Content.ReadAsStringAsync();
                // PUT /auth/name returns the chat profile ({playerId,
                // displayName, sessionTag}), while /auth/me uses its legacy
                // ({steamId, username, ...}) shape. Normalize both at this
                // HTTP boundary so callers never lose the identity.
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("playerId", out var playerId) ||
                    !root.TryGetProperty("displayName", out var name) ||
                    !root.TryGetProperty("sessionTag", out var tag) ||
                    playerId.ValueKind != JsonValueKind.String ||
                    name.ValueKind != JsonValueKind.String ||
                    tag.ValueKind != JsonValueKind.String)
                    return null;
                var rawPlayerId = playerId.GetString() ?? string.Empty;
                if (rawPlayerId.Length == 0)
                    return null;
                long.TryParse(rawPlayerId, out var steamId);
                if (steamId <= 0)
                    steamId = _steamId ?? 0;
                return new GuestUserInfo
                {
                    SteamId = steamId,
                    PlayerId = rawPlayerId,
                    Username = name.GetString() ?? string.Empty,
                    SessionTag = tag.GetString() ?? string.Empty
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
        /// Renew the current JWT via POST /auth/refresh without creating a new
        /// identity. A response for a different identity is rejected.
        /// </summary>
        public async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            if (!IsAuthenticated || !_steamId.HasValue)
                return false;
            try
            {
                LastStatusCode = null;
                using var response = await _http.PostAsync("auth/refresh", content: null, ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return false;
                var json = await response.Content.ReadAsStringAsync();
                var auth = JsonSerializer.Deserialize<GuestAuthResponse>(json, ServerJsonOptions);
                if (auth == null || string.IsNullOrEmpty(auth.Token) ||
                    auth.SteamId != _steamId.Value)
                    return false;

                _token = auth.Token;
                _expiresAt = auth.ExpiresAt == default ? null : auth.ExpiresAt;
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
                LastStatusCode = null;
                using var response = await _http.GetAsync("auth/me", ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<GuestUserInfo>(json, ServerJsonOptions);
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
                LastStatusCode = null;
                using var response = await _http.GetAsync("servers", ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<ServerInfo>>(json, ServerJsonOptions);
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

        public void ClearAuthentication()
        {
            _token = null;
            _steamId = null;
            _expiresAt = null;
            _http.DefaultRequestHeaders.Authorization = null;
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _http.Dispose();
        }

        private sealed class SetDisplayNamePayload
        {
            public SetDisplayNamePayload(string displayName) => DisplayName = displayName;
            public string DisplayName { get; }
        }
    }
}
