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
    /// <summary>HTTP client for the master server; carries one application JWT.</summary>
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
        private bool _steamAuthenticated;
        private int _sessionVersion;

        /// <summary>JWT bearer token for the current application session.</summary>
        public string? Token => _token;

        /// <summary>Verified Steam identity (or development guest identity).</summary>
        public long? SteamId => _steamId;

        /// <summary>UTC expiry returned by the master for the current token.</summary>
        public DateTimeOffset? TokenExpiresAt => _expiresAt;

        public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
        public bool IsSteamAuthenticated => IsAuthenticated && _steamAuthenticated;

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
        /// <summary>Authenticate with a fresh Steam web ticket, encoded as hex.</summary>
        public async Task<bool> AuthenticateSteamAsync(string ticket, CancellationToken ct = default)
        {
            if (IsAuthenticated || string.IsNullOrWhiteSpace(ticket))
                return false;
            return await ApplyAuthAsync("auth/steam", ticket, null, ct, steamLogin: true);
        }

        private async Task<bool> ApplyAuthAsync(string path, string? ticket, long? expectedSteamId,
            CancellationToken ct, bool steamLogin = false)
        {
            int version = _sessionVersion;
            try
            {
                LastStatusCode = null;
                using var content = ticket == null ? null : new StringContent(
                    JsonSerializer.Serialize(new SteamAuthPayload(ticket), ServerJsonOptions),
                    Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(path, content, ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return false;
                var auth = JsonSerializer.Deserialize<GuestAuthResponse>(
                    await response.Content.ReadAsStringAsync(), ServerJsonOptions);
                if (version != _sessionVersion || auth == null || string.IsNullOrEmpty(auth.Token) ||
                    auth.SteamId <= 0 || (expectedSteamId.HasValue && auth.SteamId != expectedSteamId.Value))
                    return false;
                _token = auth.Token;
                _steamId = auth.SteamId;
                _expiresAt = auth.ExpiresAt == default ? null : auth.ExpiresAt;
                if (steamLogin)
                    _steamAuthenticated = true;
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
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

        /// <summary>Renew the same account with a fresh Steam ticket; development guests use no ticket.</summary>
        public Task<bool> RefreshAsync(string? freshSteamTicket, CancellationToken ct = default)
        {
            if (!IsAuthenticated || !_steamId.HasValue || (_steamAuthenticated && string.IsNullOrWhiteSpace(freshSteamTicket)))
                return Task.FromResult(false);
            return ApplyAuthAsync("auth/refresh", _steamAuthenticated ? freshSteamTicket : null, _steamId, ct);
        }

        public Task<bool> RefreshAsync(CancellationToken ct = default) => RefreshAsync(null, ct);

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
            _sessionVersion++;
            _token = null;
            _steamId = null;
            _expiresAt = null;
            _steamAuthenticated = false;
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

        private sealed class SteamAuthPayload
        {
            public SteamAuthPayload(string ticket) => Ticket = ticket;
            public string Ticket { get; }
        }
    }
}
