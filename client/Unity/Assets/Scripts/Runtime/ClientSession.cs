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
        }
    }
}
