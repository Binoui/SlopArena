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

        public MatchContentCatalogProvider ContentProvider => _contentProvider;

        public MultiMatchOrchestrator(ServerConfig config)
        {
            _config = config;
            _contentProvider = new MatchContentCatalogProvider();
        }

        /// <summary>Optional callback invoked with (match guid, winner steam id) when a match ends (issue #40).</summary>
        public Action<Guid, long>? ReportMatchResult { get; set; }

        /// <summary>Assigns a match only after building its match-scoped catalog.</summary>
        public int AssignMatch(string matchId, string arenaName, IReadOnlyList<MatchPlayer> roster, byte maxStocks = MatchDefaults.DefaultMaxStocks)
            => TryAssignMatch(matchId, arenaName, roster, maxStocks, out var port, out _, out _) ? port : -1;

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
        /// Maximum concurrent matches (from config).
        /// </summary>
        public int MaxConcurrentMatches => _config.MaxConcurrentMatches;

        /// <summary>
        /// Server name from config.
        /// </summary>
        public string ServerName => _config.ServerName;

        /// <summary>
        /// Region from config.
        /// </summary>
        public string Region => _config.Region;

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
        public string ServerName { get; set; } = "SlopArena Server";
        public string Region { get; set; } = "EU";
        public int Port { get; set; } = 9876;
        public int MaxConcurrentMatches { get; set; } = 15;
        public string MasterServerUrl { get; set; } = "http://localhost:5000";
        /// <summary>
        /// Public IP or DNS name advertised to the master server (clients connect
        /// here over UDP). Null → auto-detect LAN IP (correct only for directly
        /// routable machines). Set behind NAT (e.g. "slop.barakaslurp.fr").
        /// </summary>
        public string? PublicIp { get; set; }
        public bool IsOfficial { get; set; } = false;
        /// <summary>Directory containing .arena files. Relative to the server working directory.</summary>
        public string ArenaDataDir { get; set; } = "data/arenas";
        public CustomRules? CustomRules { get; set; }

        public static ServerConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Config] {path} not found, using defaults.");
                return new ServerConfig();
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? new ServerConfig();
        }
    }

    public class CustomRules
    {
        public string[]? AllowedCharacters { get; set; }
        public string[]? AllowedMaps { get; set; }
    }
}
