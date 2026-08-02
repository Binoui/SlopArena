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

        public MultiMatchOrchestrator(ServerConfig config)
        {
            _config = config;
        }

        /// <summary>Optional callback invoked with (match guid, winner steam id) when a match ends (issue #40).</summary>
        public Action<Guid, long>? ReportMatchResult { get; set; }

        /// <summary>
        /// Assign a new match to the next available port with a roster of
        /// players + character classes (issue #35). Returns the assigned UDP
        /// port, or -1 if no slots are available.
        /// </summary>
        public int AssignMatch(string matchId, string arenaName, IReadOnlyList<MatchPlayer> roster, byte maxStocks = 3)
        {
            for (int offset = 0; offset < _config.MaxConcurrentMatches; offset++)
            {
                int port = _config.Port + offset;
                if (!_activeMatches.ContainsKey(port))
                {
                    var match = new MatchInstance(port, matchId, arenaName, roster, OnMatchEnd, maxStocks, ReportMatchResult);
                    if (_activeMatches.TryAdd(port, match))
                    {
                        match.Start();
                        Console.WriteLine($"[Orchestrator] Match {matchId} assigned to port {port} ({_activeMatches.Count}/{_config.MaxConcurrentMatches}) — {roster.Count} players");
                        return port;
                    }
                }
            }

            Console.WriteLine($"[Orchestrator] No ports available for match {matchId} (max {_config.MaxConcurrentMatches})");
            return -1;
        }

        /// <summary>
        /// Assign a match with two Manki players (legacy/training path).
        /// </summary>
        public int AssignMatch(string matchId, string arenaName)
            => AssignMatch(matchId, arenaName, new[]
            {
                new MatchPlayer(0, CharacterClass.Manki, 1),
                new MatchPlayer(0, CharacterClass.Manki, 2),
            });

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
