#nullable enable
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SlopArena.Shared;

namespace SlopArena.Client.Network
{
    public sealed class MatchAbortedNotification
    {
        public Guid MatchId { get; }
        public string Reason { get; }
        public MatchAbortedNotification(Guid matchId, string reason)
        {
            MatchId = matchId;
            Reason = reason;
        }
    }

    /// <summary>
    /// One authenticated SignalR connection to the master server. It carries
    /// lobby control and all chat channels; callers keep this instance alive
    /// across scene changes and only the persistent ChatSession pumps it.
    /// </summary>
    public sealed class LobbyClient
    {
        private const int MaxPendingActions = 1024;
        private HubConnection? _conn;
        private readonly string _masterServerUrl;
        private readonly Func<string?> _authTokenProvider;
        private readonly int _protocolVersion;
        private Guid _activeMatchId;
        private Guid _joinedServerId;
        private readonly ConcurrentQueue<Action> _pending = new();
        private int _pendingCount;
        private int _overflowed;

        /// <summary>True while the hub connection is open.</summary>
        public bool IsConnected => _conn != null && _conn.State == HubConnectionState.Connected;

        private bool _resumeServerOnly;
        private bool _leavePending;
        /// <summary>Server target retained for reconnect revalidation.</summary>
        public Guid JoinedServerId => _joinedServerId;

        /// <summary>True if background pushes exceeded the bounded main-thread queue.</summary>
        public bool HasPendingOverflow => Volatile.Read(ref _overflowed) != 0;
        public void ClearPendingOverflow() => Interlocked.Exchange(ref _overflowed, 0);

        // ── Real-time lobby events (raised on the main thread, after Pump) ──
        public event Action<LobbyPlayerInfo>? PlayerJoined;
        public event Action<long>? PlayerLeft;
        public event Action<LobbySnapshot>? LobbyUpdated;
        public event Action<MatchStartingConfig>? MatchStarting;
        public event Action<MatchStartingConfig>? StageSelect;
        public event Action<LobbyPlayerInfo>? CharacterSelected;
        public event Action<MatchStartedConfig>? MatchStarted;
        public event Action? MatchStartedRejected;
        public event Action<MatchAbortedNotification>? MatchAborted;
        public event Action? Connected;
        public event Action<Exception?>? Disconnected;
        public event Action<string>? Error;

        // ── Chat pushes (raised on the main thread, after Pump) ──
        public event Action<ChatMessage>? ChatMessageReceived;
        public event Action<ChatPresence>? ChatPresenceChanged;
        public event Action<ServerChatState>? ChatServerChanged;
        public event Action<ChatSnapshot>? ChatStateReceived;
        public event Action<ChatPlayer[]>? OnlinePlayersReceived;

        /// <param name="masterServerUrl">Master server base URL (e.g. http://localhost:5000).</param>
        /// <param name="authToken">Guest JWT to send as bearer auth on the connection.</param>
        /// <param name="protocolVersion">2 for Steam-authenticated play; 0 for explicit Editor development guests.</param>
        public LobbyClient(string masterServerUrl, string authToken)
            : this(masterServerUrl, () => authToken, SteamMatchDescriptor.CurrentProtocolVersion)
        {
        }

        /// <summary>Construct with a live token provider so renewal updates the hub credential.</summary>
        public LobbyClient(string masterServerUrl, Func<string?> authTokenProvider, int protocolVersion = SteamMatchDescriptor.CurrentProtocolVersion)
        {
            if (protocolVersion != SteamMatchDescriptor.CurrentProtocolVersion &&
                !(protocolVersion == 0 && UnityEngine.Application.isEditor))
                throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            _masterServerUrl = masterServerUrl.TrimEnd('/');
            _authTokenProvider = authTokenProvider ?? throw new ArgumentNullException(nameof(authTokenProvider));
            _protocolVersion = protocolVersion;
        }

        /// <summary>
        /// Build the connection, register handlers, and start it. Returns false
        /// (without throwing) on a connection failure.
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (IsConnected) return true;
            if (_conn?.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting)
                return false;
            if (string.IsNullOrEmpty(_authTokenProvider()))
            {
                Enqueue(() => Error?.Invoke("Not authenticated; cannot connect to the room directory."));
                return false;
            }
            if (_conn == null)
            {
                _conn = new HubConnectionBuilder()
                    .WithUrl($"{_masterServerUrl}/lobby", options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_authTokenProvider());
                        // Unity Mono/Proton requires the existing long-poll transport.
                        options.Transports = HttpTransportType.LongPolling;
                    })
                    .WithAutomaticReconnect()
                    .Build();
                RegisterHandlers();
                _conn.Closed += ex =>
                {
                    Enqueue(() => Disconnected?.Invoke(ex));
                    return Task.CompletedTask;
                };
                _conn.Reconnecting += ex =>
                {
                    Enqueue(() => Disconnected?.Invoke(ex));
                    return Task.CompletedTask;
                };
                _conn.Reconnected += async _ =>
                {
                    await RestoreMembershipAsync();
                    Enqueue(() => Connected?.Invoke());
                };
            }
            try
            {
                await _conn.StartAsync();
                await RestoreMembershipAsync();
                Enqueue(() => Connected?.Invoke());
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] SignalR connect failed: {ex.Message}");
                Enqueue(() => Error?.Invoke($"Failed to connect: {ex.Message}"));
                return false;
            }
        }

        private async Task RestoreMembershipAsync()
        {
            if (_leavePending)
            {
                await LeaveLobbyAsync();
                return;
            }
            if (_joinedServerId == Guid.Empty) return;
            try
            {
                await _conn!.InvokeCoreAsync(
                    _resumeServerOnly ? "ResumeServer" : "JoinLobby",
                    new object?[] { _joinedServerId, _protocolVersion });
            }
            catch (Exception ex)
            {
                Enqueue(() => Error?.Invoke($"Server membership could not be revalidated: {ex.Message}"));
            }
        }

        private void RegisterHandlers()
        {
            _conn!.On<JsonElement>("PlayerJoined", element =>
            {
                var player = LobbyPayloadCodec.TryParsePlayer(element);
                if (player is not null) Enqueue(() => PlayerJoined?.Invoke(player));
            });
            _conn.On<long>("PlayerLeft", steamId => Enqueue(() => PlayerLeft?.Invoke(steamId)));
            _conn.On<JsonElement>("LobbyUpdated", element =>
            {
                var snap = LobbyPayloadCodec.TryParseSnapshot(element);
                if (snap is not null) Enqueue(() => LobbyUpdated?.Invoke(snap));
            });
            _conn.On<JsonElement>("MatchStarting", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarting(element);
                if (cfg is not null) Enqueue(() => MatchStarting?.Invoke(cfg));
            });
            _conn.On<JsonElement>("StageSelect", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarting(element);
                if (cfg is not null) Enqueue(() => StageSelect?.Invoke(cfg));
            });
            _conn.On<JsonElement>("CharacterSelected", element =>
            {
                var player = LobbyPayloadCodec.TryParsePlayer(element);
                if (player is not null) Enqueue(() => CharacterSelected?.Invoke(player));
            });
            _conn.On<JsonElement>("MatchStarted", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarted(element);
                if (cfg == null)
                {
                    Enqueue(() =>
                    {
                        _resumeServerOnly = false;
                        _activeMatchId = Guid.Empty;
                        MatchStartedRejected?.Invoke();
                    });
                    return;
                }
                _resumeServerOnly = true;
                Enqueue(() =>
                {
                    _activeMatchId = cfg.Descriptor?.MatchId ?? Guid.Empty;
                    MatchStarted?.Invoke(cfg);
                });
            });
            _conn.On<JsonElement>("MatchAborted", element =>
            {
                var aborted = TryParseMatchAborted(element);
                if (aborted == null) return;
                Enqueue(() =>
                {
                    if (_activeMatchId != aborted.MatchId)
                        return;
                    _activeMatchId = Guid.Empty;
                    _resumeServerOnly = false;
                    MatchAborted?.Invoke(aborted);
                });
            });
            _conn.On<JsonElement>("ChatMessage", element =>
            {
                var message = Deserialize<ChatMessage>(element);
                if (message is not null) Enqueue(() =>
                {
                    if (string.Equals(message.Channel, "server", StringComparison.OrdinalIgnoreCase)
                        && (_leavePending || message.ServerId != _joinedServerId))
                        return;
                    ChatMessageReceived?.Invoke(message);
                });
            });
            _conn.On<JsonElement>("ChatPresenceChanged", element =>
            {
                var presence = Deserialize<ChatPresence>(element);
                if (presence is not null) Enqueue(() => ChatPresenceChanged?.Invoke(presence));
            });
            _conn.On<JsonElement>("ChatServerChanged", element =>
            {
                var state = Deserialize<ServerChatState>(element);
                if (state is not null) Enqueue(() =>
                {
                    if (!_leavePending && state.ServerId == (_joinedServerId == Guid.Empty ? (Guid?)null : _joinedServerId))
                        ChatServerChanged?.Invoke(state);
                });
            });
        }

        /// <summary>Join the lobby for the given game server.</summary>
        public async Task JoinLobbyAsync(Guid serverId)
        {
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke("Not connected; cannot JoinLobby."));
                return;
            }
            if (_leavePending)
            {
                await LeaveLobbyAsync();
                if (_leavePending) return;
            }
            var previousServerId = _joinedServerId;
            bool previousResumeOnly = _resumeServerOnly;
            // An interrupted reply is not a rejected admission. Revalidate it
            // as chat-only on reconnect rather than allocating a waiting slot.
            _joinedServerId = serverId;
            _resumeServerOnly = true;
            try
            {
                await _conn.InvokeCoreAsync("JoinLobby", new object?[] { serverId, _protocolVersion });
                _resumeServerOnly = false;
            }
            catch (Exception ex)
            {
                if (ex is HubException && !ex.Message.Contains("lobby_full"))
                {
                    _joinedServerId = previousServerId;
                    _resumeServerOnly = previousResumeOnly;
                }
                UnityEngine.Debug.LogWarning($"[LobbyClient] JoinLobby rejected: {ex.Message}");
                Enqueue(() => Error?.Invoke($"JoinLobby rejected: {ex.Message}"));
            }
        }
        /// <summary>
        /// Revalidate an already admitted GameServer after reconnect without
        /// re-entering its waiting roster.
        /// </summary>
        public async Task ResumeServerAsync(Guid serverId)
        {
            if (_leavePending) return;
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke("Not connected; cannot ResumeServer."));
                return;
            }
            try
            {
                await _conn.InvokeCoreAsync("ResumeServer", new object?[] { serverId, _protocolVersion });
                _joinedServerId = serverId;
                _resumeServerOnly = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] ResumeServer rejected: {ex.Message}");
                Enqueue(() => Error?.Invoke($"ResumeServer rejected: {ex.Message}"));
            }
        }

        /// <summary>Leave the current lobby and revoke Server Chat membership.</summary>
        public async Task LeaveLobbyAsync()
        {
            _leavePending = true;
            _joinedServerId = Guid.Empty;
            _resumeServerOnly = false;
            Enqueue(() => ChatServerChanged?.Invoke(new ServerChatState()));
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke("Server leave will complete after reconnect."));
                return;
            }
            try
            {
                await _conn.InvokeCoreAsync("LeaveLobby", Array.Empty<object?>());
                _leavePending = false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] LeaveLobby not confirmed: {ex.Message}");
                Enqueue(() => Error?.Invoke($"LeaveLobby rejected: {ex.Message}"));
            }
        }

        public Task HostStartAsync() => InvokeSafe("HostStart");
        public Task SelectCharacterAsync(string characterClass) => InvokeSafe("SelectCharacter", characterClass);
        public Task StartStageSelectAsync() => InvokeSafe("StartStageSelect");
        public Task StartMatchAsync(string arenaName) => InvokeSafe("StartMatch", arenaName);

        public async Task<ChatSnapshot?> GetChatStateAsync()
        {
            var snapshot = await InvokeChatAsync<ChatSnapshot>("GetChatState");
            if (_leavePending && snapshot != null)
                snapshot.Server = new ServerChatState();
            return snapshot;
        }
        public Task<ChatPlayer[]?> GetOnlinePlayersAsync() => InvokeChatAsync<ChatPlayer[]>("GetOnlinePlayers");
        public Task<ChatMessage?> SendGlobalAsync(string text) => SendChatAsync("SendGlobal", text);
        public Task<ChatMessage?> SendServerAsync(Guid serverId, string text) =>
            _leavePending || serverId != _joinedServerId
                ? Task.FromException<ChatMessage?>(new InvalidOperationException("not_joined"))
                : SendChatAsync("SendServer", serverId, text);
        public Task<ChatMessage?> SendDirectAsync(string playerId, string text) =>
            SendChatAsync("SendDirect", playerId, text);

        private Task<ChatMessage?> SendChatAsync(string method, params object?[] args)
        {
            if (_conn is null || !IsConnected)
                return Task.FromException<ChatMessage?>(new InvalidOperationException("not_connected"));
            return _conn.InvokeCoreAsync<ChatMessage?>(method, args);
        }

        private async Task InvokeSafe(string method, params object?[] args)
        {
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke($"Not connected; cannot {method}."));
                return;
            }
            try
            {
                await _conn.InvokeCoreAsync(method, args);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[LobbyClient] {method} rejected: {ex}");
                Enqueue(() => Error?.Invoke($"{method} rejected: {ex.Message}"));
            }
        }

        private async Task<T?> InvokeChatAsync<T>(string method, params object?[] args)
        {
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke($"not_connected: cannot {method}."));
                return default;
            }
            try
            {
                return await _conn.InvokeCoreAsync<T>(method, args);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] {method} rejected: {ex.Message}");
                Enqueue(() => Error?.Invoke($"{method} rejected: {ex.Message}"));
                return default;
            }
        }

        private static T? Deserialize<T>(JsonElement element)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions);
            }
            catch
            {
                return default;
            }
        }
        private static MatchAbortedNotification? TryParseMatchAborted(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("matchId", out var match) || match.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(match.GetString(), out var matchId) || matchId == Guid.Empty ||
                !element.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String)
                return null;

            string? value = reason.GetString();
            return value is "unfilled" or "absent" or "host_restart" or "host_shutdown" or "content_unavailable"
                ? new MatchAbortedNotification(matchId, value)
                : null;
        }


        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private void Enqueue(Action action)
        {
            while (true)
            {
                int count = Volatile.Read(ref _pendingCount);
                if (count >= MaxPendingActions)
                {
                    Interlocked.Exchange(ref _overflowed, 1);
                    return;
                }
                if (Interlocked.CompareExchange(ref _pendingCount, count + 1, count) == count)
                    break;
            }
            _pending.Enqueue(action);
        }

        /// <summary>Drain queued hub events onto the calling (main) thread.</summary>
        public void Pump(int maxActions = MaxPendingActions)
        {
            if (maxActions < 1) return;
            int pumped = 0;
            while (pumped++ < maxActions && _pending.TryDequeue(out var action))
            {
                try { action(); }
                finally { Interlocked.Decrement(ref _pendingCount); }
            }
        }

        /// <summary>Stop the connection if open. Safe to call during application shutdown.</summary>
        public async Task DisconnectAsync()
        {
            if (_conn != null)
            {
                try { await _conn.DisposeAsync(); }
                catch { }
                _conn = null;
            }
        }
    }
}
