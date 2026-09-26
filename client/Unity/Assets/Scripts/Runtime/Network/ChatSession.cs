#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using SlopArena.Client;
using SlopArena.Shared;
using Steamworks;

namespace SlopArena.Client.Network
{
    /// <summary>
    /// Game-wide owner for the authenticated guest, SignalR transport, chat
    /// store, reconnect/renewal lifecycle, and main-thread delivery. A single
    /// instance survives scene changes; screens never create chat identities or
    /// own the lobby connection.
    /// </summary>
    public sealed class ChatSession : MonoBehaviour
    {
        public const string DisplayNamePlayerPrefsKey = "SlopArena.Chat.DisplayName";
        public const int MaxConversations = 32;
        public const int MaxMessagesPerConversation = 50;
        public const int MaxMutedPlayers = 256;

        private const float InitialRetrySeconds = 2f;
        private const float MaxRetrySeconds = 30f;
        private const int MaxAutomaticRetries = 5;
        private const float TokenRenewalLeadSeconds = 60f;
        private const string SteamBackendIdentity = "sloparena-playtest";
        private const int SteamTicketTimeoutSeconds = 15;

        private static ChatSession? _instance;
        private readonly List<ChatConversation> _conversations = new();
        private readonly List<ChatPlayer> _onlinePlayers = new();
        private readonly HashSet<string> _mutedPlayerIds = new(StringComparer.Ordinal);
        private readonly object _taskSync = new();
        private MasterServerClient? _masterClient;
        private readonly Dictionary<string, ChatPlayer> _mutedProfiles = new(StringComparer.Ordinal);
        private LobbyClient? _lobby;
        private Task<bool>? _connectTask;
        private Task<bool>? _authTask;
        private Task<bool>? _renewTask;
        private Callback<GetTicketForWebApiResponse_t>? _ticketCallback;
        private TaskCompletionSource<string?>? _pendingTicket;
        private HAuthTicket _ticketHandle;
        private bool _ownsSteamApi;
        private bool _developmentGuest;
        private ulong _authenticatedSteamId;
        private int _accountGeneration;
        private string _masterServerUrl = ClientSession.DefaultMasterServerUrl;
        private string _savedDisplayName = string.Empty;
        private string _status = "Offline";
        private string? _selfPlayerId;
        private ChatConversation? _activeConversation;
        private bool _authAttempted;
        private bool _nameApplied;
        private bool _savedNameRejected;
        private int _nameGeneration;
        private bool _directoryAvailable;
        private bool _chatStateReady;
        private bool _destroyed;
        private float _nextRetryAt;
        private float _nextRenewalAt;
        private float _retryDelay = InitialRetrySeconds;
        // Per-launch budget: never reset on success, so a flapping connection cannot retry forever.
        private int _automaticRetries;
        private Guid? _joinedServerId;
        public static ChatSession? Instance => _instance;
        public event Action? Changed;

        /// <summary>
        /// Raised with the conversation key when the bounded history buffer
        /// (issue #214) trims its oldest message to stay at
        /// <see cref="MaxMessagesPerConversation"/>. Presentation uses this to
        /// return an evicted reading anchor to newest with an explanation.
        /// </summary>
        public event Action<string>? ConversationHistoryTrimmed;

        public ChatPlayer? Self { get; private set; }
        public bool IsConnected => _nameApplied && _chatStateReady && _lobby?.IsConnected == true;
        public bool NeedsDisplayName => string.IsNullOrEmpty(_savedDisplayName);
        public string SavedDisplayName => _savedDisplayName;
        public bool SavedNameRejected => _savedNameRejected;
        public bool DirectoryAvailable => _directoryAvailable;
        public string Status => _status;
        public Guid? JoinedServerId => _joinedServerId;
        public IReadOnlyList<ChatPlayer> OnlinePlayers => _onlinePlayers;
        public IReadOnlyList<ChatConversation> Conversations => _conversations;
        public ChatConversation? ActiveConversation => _activeConversation;
        public IReadOnlyCollection<ChatPlayer> MutedPlayers => _mutedProfiles.Values;
        public bool IsSending => _activeConversation?.IsSending == true;
        public MasterServerClient? MasterClient => _masterClient;
        public LobbyClient? ActiveLobby => _lobby;
        public string? AuthToken => _masterClient?.Token;
        public long? SteamId => _masterClient?.SteamId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null || SteamProbeClient.IsRequested())
                return;
            if (!Application.isEditor)
                ClientSession.MasterServerUrl = ClientSession.DefaultMasterServerUrl;
            // Guest access is an explicit Editor-only development choice, never a packaged fallback.
            // Set SLOPARENA_DEV_GUEST=1 before Editor launch for a local guest session.
            var overrideUrl = Application.isEditor
                ? Environment.GetEnvironmentVariable("SLOPARENA_MASTER_URL")
                : null;
            if (overrideUrl != null)
            {
                if (!Uri.TryCreate(overrideUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
                    uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                    throw new InvalidOperationException("SLOPARENA_MASTER_URL must be an HTTPS origin without credentials, path, query, or fragment.");
                ClientSession.MasterServerUrl = uri.GetLeftPart(UriPartial.Authority);
            }
            var guestFlag = Application.isEditor &&
                Environment.GetEnvironmentVariable("SLOPARENA_DEV_GUEST") == "1";
            var go = new GameObject(nameof(ChatSession));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ChatSession>();
            _instance._developmentGuest = guestFlag;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _masterServerUrl = ClientSession.MasterServerUrl;
            _savedDisplayName = PlayerPrefs.GetString(DisplayNamePlayerPrefsKey, string.Empty).Trim();
            _activeConversation = GetOrCreateConversation("global", "GLOBAL")!;
            _status = NeedsDisplayName ? "Choose a display name to connect." : "Connecting…";
        }

        private void Start()
        {
            if (!_developmentGuest)
            {
                try
                {
                    if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
                    {
                        SetStatus("Steam is unavailable. Local play remains available.");
                        return;
                    }
                    _ownsSteamApi = true;
                    _ticketCallback = Callback<GetTicketForWebApiResponse_t>.Create(OnWebTicket);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Steam initialization failed: {ex.GetType().Name}");
                    SetStatus("Steam is unavailable. Local play remains available.");
                    return;
                }
            }
            if (!NeedsDisplayName)
                _ = EnsureConnectedAsync();
        }

        private void Update()
        {
            if (_destroyed)
                return;
            _lobby?.Pump(256);
            if (_ownsSteamApi)
            {
                SteamAPI.RunCallbacks();
                if (_authenticatedSteamId != 0 &&
                    (!SteamUser.BLoggedOn() || SteamUser.GetSteamID().m_SteamID != _authenticatedSteamId))
                    ClearAccountSession();
            }
            if (_lobby?.HasPendingOverflow == true)
            {
                _lobby.ClearPendingOverflow();
                _chatStateReady = false;
                SetStatus(_automaticRetries >= MaxAutomaticRetries
                    ? "CHAT OVERFLOW — RETRY PAUSED"
                    : "Chat event backlog overflowed; resyncing from Master.");
            }

            if ((_automaticRetries < MaxAutomaticRetries || IsConnected) &&
                _masterClient?.TokenExpiresAt is DateTimeOffset expires &&
                DateTimeOffset.UtcNow >= expires.AddSeconds(-TokenRenewalLeadSeconds))
            {
                if (Time.unscaledTime >= _nextRenewalAt && (_renewTask == null || _renewTask.IsCompleted))
                {
                    _nextRenewalAt = Time.unscaledTime + MaxRetrySeconds;
                    _renewTask = RenewTokenAsync();
                }
            }

            if (!NeedsDisplayName && !_savedNameRejected && !IsConnected && _connectTask == null &&
                _automaticRetries < MaxAutomaticRetries &&
                Time.unscaledTime >= _nextRetryAt && _authAttempted)
            {
                _automaticRetries++;
                _nextRetryAt = Time.unscaledTime + _retryDelay;
                _retryDelay = Math.Min(MaxRetrySeconds, _retryDelay * 2f);
                _ = EnsureConnectedAsync();
            }
        }

        /// <summary>Set the endpoint before the first auth attempt.</summary>
        public static void ConfigureMasterServerUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            var normalized = url.TrimEnd('/');
            if (!Application.isEditor &&
                !string.Equals(normalized, ClientSession.DefaultMasterServerUrl, StringComparison.OrdinalIgnoreCase))
                return;
            if (_instance != null &&
                _instance._authAttempted &&
                !string.Equals(_instance._masterServerUrl, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _instance.SetStatus("The launch session cannot switch Master servers after authentication starts.");
                return;
            }
            ClientSession.MasterServerUrl = normalized;
            if (_instance == null)
                return;
            if (string.Equals(_instance._masterServerUrl, normalized, StringComparison.OrdinalIgnoreCase))
                return;
            _instance._masterServerUrl = normalized;
            if (!_instance._authAttempted)
            {
                _instance._masterClient?.Dispose();
                _instance._masterClient = null;
            }
        }

        public async Task<bool> EnsureConnectedAsync()
        {
            if (_destroyed || NeedsDisplayName)
            {
                SetStatus("Choose a display name to connect.");
                return false;
            }
            if (IsConnected && _nameApplied)
                return true;
            Task<bool>? task;
            lock (_taskSync)
            {
                if (_connectTask == null)
                {
                    _nextRetryAt = Math.Max(_nextRetryAt, Time.unscaledTime + InitialRetrySeconds);
                    _connectTask = ConnectCoreAsync();
                }
                task = _connectTask;
            }
            try
            {
                bool connected = await task;
                if (!connected && _automaticRetries >= MaxAutomaticRetries && !_destroyed)
                    SetStatus("RETRY PAUSED");
                return connected;
            }
            finally
            {
                lock (_taskSync)
                {
                    if (ReferenceEquals(_connectTask, task) && task.IsCompleted)
                        _connectTask = null;
                }
            }
        }

        public async Task<bool> SetDisplayNameAsync(string name)
        {
            if (!ChatTextValidation.TryValidateDisplayName(name, out var normalized, out var validationError))
            {
                SetStatus(validationError);
                return false;
            }
            EnsureMasterClient();
            if (!await EnsureAuthenticatedAsync())
                return false;
            var profile = await _masterClient!.SetDisplayNameAsync(normalized);
            if (profile == null)
            {
                SetStatus(_masterClient.LastStatusCode == 409
                    ? "You cannot rename while joined to a GameServer."
                    : "The Master rejected that display name. Try again.");
                return false;
            }
            ApplySelf(ToChatPlayer(profile));
            _savedDisplayName = normalized;
            // A rename accepted while an initial connect apply is still in
            // flight makes the apply loop re-apply this latest name. Keep
            // _nameApplied false so the trailing EnsureConnectedAsync runs
            // that versioned apply instead of trusting the connected
            // fast-path.
            _nameGeneration++;
            PlayerPrefs.SetString(DisplayNamePlayerPrefsKey, normalized);
            PlayerPrefs.Save();
            _nameApplied = false;
            SetStatus(IsConnected ? "Connected" : "Connecting…");
            return await EnsureConnectedAsync();
        }

        public Task<bool> RenameAsync(string name) => SetDisplayNameAsync(name);

        /// <summary>
        /// Validate and persist the display name locally without waiting for
        /// Master acceptance (issue #209). Solo and Training remain available
        /// offline; the saved name is applied remotely on the next connection,
        /// and a remote rejection is surfaced without claiming connection
        /// success.
        /// </summary>
        public async Task<bool> AcceptDisplayNameLocallyAsync(string name)
        {
            if (!ChatTextValidation.TryValidateDisplayName(name, out var normalized, out var validationError))
            {
                SetStatus(validationError);
                return false;
            }
            bool changed = !string.Equals(_savedDisplayName, normalized, StringComparison.Ordinal);
            _savedDisplayName = normalized;
            _savedNameRejected = false;
            if (changed)
            {
                _nameGeneration++;
                _nameApplied = false;
            }
            PlayerPrefs.SetString(DisplayNamePlayerPrefsKey, normalized);
            PlayerPrefs.Save();
            SetStatus(changed ? "Display name saved locally. Connecting…" : "Display name saved locally.");
            NotifyChanged();
            try
            {
                await EnsureConnectedAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Connection problem after saving your display name. Local play remains available.");
            }
            return true;
        }

        public async Task RefreshDirectoryAsync()
        {
            if (!await EnsureConnectedAsync())
                return;
            var online = await _lobby!.GetOnlinePlayersAsync();
            if (online == null)
            {
                _directoryAvailable = false;
                SetStatus("Online directory unavailable.");
                return;
            }
            ReplaceOnlinePlayers(online);
            _directoryAvailable = true;
            SetStatus("Connected");
            NotifyChanged();
        }

        public bool SelectConversation(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            var conversation = _conversations.FirstOrDefault(c => c.Key == key);
            if (conversation == null)
                return false;
            _activeConversation = conversation;
            // Read state is presentation-owned (issue #214): selecting alone
            // does not acknowledge; the overlay marks read only when the
            // selected conversation is visible, focused, unobscured and at
            // newest.
            RefreshConversationStates();
            NotifyChanged();
            return true;
        }

        public bool OpenDirect(ChatPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.PlayerId) ||
                string.Equals(player.PlayerId, _selfPlayerId, StringComparison.Ordinal))
                return false;
            var conversation = GetOrCreateConversation(
                DirectKey(player.PlayerId), FormatPlayerTitle(player));
            if (conversation == null)
                return false;
            conversation.Title = FormatPlayerTitle(player);
            _activeConversation = conversation;
            // Read state is presentation-owned (issue #214): selecting alone
            // does not acknowledge; the overlay marks read only when the
            // selected conversation is visible, focused, unobscured and at
            // newest.
            RefreshConversationStates();
            NotifyChanged();
            return true;
        }

        public void SetDraft(string text)
        {
            if (_activeConversation == null)
                return;
            _activeConversation.Draft = text ?? string.Empty;
            RefreshConversationStates();
            NotifyChanged();
        }

        public async Task SendDraftAsync()
        {
            var conversation = _activeConversation;
            if (conversation == null || conversation.IsSending)
                return;
            var key = conversation.Key;
            var text = conversation.Draft;
            if (!ChatTextValidation.TryValidateMessage(text, out var normalized, out var validationError))
            {
                conversation.Feedback = validationError;
                NotifyChanged();
                return;
            }
            if (!IsConnected)
            {
                conversation.Feedback = "Not sent while offline. Your draft was kept; send it when ready.";
                NotifyChanged();
                return;
            }
            if (!IsDestinationAvailable(key))
            {
                conversation.Feedback = key.StartsWith("direct:", StringComparison.Ordinal)
                    ? "That player is offline. The message was not queued."
                    : "That GameServer is no longer joined.";
                NotifyChanged();
                return;
            }

            conversation.IsSending = true;
            conversation.Feedback = "Sending…";
            RefreshConversationStates();
            NotifyChanged();
            ChatMessage? accepted = null;
            try
            {
                if (key == "global")
                    accepted = await _lobby!.SendGlobalAsync(normalized);
                else if (TryServerKey(key, out var serverId))
                    accepted = await _lobby!.SendServerAsync(serverId, normalized);
                else if (key.StartsWith("direct:", StringComparison.Ordinal))
                    accepted = await _lobby!.SendDirectAsync(key.Substring("direct:".Length), normalized);
                else
                    conversation.Feedback = "Unknown chat destination.";

                if (accepted != null)
                {
                    AddMessage(accepted);
                    if (conversation.Draft == text)
                        conversation.Draft = string.Empty;
                    conversation.Feedback = "Sent";
                }
                else if (!IsConnected)
                {
                    conversation.Feedback = "Send status is uncertain because the connection dropped. Your draft was kept.";
                }
                else
                {
                    conversation.Feedback = "Send was not confirmed. Your draft was kept.";
                }
            }
            catch (Microsoft.AspNetCore.SignalR.HubException ex)
            {
                string reason = ex.Message switch
                {
                    var message when message.Contains("rate_limited") => "Too many messages. Wait a few seconds before retrying.",
                    var message when message.Contains("recipient_offline") => "That player is offline. The message was not queued.",
                    var message when message.Contains("not_in_server") => "This GameServer is no longer joined.",
                    var message when message.Contains("not_connected") => "The connection was lost before the message was accepted.",
                    var message when message.Contains("chat_capacity") => "Chat is temporarily busy. Try again later.",
                    _ => "The Master rejected this message."
                };
                conversation.Feedback = reason + " Your draft was kept.";
            }
            catch (Exception)
            {
                conversation.Feedback = "Send status is uncertain. Your draft was kept and will not be resent automatically.";
            }
            finally
            {
                conversation.IsSending = false;
                RefreshConversationStates();
                NotifyChanged();
            }
        }

        public bool IsMuted(string playerId) =>
            !string.IsNullOrEmpty(playerId) && _mutedPlayerIds.Contains(playerId);

        public void SetMuted(string playerId, bool muted)
        {
            if (string.IsNullOrEmpty(playerId))
                return;
            if (muted)
            {
                if (!_mutedPlayerIds.Contains(playerId) && _mutedPlayerIds.Count >= MaxMutedPlayers)
                {
                    if (_activeConversation != null)
                        _activeConversation.Feedback = "Mute list is full.";
                    NotifyChanged();
                    return;
                }
                _mutedPlayerIds.Add(playerId);
                var profile = FindKnownPlayer(playerId);
                if (profile != null)
                    _mutedProfiles[playerId] = profile;
            }
            else
            {
                _mutedPlayerIds.Remove(playerId);
                _mutedProfiles.Remove(playerId);
            }
            NotifyChanged();
        }

        public void MarkActiveRead()
        {
            if (_activeConversation == null)
                return;
            _activeConversation.Unread = 0;
        }

        public bool IsMessageVisible(ChatMessage message) =>
            message != null && message.Sender != null && !IsMuted(message.Sender.PlayerId);

        private async Task<bool> ConnectCoreAsync()
        {
            int accountGeneration = _accountGeneration;
            EnsureMasterClient();
            if (!await EnsureAuthenticatedAsync() || accountGeneration != _accountGeneration)
                return false;
            if (!_nameApplied)
            {
                // Re-apply while a local save races this connect, so the latest
                // locally accepted name is the one the Master sees.
                while (true)
                {
                    int generation = _nameGeneration;
                    var profile = await _masterClient!.SetDisplayNameAsync(_savedDisplayName);
                    if (accountGeneration != _accountGeneration)
                        return false;
                    if (profile == null)
                    {
                        _nameApplied = false;
                        int? statusCode = _masterClient.LastStatusCode;
                        // A 4xx against an outdated generation says nothing about
                        // the latest local name; keep retrying that instead.
                        if (_nameGeneration == generation && statusCode is 400 or 409 or 422)
                        {
                            _savedNameRejected = true;
                            SetStatus("Master rejected your saved display name. Correct it in CHAT; local play remains available.");
                        }
                        else
                        {
                            SetStatus("Could not apply your saved display name yet. Local play remains available.");
                        }
                        return false;
                    }
                    ApplySelf(ToChatPlayer(profile));
                    if (_nameGeneration == generation)
                    {
                        _nameApplied = true;
                        break;
                    }
                }
            }

            if (_lobby == null)
            {
                _lobby = new LobbyClient(
                    _masterServerUrl,
                    () => _masterClient?.Token,
                    _developmentGuest ? 0 : SteamMatchDescriptor.CurrentProtocolVersion);
                SubscribeLobby(_lobby);
                ClientSession.ActiveLobby = _lobby;
            }
            var lobby = _lobby;
            if (!lobby.IsConnected && !await lobby.ConnectAsync())
            {
                if (accountGeneration != _accountGeneration)
                    return false;
                _chatStateReady = false;
                SetStatus("Master connection unavailable. Local play remains available.");
                return false;
            }
            if (accountGeneration != _accountGeneration)
                return false;
            var snapshot = await lobby.GetChatStateAsync();
            if (accountGeneration != _accountGeneration)
                return false;
            if (snapshot == null)
            {
                _chatStateReady = false;
                SetStatus("Master chat admission is unavailable. Retry when online.");
                return false;
            }
            ApplySnapshot(snapshot);
            _chatStateReady = true;
            _retryDelay = InitialRetrySeconds;
            _nextRetryAt = Time.unscaledTime + InitialRetrySeconds;
            SetStatus("Connected");
            await RefreshDirectoryCoreAsync();
            return true;
        }

        private async Task<bool> EnsureAuthenticatedAsync()
        {
            if (_masterClient!.IsAuthenticated)
                return true;
            _authAttempted = true;
            if (!_developmentGuest && !_ownsSteamApi)
            {
                SetStatus("Steam is unavailable. Local play remains available.");
                return false;
            }
            SetStatus(_developmentGuest ? "Signing in as a development guest…" : "Signing in with Steam…");
            Task<bool>? task;
            lock (_taskSync)
            {
                _authTask ??= AuthenticateCoreAsync();
                task = _authTask;
            }
            int generation = _accountGeneration;
            bool authenticated;
            try { authenticated = await task; }
            finally
            {
                lock (_taskSync)
                {
                    if (ReferenceEquals(_authTask, task) && task.IsCompleted) _authTask = null;
                }
            }
            if (generation != _accountGeneration)
                return false;
            if (!_developmentGuest && authenticated &&
                 (!SteamUser.BLoggedOn() ||
                  _masterClient?.SteamId != (long)SteamUser.GetSteamID().m_SteamID))
            {
                ClearAccountSession();
                return false;
            }
            if (!authenticated)
            {
                SetStatus("Could not authenticate with Master. Local play remains available.");
                return false;
            }
            _authenticatedSteamId = _developmentGuest ? 0 : SteamUser.GetSteamID().m_SteamID;
            ClientSession.AuthToken = _masterClient.Token;
            ClientSession.SteamId = _masterClient.SteamId ?? 0;
            return true;
        }

        private async Task<bool> AuthenticateCoreAsync()
        {
            if (_developmentGuest)
                return await _masterClient!.AuthenticateGuestAsync();
            var ticket = await RequestWebTicketAsync();
            try { return ticket != null && await _masterClient!.AuthenticateSteamAsync(ticket); }
            finally { CancelWebTicket(); }
        }

        private async Task<bool> RenewTokenAsync()
        {
            if (_masterClient == null || !_masterClient.IsAuthenticated)
                return false;
            var ticket = _developmentGuest ? null : await RequestWebTicketAsync();
            bool renewed;
            try
            {
                renewed = (_developmentGuest || ticket != null) &&
                    await _masterClient.RefreshAsync(ticket);
            }
            finally { CancelWebTicket(); }
            if (renewed)
            {
                ClientSession.AuthToken = _masterClient.Token;
                SetStatus(IsConnected ? "Connected" : _status);
                NotifyChanged();
                return true;
            }
            if (_masterClient.TokenExpiresAt is DateTimeOffset expires && expires <= DateTimeOffset.UtcNow)
            {
                _masterClient.ClearAuthentication();
                ClientSession.AuthToken = null;
                _chatStateReady = false;
                _nameApplied = false;
                ClientSession.ActiveLobby = null;
                _ = _lobby?.DisconnectAsync();
                _lobby = null;
                SetStatus("Authentication expired. Reconnect requires Steam.");
            }
            return false;
        }

        private async Task<string?> RequestWebTicketAsync()
        {
            if (!_ownsSteamApi || !SteamUser.BLoggedOn() || _pendingTicket != null)
                return null;
            _pendingTicket = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ticketHandle = SteamUser.GetAuthTicketForWebApi(SteamBackendIdentity);
            try
            {
                var completed = await Task.WhenAny(_pendingTicket.Task,
                    Task.Delay(TimeSpan.FromSeconds(SteamTicketTimeoutSeconds)));
                return completed == _pendingTicket.Task ? await _pendingTicket.Task : null;
            }
            finally
            {
                _pendingTicket = null;
            }
        }

        private void CancelWebTicket()
        {
            if (!_ownsSteamApi || _ticketHandle == HAuthTicket.Invalid)
                return;
            SteamUser.CancelAuthTicket(_ticketHandle);
            _ticketHandle = HAuthTicket.Invalid;
        }

        private void OnWebTicket(GetTicketForWebApiResponse_t response)
        {
            if (_pendingTicket == null || response.m_hAuthTicket != _ticketHandle)
                return;
            _pendingTicket.TrySetResult(response.m_eResult == EResult.k_EResultOK &&
                response.m_cubTicket > 0 && response.m_cubTicket <= response.m_rgubTicket.Length
                    ? BitConverter.ToString(response.m_rgubTicket, 0, response.m_cubTicket).Replace("-", "")
                    : null);
        }

        private void ClearAccountSession()
        {
            _accountGeneration++;
            _authenticatedSteamId = 0;
            _pendingTicket?.TrySetResult(null);
            CancelWebTicket();
            ClientSession.ClearActiveMatchForAccountChange();
            _masterClient?.ClearAuthentication();
            ClientSession.ClearAccountData();
            _ = _lobby?.DisconnectAsync();
            _lobby = null;
            _authTask = null;
            _renewTask = null;
            _nameApplied = false;
            _chatStateReady = false;
            _directoryAvailable = false;
            _joinedServerId = null;
            Self = null;
            _selfPlayerId = null;
            _onlinePlayers.Clear();
            _mutedProfiles.Clear();
            _mutedPlayerIds.Clear();
            _conversations.Clear();
            _activeConversation = GetOrCreateConversation("global", "GLOBAL");
            SetStatus("Steam account changed. Connecting with the current account…");
        }

        private void EnsureMasterClient()
        {
            if (_masterClient != null)
                return;
            _masterClient = new MasterServerClient(_masterServerUrl);
        }

        private async Task RefreshDirectoryCoreAsync()
        {
            var online = await _lobby!.GetOnlinePlayersAsync();
            if (online == null)
            {
                _directoryAvailable = false;
                return;
            }
            ReplaceOnlinePlayers(online);
            _directoryAvailable = true;
            RefreshConversationStates();
        }

        private void SubscribeLobby(LobbyClient lobby)
        {
            lobby.ChatMessageReceived += OnChatMessage;
            lobby.ChatPresenceChanged += OnPresence;
            lobby.ChatServerChanged += OnServerChanged;
            lobby.Connected += OnLobbyConnected;
            lobby.Disconnected += OnLobbyDisconnected;
            lobby.Error += OnLobbyError;
            lobby.MatchAborted += OnMatchAborted;
            lobby.MatchStartedRejected += OnMatchStartedRejected;
        }

        private void OnLobbyConnected()
        {
            if (_connectTask == null)
                _ = RefreshAfterReconnectAsync();
        }

        private async Task RefreshAfterReconnectAsync()
        {
            if (_lobby == null || !_lobby.IsConnected)
                return;
            var snapshot = await _lobby.GetChatStateAsync();
            if (snapshot != null)
            {
                ApplySnapshot(snapshot);
                _chatStateReady = true;
                SetStatus("Connected");
            }
            await RefreshDirectoryCoreAsync();
            NotifyChanged();
        }

        private void OnLobbyDisconnected(Exception? exception)
        {
            _chatStateReady = false;
            SetStatus(_automaticRetries >= MaxAutomaticRetries
                ? "RETRY PAUSED"
                : "Master connection dropped. Retrying without resending messages.");
            NotifyChanged();
        }

        private void OnLobbyError(string message)
        {
            if (message.IndexOf("not_connected", StringComparison.OrdinalIgnoreCase) >= 0)
                _chatStateReady = false;
            NotifyChanged();
        }

        private void OnChatMessage(ChatMessage message)
        {
            if (message == null || message.MessageId == Guid.Empty)
                return;
            AddMessage(message);
            NotifyChanged();
        }

        private void OnPresence(ChatPresence presence)
        {
            if (presence?.Player == null)
                return;
            int index = _onlinePlayers.FindIndex(p => p.PlayerId == presence.Player.PlayerId);
            if (presence.Online)
            {
                if (index < 0) _onlinePlayers.Add(presence.Player);
                else _onlinePlayers[index] = presence.Player;
                if (_mutedPlayerIds.Contains(presence.Player.PlayerId))
                    _mutedProfiles[presence.Player.PlayerId] = presence.Player;
                if (Self?.PlayerId == presence.Player.PlayerId)
                    Self = presence.Player;
            }
            else if (index >= 0)
            {
                _onlinePlayers.RemoveAt(index);
            }
            _directoryAvailable = true;
            RefreshConversationTitles();
            RefreshConversationStates();
            NotifyChanged();
        }

        private void OnServerChanged(ServerChatState state)
        {
            _joinedServerId = state?.ServerId;
            ClientSession.SelectedServerId = _joinedServerId ?? Guid.Empty;
            if (_joinedServerId is Guid serverId)
            {
                GetOrCreateConversation(ServerKey(serverId), ServerTitle(serverId));
                foreach (var message in state.Messages ?? Array.Empty<ChatMessage>())
                    AddMessage(message);
            }
            RefreshConversationStates();
            NotifyChanged();
        }
        private void OnMatchAborted(MatchAbortedNotification notification)
        {
            if (!ClientSession.ApplyMatchAborted(notification.MatchId, notification.Reason) &&
                ClientSession.SelectedServerId != Guid.Empty &&
                _lobby is { IsConnected: true } lobby &&
                lobby.JoinedServerId == ClientSession.SelectedServerId)
                _ = lobby.JoinLobbyAsync(ClientSession.SelectedServerId);
            SetStatus($"Match aborted ({notification.Reason}). Returned to the lobby.");
            NotifyChanged();
        }

        private void OnMatchStartedRejected()
        {
            ClientSession.RejectMatchStart("Master sent an invalid match descriptor or content map.");
            SetStatus("The Master rejected this match descriptor. Returned to the lobby.");
            NotifyChanged();
        }


        private void ApplySnapshot(ChatSnapshot snapshot)
        {
            if (snapshot?.Self != null)
                ApplySelf(snapshot.Self);
            var global = GetOrCreateConversation("global", "GLOBAL");
            if (global != null)
                foreach (var message in snapshot?.GlobalMessages ?? Array.Empty<ChatMessage>())
                    AddMessage(message);
            if (snapshot?.Server?.ServerId is Guid serverId)
            {
                _joinedServerId = serverId;
                var server = GetOrCreateConversation(ServerKey(serverId), ServerTitle(serverId));
                foreach (var message in snapshot.Server.Messages ?? Array.Empty<ChatMessage>())
                    AddMessage(message);
            }
            else
            {
                _joinedServerId = null;
            }
            RefreshConversationStates();
            NotifyChanged();
        }

        private void AddMessage(ChatMessage message)
        {
            if (message == null || message.MessageId == Guid.Empty)
                return;
            string key;
            string title;
            switch ((message.Channel ?? string.Empty).ToLowerInvariant())
            {
                case "global":
                    key = "global";
                    title = "GLOBAL";
                    break;
                case "server" when message.ServerId is Guid serverId:
                    key = ServerKey(serverId);
                    title = ServerTitle(serverId);
                    break;
                case "direct":
                    string? other = message.Sender?.PlayerId == _selfPlayerId
                        ? message.RecipientId
                        : message.Sender?.PlayerId;
                    if (string.IsNullOrEmpty(other)) return;
                    key = DirectKey(other);
                    title = ResolveDirectTitle(other, message.Sender);
                    break;
                default:
                    return;
            }
            var conversation = GetOrCreateConversation(key, title);
            if (conversation == null)
                return;
            conversation.Title = string.IsNullOrEmpty(conversation.Title) ? title : conversation.Title;
            if (conversation.MutableMessages.Any(existing => existing.MessageId == message.MessageId))
                return;
            conversation.MutableMessages.Add(message);
            bool trimmed = false;
            while (conversation.MutableMessages.Count > MaxMessagesPerConversation)
            {
                conversation.MutableMessages.RemoveAt(0);
                trimmed = true;
            }
            if (trimmed)
                ConversationHistoryTrimmed?.Invoke(conversation.Key);
            bool incoming = message.Sender?.PlayerId != _selfPlayerId;
            if (incoming && IsMessageVisible(message))
                conversation.Unread++;
        }

        private ChatConversation? GetOrCreateConversation(string key, string title)
        {
            var existing = _conversations.FirstOrDefault(c => c.Key == key);
            if (existing != null)
                return existing;
            if (_conversations.Count >= MaxConversations)
            {
                var evict = _conversations.FirstOrDefault(c =>
                    c.Key != _activeConversation?.Key && !c.IsSending && string.IsNullOrEmpty(c.Draft));
                if (evict == null)
                    return null;
                _conversations.Remove(evict);
            }
            var conversation = new ChatConversation(key, title);
            _conversations.Add(conversation);
            return conversation;
        }

        private void ReplaceOnlinePlayers(IEnumerable<ChatPlayer> players)
        {
            _onlinePlayers.Clear();
            foreach (var player in players ?? Array.Empty<ChatPlayer>())
            {
                if (player == null || string.IsNullOrEmpty(player.PlayerId) || player.PlayerId == _selfPlayerId)
                    continue;
                if (_onlinePlayers.All(existing => existing.PlayerId != player.PlayerId))
                    _onlinePlayers.Add(player);
                if (_mutedPlayerIds.Contains(player.PlayerId))
                    _mutedProfiles[player.PlayerId] = player;
            }
            RefreshConversationTitles();
        }

        private static ChatPlayer ToChatPlayer(GuestUserInfo profile) =>
            new ChatPlayer(
                string.IsNullOrEmpty(profile.PlayerId)
                    ? profile.SteamId.ToString(CultureInfo.InvariantCulture)
                    : profile.PlayerId,
                profile.Username,
                profile.SessionTag);

        private void ApplySelf(ChatPlayer player)
        {
            if (player == null)
                return;
            Self = player;
            _selfPlayerId = player.PlayerId;
            ClientSession.SteamId = long.TryParse(player.PlayerId, out var id) ? id : ClientSession.SteamId;
            ClientSession.Username = player.DisplayName;
            _nameApplied = true;
            _savedNameRejected = false;
            RefreshConversationStates();
        }

        private ChatPlayer? FindKnownPlayer(string playerId)
        {
            var online = _onlinePlayers.FirstOrDefault(player => player.PlayerId == playerId);
            if (online != null)
                return online;
            foreach (var conversation in _conversations)
                foreach (var message in conversation.Messages)
                {
                    if (message.Sender?.PlayerId == playerId)
                        return message.Sender;
                }
            return null;
        }

        private void RefreshConversationTitles()
        {
            foreach (var conversation in _conversations.Where(c => c.Key.StartsWith("direct:", StringComparison.Ordinal)))
            {
                var id = conversation.Key.Substring("direct:".Length);
                var player = _onlinePlayers.FirstOrDefault(p => p.PlayerId == id);
                if (player != null)
                    conversation.Title = FormatPlayerTitle(player);
            }
        }

        private void RefreshConversationStates()
        {
            foreach (var conversation in _conversations)
            {
                conversation.CanSend = IsConnected &&
                    ChatTextValidation.TryValidateMessage(conversation.Draft, out _, out _) &&
                    IsDestinationAvailable(conversation.Key);
            }
        }

        private bool IsDestinationAvailable(string key)
        {
            if (key == "global") return true;
            if (TryServerKey(key, out var serverId)) return _joinedServerId == serverId;
            if (key.StartsWith("direct:", StringComparison.Ordinal))
            {
                if (!_directoryAvailable) return false;
                var id = key.Substring("direct:".Length);
                return _onlinePlayers.Any(player => player.PlayerId == id);
            }
            return false;
        }

        private static string ServerKey(Guid serverId) => $"server:{serverId:D}";
        private static string DirectKey(string playerId) => $"direct:{playerId}";
        private static bool TryServerKey(string key, out Guid serverId)
        {
            serverId = default;
            return key.StartsWith("server:", StringComparison.Ordinal) &&
                Guid.TryParse(key.Substring("server:".Length), out serverId);
        }
        private string ServerTitle(Guid serverId) =>
            serverId == ClientSession.SelectedServerId && !string.IsNullOrEmpty(ClientSession.SelectedServerName)
                ? $"SERVER // {ClientSession.SelectedServerName}"
                : $"SERVER // {serverId:D}";
        private string ResolveDirectTitle(string playerId, ChatPlayer fallback) =>
            FormatPlayerTitle(_onlinePlayers.FirstOrDefault(p => p.PlayerId == playerId) ?? fallback ?? new ChatPlayer(playerId, playerId, string.Empty));
        private static string FormatPlayerTitle(ChatPlayer player) =>
            string.IsNullOrEmpty(player.SessionTag) ? player.DisplayName : $"{player.DisplayName} // {player.SessionTag}";

        private void SetStatus(string status)
        {
            _status = status ?? string.Empty;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnDestroy()
        {
            if (_instance != this)
                return;
            _destroyed = true;
            _lobby?.DisconnectAsync();
            _masterClient?.Dispose();
            _lobby = null;
            _masterClient = null;
            _ticketCallback?.Dispose();
            if (_ownsSteamApi)
                SteamAPI.Shutdown();
            _instance = null;
        }
    }
}
