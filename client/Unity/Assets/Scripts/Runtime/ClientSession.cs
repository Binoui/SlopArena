#nullable enable
using System;
using System.Collections.Generic;

namespace SlopArena.Client
{
    /// <summary>
    /// Static client-side session state bridging the master server guest auth
    /// (performed on the Server Browser screen) to the SignalR lobby client
    /// (LobbyRoom screen). The Server Browser authenticates as a guest via
    /// <c>MasterServerClient</c>, then stashes the JWT + SteamId + selected
    /// server here before loading the <c>LobbyRoom</c> scene.
    /// </summary>
    public static class ClientSession
    {
        /// <summary>Master server base URL (release default; dev overrides via scene inspector).</summary>
        public static string MasterServerUrl = "https://sloparena.barakaslurp.fr";

        /// <summary>Guest JWT bearer token; null until guest auth succeeds.</summary>
        public static string? AuthToken;

        /// <summary>Guest SteamId assigned by the master server.</summary>
        public static long SteamId;

        /// <summary>Guest display name, populated from /auth/me (optional).</summary>
        public static string? Username;

        /// <summary>Game server the player selected in the Server Browser.</summary>
        public static Guid SelectedServerId;

        /// <summary>Display name of the selected game server (for the lobby title).</summary>
        public static string SelectedServerName = string.Empty;

        /// <summary>
        /// The live SignalR lobby connection, kept alive across scene transitions
        /// (LobbyRoom → CharSelect, issue #34). Created by LobbyRoomUI on first
        /// connect, reused by CharSelectController, disposed on leave/return to
        /// server browser.
        /// </summary>
        public static Network.LobbyClient? ActiveLobby;

        /// <summary>
        /// Roster snapshot stashed by LobbyRoomUI.OnMatchStarting so
        /// CharSelectController has the player list immediately on scene load,
        /// before the first LobbyUpdated push arrives (issue #34).
        /// </summary>
        public static Shared.LobbySnapshot? LobbyRoster;

        /// <summary>Roster stashed at match start (entityId → name/class) for the results screen (issue #40).</summary>
        public static IReadOnlyList<Shared.LobbyPlayerInfo>? MatchRoster;

        /// <summary>
        /// True when the local player is the lobby host. Computed from the
        /// roster (first player in the lobby), NOT <c>MatchConfig.IsHost</c> —
        /// on a dedicated server (alfred) every client joins via the server
        /// browser, which sets <c>MatchConfig.IsHost=false</c> even for the
        /// player the master promotes to lobby host. Set by CharSelectController
        /// on scene load, consumed by StageSelectController.
        /// </summary>
        public static bool IsLobbyHost;

        /// <summary>Final standings, set by PvPMatch when the match ends; consumed by ResultsUI.</summary>
        public static MatchResultsData? CurrentMatchResults;

        /// <summary>One player's final standing line on the results screen.</summary>
        public sealed class ResultEntry
        {
            public ulong EntityId;
            public string Name = "";
            public string ClassName = "";
            public int StocksRemaining;
            public int DamagePercent;
            public bool IsWinner;
        }

        /// <summary>Final standings snapshot for the results screen.</summary>
        public sealed class MatchResultsData
        {
            public bool SharedVictory;
            public List<ResultEntry> Entries = new();
        }

        /// <summary>
        /// Apply a <c>MatchStarted</c> push: stash the match config (arena, port,
        /// roster, classes, entity IDs) and load the PvP arena scene. Shared by
        /// CharSelectController and StageSelectController — the host picks the
        /// arena on the stage select screen, so the push arrives while both
        /// clients are on StageSelect (issue: multiplayer stage select).
        /// </summary>
        public static void ApplyMatchStarted(Shared.MatchStartedConfig config)
        {
            // Find the local player in the roster (by SteamId). The master
            // server assigned entity IDs 1..N by join order (issue #35); the
            // game server spawns each with the roster's character class, so
            // every client renders the right chars (issue #36).
            Shared.LobbyPlayerInfo? local = null;
            foreach (var p in config.Players)
            {
                if (p.SteamId == SteamId)
                    local = p;
            }

            if (local == null)
            {
                UnityEngine.Debug.LogError("[PvP] Match started but local player missing from roster.");
                UnityEngine.SceneManagement.SceneManager.LoadScene("ServerBrowser");
                return;
            }

            UI.MatchConfig.Mode = UI.GameMode.PvP;
            UI.MatchConfig.ArenaName = string.IsNullOrEmpty(config.ArenaName) ? "slop_court" : config.ArenaName;
            UI.MatchConfig.ServerPort = config.MatchPort > 0 ? config.MatchPort : UI.MatchConfig.ServerPort;
            // ServerIP is already set (host: localhost, joiner: server browser IP).
            UI.MatchConfig.PlayerClass = ParseClass(local.CharacterSelection, Shared.CharacterClass.Manki);
            UI.MatchConfig.LocalEntityId = (ulong)(local.EntityId > 0 ? local.EntityId : 1);
            // Codec guarantees [1,99] (default 3); assign directly so a stale value
            // from a previous match can never leak through (issue #38).
            UI.MatchConfig.MaxStocks = config.MaxStocks;
            // Every non-local rostered player is an opponent (issue #36).
            // entityId <= 0 means the master never assigned it, so the game
            // server never spawned the entity — skip it.
            UI.MatchConfig.Opponents.Clear();
            foreach (var p in config.Players)
            {
                if (p.SteamId == SteamId) continue;
                if (p.EntityId <= 0) continue;
                UI.MatchConfig.Opponents.Add(new UI.MatchConfig.OpponentInfo(
                    (ulong)p.EntityId,
                    ParseClass(p.CharacterSelection, Shared.CharacterClass.Manki)));
            }

            // Stash the roster so the results screen can render names/classes.
            MatchRoster = config.Players;

            // Go straight to the PvP arena — the master server already launched
            // the game server and assigned the UDP port (issue #35).
            UnityEngine.SceneManagement.SceneManager.LoadScene("Arena_PvP");
        }

        private static Shared.CharacterClass ParseClass(string? name, Shared.CharacterClass fallback)
        {
            if (string.IsNullOrEmpty(name))
                return fallback;
            return System.Enum.TryParse<Shared.CharacterClass>(name, ignoreCase: true, out var c) && c != Shared.CharacterClass.None
                ? c
                : fallback;
        }

        public static void Reset()
        {
            MasterServerUrl = "https://sloparena.barakaslurp.fr";
            AuthToken = null;
            SteamId = 0;
            Username = null;
            SelectedServerId = Guid.Empty;
            SelectedServerName = string.Empty;
            ActiveLobby = null;
            LobbyRoster = null;
            MatchRoster = null;
            CurrentMatchResults = null;
            IsLobbyHost = false;
        }
    }
}
