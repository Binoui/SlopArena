#nullable enable
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using SlopArena.Shared;

namespace SlopArena.Client.Network
{
    /// <summary>
    /// Client-side connection to the master server's SignalR <c>LobbyHub</c>
    /// (ADR-0004, issue #33). Connects with the guest JWT as bearer auth,
    /// joins a lobby for a game server, and surfaces the hub's real-time pushes
    /// as plain C# events marshalled onto the Unity main thread via <see cref="Pump"/>.
    ///
    /// Server → client pushes: <c>PlayerJoined</c>, <c>PlayerLeft</c>,
    /// <c>LobbyUpdated</c>, <c>MatchStarting</c>, <c>StageSelect</c>, <c>MatchStarted</c>.
    /// Client → server: <see cref="JoinLobbyAsync"/>, <see cref="LeaveLobbyAsync"/>,
    /// <see cref="HostStartAsync"/>, <see cref="StartStageSelectAsync"/>, <see cref="StartMatchAsync"/>.
    /// </summary>
    public sealed class LobbyClient
    {
        private HubConnection? _conn;
        private readonly string _masterServerUrl;
        private readonly string _authToken;
        // Server the caller last asked to join; re-joined after an automatic
        // reconnect because SignalR group membership is per-connection-id and
        // does not survive a transport drop.
        private Guid _joinedServerId;
        // Actions queued from background SignalR threads; drained on the main
        // thread by the owner MonoBehaviour's Update → Pump().
        private readonly ConcurrentQueue<Action> _pending = new();

        /// <summary>True while the hub connection is open.</summary>
        public bool IsConnected => _conn != null && _conn.State == HubConnectionState.Connected;

        // ── Real-time lobby events (raised on the main thread, after Pump) ──

        /// <summary>A player joined the lobby.</summary>
        public event Action<LobbyPlayerInfo>? PlayerJoined;
        /// <summary>A player left the lobby (arg = their SteamId).</summary>
        public event Action<long>? PlayerLeft;
        /// <summary>Full lobby snapshot pushed on any membership change.</summary>
        public event Action<LobbySnapshot>? LobbyUpdated;
        /// <summary>The host started the match; clients should go to char-select.</summary>
        public event Action<MatchStartingConfig>? MatchStarting;
        /// <summary>All locked in; the host moved everyone to stage select.</summary>
        public event Action<MatchStartingConfig>? StageSelect;
        /// <summary>A player locked in / changed their character (issue #34).</summary>
        public event Action<LobbyPlayerInfo>? CharacterSelected;
        /// <summary>The host started the actual match; clients should connect to the game server (issue #34).</summary>
        public event Action<MatchStartedConfig>? MatchStarted;
        /// <summary>The hub connection opened (or reopened after a retry).</summary>
        public event Action? Connected;
        /// <summary>The hub connection closed. Arg is null on a clean close.</summary>
        public event Action<Exception?>? Disconnected;
        /// <summary>A non-fatal error (e.g. a hub method rejected the call).</summary>
        public event Action<string>? Error;

        /// <param name="masterServerUrl">Master server base URL (e.g. http://localhost:5000).</param>
        /// <param name="authToken">Guest JWT to send as bearer auth on the connection.</param>
        public LobbyClient(string masterServerUrl, string authToken)
        {
            _masterServerUrl = masterServerUrl.TrimEnd('/');
            _authToken = authToken;
        }

        /// <summary>
        /// Build the connection, register handlers, and start it. Returns false
        /// (without throwing) on a connection failure.
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (_conn != null)
                return IsConnected;

            _conn = new HubConnectionBuilder()
                .WithUrl($"{_masterServerUrl}/lobby", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(_authToken);
                    // LongPolling only: Unity Mono's ClientWebSocket is unreliable in
                    // standalone players (hub connect fails silently under Proton/Wine
                    // while plain HttpClient calls work). LongPolling is plain HTTP and
                    // works on every platform. Revisit if a native WebSocket impl lands.
                    options.Transports = HttpTransportType.LongPolling;
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHandlers();

            _conn.Closed += ex =>
            {
                _pending.Enqueue(() => Disconnected?.Invoke(ex));
                return Task.CompletedTask;
            };
            _conn.Reconnecting += ex =>
            {
                _pending.Enqueue(() => Disconnected?.Invoke(ex));
                return Task.CompletedTask;
            };
            _conn.Reconnected += _connectionId =>
            {
                // Re-add ourselves to the lobby group after a reconnect; the
                // new connection id is not in the old group. Fire-and-forget —
                // errors surface via the Error event in InvokeSafe.
                var serverId = _joinedServerId;
                if (serverId != Guid.Empty)
                    _ = _conn.InvokeAsync("JoinLobby", serverId);
                _pending.Enqueue(() => Connected?.Invoke());
                return Task.CompletedTask;
            };

            try
            {
                await _conn.StartAsync();
                _pending.Enqueue(() => Connected?.Invoke());
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[LobbyClient] SignalR connect failed: {ex}");
                _pending.Enqueue(() => Error?.Invoke($"Failed to connect: {ex.Message}"));
                return false;
            }
        }

        private void RegisterHandlers()
        {
            // PlayerJoined: { steamId, name, characterSelection, isHost }
            _conn!.On<JsonElement>("PlayerJoined", element =>
            {
                var player = LobbyPayloadCodec.TryParsePlayer(element);
                if (player is null) return;
                _pending.Enqueue(() => PlayerJoined?.Invoke(player));
            });

            // PlayerLeft: a bare long (the leaving player's SteamId)
            _conn.On<long>("PlayerLeft", steamId =>
            {
                _pending.Enqueue(() => PlayerLeft?.Invoke(steamId));
            });

            // LobbyUpdated / MatchStarting: { serverId, players[] }
            _conn.On<JsonElement>("LobbyUpdated", element =>
            {
                var snap = LobbyPayloadCodec.TryParseSnapshot(element);
                if (snap is null) return;
                _pending.Enqueue(() => LobbyUpdated?.Invoke(snap));
            });
            _conn.On<JsonElement>("MatchStarting", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarting(element);
                if (cfg is null) return;
                _pending.Enqueue(() => MatchStarting?.Invoke(cfg));
            });
            // StageSelect carries the same { serverId, players[] } shape as MatchStarting.
            _conn.On<JsonElement>("StageSelect", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarting(element);
                if (cfg is null) return;
                _pending.Enqueue(() => StageSelect?.Invoke(cfg));
            });
            _conn.On<JsonElement>("CharacterSelected", element =>
            {
                var player = LobbyPayloadCodec.TryParsePlayer(element);
                if (player is null) return;
                _pending.Enqueue(() => CharacterSelected?.Invoke(player));
            });
            _conn.On<JsonElement>("MatchStarted", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarted(element);
                if (cfg is null) return;
                _pending.Enqueue(() => MatchStarted?.Invoke(cfg));
            });
        }

        /// <summary>Join the lobby for the given game server.</summary>
        public Task JoinLobbyAsync(Guid serverId)
        {
            _joinedServerId = serverId;
            return InvokeSafe("JoinLobby", serverId);
        }

        /// <summary>Leave the current lobby.</summary>
        public Task LeaveLobbyAsync()
        {
            _joinedServerId = Guid.Empty;
            return InvokeSafe("LeaveLobby");
        }

        /// <summary>Host-only: start the match for this lobby.</summary>
        public Task HostStartAsync() =>
            InvokeSafe("HostStart");

        /// <summary>Lock in a character selection (issue #34). Can be called again to change pick.</summary>
        public Task SelectCharacterAsync(string characterClass) =>
            InvokeSafe("SelectCharacter", characterClass);

        /// <summary>
        /// Host-only: move everyone from char select to stage select. Requires
        /// all players locked in; the host picks the arena there, then calls
        /// <see cref="StartMatchAsync"/>.
        /// </summary>
        public Task StartStageSelectAsync() =>
            InvokeSafe("StartStageSelect");

        /// <summary>Host-only: start the actual match on the given arena (issue #34). Requires all locked in.</summary>
        public Task StartMatchAsync(string arenaName) =>
            InvokeSafe("StartMatch", arenaName);

        private async Task InvokeSafe(string method, params object[] args)
        {
            if (_conn is null || !IsConnected)
            {
                _pending.Enqueue(() => Error?.Invoke($"Not connected; cannot {method}."));
                return;
            }
            try
            {
                // Use the object[]-taking overload: InvokeAsync(string, object? arg1, …)
                // would bind args as a SINGLE argument (the array), serializing
                // arguments:[[…]] which fails server-side binding. InvokeCoreAsync
                // passes the array straight through (observed on the wire, fixed 2026-08-04).
                await _conn.InvokeCoreAsync(method, args);
            }
            catch (Exception ex)
            {
                // HubException surfaces here (e.g. non-host HostStart rejected).
                UnityEngine.Debug.LogError($"[LobbyClient] {method} rejected: {ex}");
                _pending.Enqueue(() => Error?.Invoke($"{method} rejected: {ex.Message}"));
            }
        }

        /// <summary>
        /// Drain queued hub events onto the calling (main) thread. The owning
        /// MonoBehaviour MUST call this from Update so the UI events fire there.
        /// </summary>
        public void Pump()
        {
            while (_pending.TryDequeue(out var action))
                action();
        }

        /// <summary>Stop the connection if open. Safe to call from OnDisable.</summary>
        public async Task DisconnectAsync()
        {
            if (_conn != null)
            {
                try { await _conn.StopAsync(); }
                catch { /* best-effort shutdown */ }
                _conn = null;
            }
        }
    }
}
