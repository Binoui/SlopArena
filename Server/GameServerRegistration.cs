using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SlopArena.Server
{
    /// <summary>
    /// Handles game server registration with the master server.
    /// On startup: registers and obtains server_id + api_token.
    /// Continuously: sends heartbeats every 10 seconds.
    /// On match end: reports result to master.
    /// </summary>
    public class GameServerRegistration
    {
        private readonly HttpClient _http;
        private readonly ServerConfig _config;
        private readonly MultiMatchOrchestrator _orchestrator;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private Guid _serverId;
        private string _apiToken = string.Empty;
        private bool _registered;

        public Guid ServerId => _serverId;
        public bool IsRegistered => _registered;

        public GameServerRegistration(ServerConfig config, MultiMatchOrchestrator orchestrator)
        {
            _config = config;
            _orchestrator = orchestrator;
            _http = new HttpClient
            {
                BaseAddress = new Uri(config.MasterServerUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>
        /// Register this game server with the master server.
        /// Returns true if registration succeeded.
        /// </summary>
        public async Task<bool> RegisterAsync(CancellationToken ct = default)
        {
            try
            {
                var ip = GetPublicIpAddress();
                var payload = new
                {
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

                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync("servers/register", content, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Registration] Failed: {response.StatusCode} — {await response.Content.ReadAsStringAsync()}");
                    return false;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<RegistrationResponse>(responseJson, _jsonOptions);

                if (result == null || string.IsNullOrEmpty(result.ApiToken))
                {
                    Console.WriteLine($"[Registration] Invalid response: {responseJson}");
                    return false;
                }

                _serverId = result.ServerId;
                _apiToken = result.ApiToken;
                _registered = true;

                Console.WriteLine($"[Registration] Registered as '{_config.ServerName}' (ID: {_serverId}, IP: {ip})");
                return true;
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[Registration] Timed out.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Registration] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start the heartbeat loop. Sends current match count every 10 seconds.
        /// Blocks until cancellation is requested.
        /// </summary>
        public async Task RunHeartbeatLoopAsync(CancellationToken ct = default)
        {
            if (!_registered)
            {
                Console.WriteLine("[Heartbeat] Not registered — cannot start heartbeat loop.");
                return;
            }

            Console.WriteLine("[Heartbeat] Loop started (every 10s).");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10_000, ct);
                    await SendHeartbeatAsync(ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Heartbeat] Error: {ex.Message}");
                }
            }

            Console.WriteLine("[Heartbeat] Loop stopped.");
        }

        private async Task SendHeartbeatAsync(CancellationToken ct)
        {
            var payload = new { currentMatches = _orchestrator.CurrentMatchCount };
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"servers/{_serverId}/heartbeat")
            {
                Content = content
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiToken);

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Heartbeat] Failed: {response.StatusCode}");
            }
        }

        /// <summary>
        /// Report a match result to the master server (ELO MMR update).
        /// </summary>
        public async Task ReportMatchResultAsync(Guid matchId, long winnerSteamId, CancellationToken ct = default)
        {
            if (!_registered) return;

            try
            {
                var payload = new { matchId, winnerSteamId };
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, "match/result")
                {
                    Content = content
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiToken);

                var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[MatchResult] Failed: {response.StatusCode}");
                }
                else
                {
                    Console.WriteLine($"[MatchResult] Reported: match={matchId}, winner={winnerSteamId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchResult] Error: {ex.Message}");
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
