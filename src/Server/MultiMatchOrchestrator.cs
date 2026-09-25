using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.Server
{
    /// <summary>
    /// Orchestrates multiple MatchInstance threads on a game server VPS.
    /// Manages port allocation, match lifecycle, and provides status.
    ///
    /// Port allocation: base_port → base_port + max_matches - 1
    /// Each port handles one match (2-4 players).
    /// </summary>
    public class MultiMatchOrchestrator
    {
        private readonly ConcurrentDictionary<int, MatchInstance> _activeMatches = new();
        private readonly ServerConfig _config;
        private readonly MatchContentCatalogProvider _contentProvider;


        public MultiMatchOrchestrator(ServerConfig config)
        {
            _config = config;
            _contentProvider = new MatchContentCatalogProvider();
        }

        /// <summary>Optional callback invoked with (match guid, winner steam id) when a match ends (issue #40).</summary>
        public Action<Guid, long>? ReportMatchResult { get; set; }

        /// <summary>Assigns a match only after building its match-scoped catalog.</summary>
        public bool TryAssignMatch(string matchId, string arenaName, IReadOnlyList<MatchPlayer> roster, byte maxStocks,
            out int port, out MatchContentHandleMap? content, out string? error)
        {
            port = -1; content = null; error = null;
            if (roster == null || roster.Count is < 2 or > 4) { error = "Roster must contain 2-4 players."; return false; }
            if (!_contentProvider.TryBuild(out var catalog, out content, out error) || catalog == null) return false;
            for (int offset = 0; offset < _config.MaxConcurrentMatches; offset++)
            {
                int candidate = _config.Port + offset;
                if (_activeMatches.ContainsKey(candidate)) continue;
                MatchInstance match;
                try { match = new MatchInstance(candidate, matchId, arenaName, roster, catalog, OnMatchEnd, maxStocks, ReportMatchResult); }
                catch (Exception ex) { error = ex.Message; return false; }
                if (_activeMatches.TryAdd(candidate, match))
                {
                    match.Start();
                    port = candidate;
                    Console.WriteLine($"[Orchestrator] Match {matchId} assigned to port {port} ({_activeMatches.Count}/{_config.MaxConcurrentMatches}) — {roster.Count} players");
                    return true;
                }
            }
            error = $"No ports available for match {matchId} (max {_config.MaxConcurrentMatches}).";
            return false;
        }

        /// <summary>
        /// Called by MatchInstance when a match ends (thread callback).
        /// </summary>
        private void OnMatchEnd(int port)
        {
            if (_activeMatches.TryRemove(port, out _))
                Console.WriteLine($"[Orchestrator] Match on port {port} ended ({_activeMatches.Count}/{_config.MaxConcurrentMatches})");
        }

        /// <summary>
        /// Number of currently active matches.
        /// </summary>
        public int CurrentMatchCount => _activeMatches.Count;

        /// <summary>
        /// <summary>
        /// Graceful shutdown — stop all matches and wait for threads.
        /// </summary>
        public void Shutdown()
        {
            Console.WriteLine("[Orchestrator] Shutting down...");

            foreach (var kv in _activeMatches)
            {
                kv.Value?.Stop();
            }

            // Wait for threads to finish (max 5 seconds)
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_activeMatches.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
                foreach (var kv in _activeMatches)
                {
                    if (kv.Value == null || !kv.Value.IsRunning)
                        _activeMatches.TryRemove(kv.Key, out _);
                }
            }

            Console.WriteLine("[Orchestrator] Shutdown complete.");
        }
    }

    /// <summary>
    /// Deserialized from server.json at startup.
    /// </summary>
    public class ServerConfig
    {
        public string DeploymentProfile { get; set; } = string.Empty;
        public Guid? HostId { get; set; }
        public string? RegistrationKey { get; set; }
        public string? MatchControlKey { get; set; }
        public string ServerName { get; set; } = "SlopArena Server";
        public string Region { get; set; } = "EU";
        public int Port { get; set; }
        public int MaxConcurrentMatches { get; set; }
        public string MasterServerUrl { get; set; } = string.Empty;
        /// <summary>Public IP or DNS name advertised for gameplay UDP.</summary>
        public string? PublicIp { get; set; }
        public bool IsOfficial { get; set; }
        /// <summary>Directory containing .arena files relative to the working directory.</summary>
        public string ArenaDataDir { get; set; } = "data/arenas";
        public CustomRules? CustomRules { get; set; }

        public bool IsVps => DeploymentProfile == "vps";

        public void Validate()
        {
            if (DeploymentProfile is not ("vps" or "development"))
                throw new InvalidOperationException("Select deploymentProfile: vps or development explicitly.");
            if (Port is < 1 or > 65535 || MaxConcurrentMatches is < 1 or > 100 ||
                Port + MaxConcurrentMatches - 1 > 65535)
                throw new InvalidOperationException("Invalid GameServer port range.");
            if (!Uri.TryCreate(MasterServerUrl, UriKind.Absolute, out var master) ||
                master.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(master.UserInfo) ||
                !string.IsNullOrEmpty(master.Query) || !string.IsNullOrEmpty(master.Fragment) ||
                master.AbsolutePath != "/")
                throw new InvalidOperationException("Invalid masterServerUrl.");
            if (!IsVps) return;
            if (HostId is null || HostId == Guid.Empty ||
                string.IsNullOrWhiteSpace(PublicIp) ||
                Uri.CheckHostName(PublicIp) is not (UriHostNameType.IPv4 or UriHostNameType.Dns) ||
                !IsBearerCredential(RegistrationKey) || !IsBearerCredential(MatchControlKey) ||
                RegistrationKey == MatchControlKey)
                throw new InvalidOperationException("VPS host identity, public address and distinct credentials are required.");
        }

        private static bool IsBearerCredential(string? value)
        {
            if (value is null || value.Length is < 32 or > 4096) return false;
            bool padding = false;
            foreach (char c in value)
            {
                if (c == '=') { padding = true; continue; }
                if (padding || !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or '+' or '/'))
                    return false;
            }
            return true;
        }

        public static ServerConfig Load(string path)
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Empty GameServer configuration.");
            config.Validate();
            return config;
        }
    }

    public class CustomRules
    {
        public string[]? AllowedCharacters { get; set; }
        public string[]? AllowedMaps { get; set; }
    }
}
