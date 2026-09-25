using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace SlopArena.Server
{
    /// <summary>
    /// Owns one GameServer registration session: retries transient startup outages,
    /// heartbeats the Master, re-registers if the record is lost, and reports
    /// completed matches with the returned API token.
    /// </summary>
    public class GameServerRegistration
    {
        private readonly HttpClient _http;
        private readonly ServerConfig _config;
        private readonly MultiMatchOrchestrator _orchestrator;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private RegistrationSession? _session;
        private int _running;
        private sealed record RegistrationSession(Guid Id, string Token);
        private enum RegistrationOutcome { Success, Retry, Fatal }

        public Guid ServerId => Volatile.Read(ref _session)?.Id ?? Guid.Empty;
        public bool IsRegistered => Volatile.Read(ref _session) is not null;

        public GameServerRegistration(ServerConfig config, MultiMatchOrchestrator orchestrator, HttpMessageHandler? handler = null)
        {
            _config = config;
            _orchestrator = orchestrator;
            _http = new HttpClient(handler ?? new HttpClientHandler())
            {
                BaseAddress = new Uri(config.MasterServerUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>Single registration and heartbeat owner; retries transient outages until cancelled.</summary>
        public async Task RunAsync(CancellationToken ct, TimeSpan? heartbeatInterval = null, TimeSpan? initialRetryDelay = null)
        {
            var heartbeat = heartbeatInterval ?? TimeSpan.FromSeconds(10);
            var firstRetry = initialRetryDelay ?? TimeSpan.FromSeconds(1);
            if (Interlocked.Exchange(ref _running, 1) != 0)
                throw new InvalidOperationException("Registration loop is already running.");
            var retry = firstRetry;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (Volatile.Read(ref _session) is null)
                    {
                        var outcome = await TryRegisterAsync(ct);
                        if (outcome == RegistrationOutcome.Fatal)
                            throw new InvalidOperationException("GameServer registration rejected; check the deployment profile and approved host credential.");
                        if (outcome == RegistrationOutcome.Success)
                        {
                            retry = firstRetry;
                            continue;
                        }
                        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 251));
                        await Task.Delay(retry + jitter, ct);
                        retry = TimeSpan.FromMilliseconds(Math.Min(retry.TotalMilliseconds * 2, 30_000));
                    }
                    else
                    {
                        await Task.Delay(heartbeat, ct);
                        await SendHeartbeatAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            finally
            {
                // Cancellation must not prevent best-effort deregistration.
                await DeregisterAsync(CancellationToken.None);
                Volatile.Write(ref _running, 0);
            }
        }

        private async Task<RegistrationOutcome> TryRegisterAsync(CancellationToken ct)
        {
            try
            {
                var ip = _config.PublicIp ?? GetPublicIpAddress();
                var payload = new
                {
                    hostId = _config.HostId,
                    name = _config.ServerName,
                    ipAddress = ip,
                    port = _config.Port,
                    region = _config.Region,
                    isOfficial = _config.IsOfficial,
                    maxConcurrentMatches = _config.MaxConcurrentMatches,
                    customRulesJson = _config.CustomRules != null
                        ? JsonSerializer.Serialize(_config.CustomRules, _jsonOptions)
                        : null
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, "servers/register")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json")
                };
                if (_config.IsVps)
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.RegistrationKey);
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Registration] Rejected: {response.StatusCode}");
                    return (int)response.StatusCode is 408 or 429 or >= 500
                        ? RegistrationOutcome.Retry : RegistrationOutcome.Fatal;
                }

                var result = JsonSerializer.Deserialize<RegistrationResponse>(
                    await response.Content.ReadAsStringAsync(ct), _jsonOptions);
                if (result == null || result.ServerId == Guid.Empty || string.IsNullOrWhiteSpace(result.ApiToken))
                    return RegistrationOutcome.Fatal;
                Volatile.Write(ref _session, new RegistrationSession(result.ServerId, result.ApiToken));
                Console.WriteLine($"[Registration] Registered (ID: {result.ServerId}, public address: {ip})");
                return RegistrationOutcome.Success;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (HttpRequestException)
            {
                Console.WriteLine("[Registration] Master unavailable; retrying.");
                return RegistrationOutcome.Retry;
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[Registration] Master timed out; retrying.");
                return RegistrationOutcome.Retry;
            }
        }

        /// <summary>Best-effort removal of the current browser record during shutdown.</summary>
        private async Task DeregisterAsync(CancellationToken ct)
        {
            var session = Volatile.Read(ref _session);
            if (session is null) return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"servers/{session.Id}");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);
                using var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                    Console.WriteLine($"[Registration] Deregistered (ID: {session.Id}).");
                else
                    Console.WriteLine($"[Registration] Deregistration failed: {response.StatusCode}");
            }
            catch (Exception)
            {
                Console.WriteLine("[Registration] Deregistration unavailable.");
            }
            finally
            {
                Interlocked.CompareExchange(ref _session, null, session);
            }
        }

        private async Task SendHeartbeatAsync(CancellationToken ct)
        {
            var session = Volatile.Read(ref _session);
            if (session is null) return;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"servers/{session.Id}/heartbeat")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { currentMatches = _orchestrator.CurrentMatchCount }),
                    Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);
            try
            {
                using var response = await _http.SendAsync(request, ct);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("[Heartbeat] Master lost registration; reconnecting.");
                    Interlocked.CompareExchange(ref _session, null, session);
                }
                else if (!response.IsSuccessStatusCode)
                    Console.WriteLine($"[Heartbeat] Master unavailable: {response.StatusCode}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (HttpRequestException)
            {
                Console.WriteLine("[Heartbeat] Master unavailable.");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[Heartbeat] Master timed out.");
            }
        }

        /// <summary>Report a finished match using the current registration token.</summary>
        public async Task ReportMatchResultAsync(Guid matchId, long winnerSteamId, CancellationToken ct = default)
        {
            var session = Volatile.Read(ref _session);
            if (session is null) return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "match/result")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { matchId, winnerSteamId }),
                        Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    Console.WriteLine($"[MatchResult] Failed: {response.StatusCode}");
            }
            catch (Exception)
            {
                Console.WriteLine("[MatchResult] Master unavailable.");
            }
        }

        /// <summary>
        /// Determine the public IP of this machine.
        /// Tries to detect non-loopback IPv4, falls back to hostname resolution.
        /// </summary>
        private static string GetPublicIpAddress()
        {
            // Try to find a non-loopback, non-link-local IPv4 address
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address) &&
                        !addr.Address.ToString().StartsWith("169.254"))
                    {
                        return addr.Address.ToString();
                    }
                }
            }

            // Fallback: resolve hostname
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var addr in host.AddressList)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr))
                    {
                        return addr.ToString();
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        private class RegistrationResponse
        {
            public Guid ServerId { get; set; }
            public string ApiToken { get; set; } = string.Empty;
        }
    }
}
