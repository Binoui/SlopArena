#nullable enable
using System;

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
        /// <summary>Master server base URL (e.g. http://localhost:5000).</summary>
        public static string MasterServerUrl = "http://localhost:5000";

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

        public static void Reset()
        {
            MasterServerUrl = "http://localhost:5000";
            AuthToken = null;
            SteamId = 0;
            Username = null;
            SelectedServerId = Guid.Empty;
            SelectedServerName = string.Empty;
            ActiveLobby = null;
            LobbyRoster = null;
        }
    }
}
