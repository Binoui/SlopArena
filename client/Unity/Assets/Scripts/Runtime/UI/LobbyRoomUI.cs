#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared;
using SlopArena.Client.Network;
using SlopArena.Client;

namespace SlopArena.Client.UI
{
    /// <summary>Live lobby room driven by the existing master-server lobby connection.</summary>
    public class LobbyRoomUI : MonoBehaviour
    {
        private const int MaxSlots = 4;
        private const float LobbyTimeoutSeconds = 12f;

        [SerializeField] private UIDocument _uiDocument;

        private LobbyClient? _lobby;
        private VisualElement? _playerList;
        private Button? _btnStart;
        private Button? _btnLeave;
        private Button? _btnRetry;
        private Button? _backButton;
        private Label? _lblStatus;
        private Label? _lblServer;
        private LobbySnapshot? _snapshot;
        private CancellationTokenSource? _lifecycleCts;
        private Coroutine? _lobbyWatchdog;
        private Coroutine? _startWatchdog;
        private bool _alive;
        private bool _awaitingLobby;
        private bool _startPending;
        private bool _leaving;
        private int _attempt;

        private void OnEnable()
        {
            _alive = true;
            _leaving = false;
            // Page re-entry is a fresh room (ADR-0032): a roster or start
            // attempt left by a previous visit must not outlive its page.
            _snapshot = null;
            _startPending = false;
            _lifecycleCts = new CancellationTokenSource();
            int generation = ++_attempt;
            var root = _uiDocument.rootVisualElement;
            _playerList = root.Q<VisualElement>("player-list");
            _btnStart = root.Q<Button>("btn-start");
            _btnLeave = root.Q<Button>("btn-leave");
            _btnRetry = root.Q<Button>("btn-retry");
            _backButton = root.Q<Button>("btn-back");
            _lblStatus = root.Q<Label>("lbl-status");
            _lblServer = root.Q<Label>("lbl-server");

            if (_lblServer != null)
                _lblServer.text = string.IsNullOrEmpty(ClientSession.SelectedServerName)
                    ? ClientSession.SelectedServerId.ToString()
                    : ClientSession.SelectedServerName;
            if (_backButton != null) _backButton.clicked += Leave;
            if (_btnLeave != null) _btnLeave.clicked += Leave;
            if (_btnRetry != null)
            {
                _btnRetry.clicked += RetryConnection;
                _btnRetry.style.display = DisplayStyle.None;
            }
            if (_btnStart != null)
            {
                _btnStart.clicked += OnStartClicked;
                _btnStart.style.display = DisplayStyle.None;
            }
            // Leave is always visible; never focus a hidden recovery action first.
            MenuNavigation.Configure(root, _btnLeave ?? _btnStart ?? _btnRetry, Leave);

            RenderPlayers();
            var chat = ChatSession.Instance;
            if (chat == null)
            {
                SetStatus("Chat session unavailable. Return to the server browser.", true);
                SetRetryVisible(false);
                return;
            }

            _lobby = chat.ActiveLobby;
            if (_lobby != null)
                SubscribeLobby(_lobby);
            _awaitingLobby = true;
            SetStatus(chat.NeedsDisplayName
                ? "Choose a display name on the HOME screen before joining a room."
                : (_lobby?.IsConnected == true ? "Rejoining the room…" : "Connecting to the room…"), false);
            SetRetryVisible(false);
            StartLobbyWatchdog(generation);
            ConnectAndJoin(generation, _lifecycleCts.Token);
        }

        private void SubscribeLobby(LobbyClient lobby)
        {
            lobby.Connected += OnConnected;
            lobby.PlayerJoined += OnPlayerJoined;
            lobby.PlayerLeft += OnPlayerLeft;
            lobby.LobbyUpdated += OnLobbyUpdated;
            lobby.MatchStarting += OnMatchStarting;
            lobby.StageSelect += OnServerStageSelect;
            lobby.MatchStarted += OnServerMatchStarted;
            lobby.Error += OnError;
            lobby.Disconnected += OnDisconnected;
        }

        private async void ConnectAndJoin(int generation, CancellationToken ct)
        {
            if (_lobby == null)
            {
                var chat = ChatSession.Instance;
                if (chat == null || !await chat.EnsureConnectedAsync())
                {
                    if (IsCurrent(generation, ct))
                        FailLobby(chat?.NeedsDisplayName == true
                            ? "Choose a display name on the HOME screen before joining a room."
                            : "Couldn’t connect to the room directory. Retry, or return to the server browser.");
                    return;
                }
                _lobby = chat.ActiveLobby;
                if (_lobby != null)
                    SubscribeLobby(_lobby);
            }
            if (_lobby == null)
                return;
            try
            {
                bool connected = _lobby.IsConnected || await _lobby.ConnectAsync();
                if (!IsCurrent(generation, ct)) return;
                if (!connected)
                {
                    FailLobby("Couldn’t connect to the room directory. Retry, or return to the server browser.");
                    return;
                }

                await _lobby.JoinLobbyAsync(ClientSession.SelectedServerId);
                if (!IsCurrent(generation, ct)) return;
                SetStatus("Connected. Waiting for the room roster…", false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyRoom] Connection failed: {ex.Message}");
                if (IsCurrent(generation, ct))
                    FailLobby("Couldn’t join this room. Retry, or return to the server browser.");
            }
        }

        private void StartLobbyWatchdog(int generation)
        {
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            _lobbyWatchdog = StartCoroutine(LobbyWatchdog(generation));
        }

        private System.Collections.IEnumerator LobbyWatchdog(int generation)
        {
            yield return new WaitForSeconds(LobbyTimeoutSeconds);
            if (_alive && generation == _attempt && _awaitingLobby)
                FailLobby("The room did not answer in time. Retry the connection or return to the browser.");
        }

        private void RetryConnection()
        {
            if (!_alive || _leaving)
                return;
            _attempt++;
            _lobby = ChatSession.Instance?.ActiveLobby;
            _awaitingLobby = true;
            _startPending = false;
            SetRetryVisible(false);
            SetStatus("Retrying the room connection…", false);
            StartLobbyWatchdog(_attempt);
            ConnectAndJoin(_attempt, _lifecycleCts!.Token);
        }


        private void OnConnected()
        {
            if (!_alive || !_awaitingLobby) return;
            SetStatus("Connected. Waiting for the room roster…", false);
        }
        private void OnPlayerJoined(LobbyPlayerInfo player)
        {
            if (_alive)
                Debug.Log($"[LobbyRoom] {player.Name} (SteamId {player.SteamId}) joined.");
        }

        private void OnPlayerLeft(long steamId)
        {
            if (_alive)
                Debug.Log($"[LobbyRoom] SteamId {steamId} left.");
        }

        private void OnLobbyUpdated(LobbySnapshot snapshot)
        {
            if (!_alive) return;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            _snapshot = snapshot;
            SetRetryVisible(false);
            RenderPlayers();
        }

        private void OnMatchStarting(MatchStartingConfig config)
        {
            if (!_alive || _leaving) return;
            _awaitingLobby = false;
            ClientSession.LobbyRoster = new LobbySnapshot(config.ServerId, config.Players);
            MatchConfig.Mode = GameMode.PvP;
            // Host authority comes from the authoritative roster at every
            // server-driven transition (issue #213), not from a local hosting
            // flag; CharSelectController recomputes the same way.
            ClientSession.IsLobbyHost = IsRosterHost(config.Players);
            FrontendController.Show(FrontendPage.FighterSelect);
        }

        private void OnServerStageSelect(MatchStartingConfig config)
        {
            // A player who locked in and backed out to this room is still in
            // the match-start flow: the host's stage-select push advances this
            // page too (issue #213). LobbyRoomUI shows no dialogs, but page
            // deactivation still drops any departed-page state.
            if (!_alive || _leaving) return;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            ClientSession.LobbyRoster = new LobbySnapshot(config.ServerId, config.Players);
            MatchConfig.Mode = GameMode.PvP;
            ClientSession.IsLobbyHost = IsRosterHost(config.Players);
            FrontendController.Show(FrontendPage.StageSelect);
        }

        private void OnServerMatchStarted(MatchStartedConfig config)
        {
            if (!_alive || _leaving) return;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            ClientSession.ApplyMatchStarted(config);
        }

        private static bool IsRosterHost(IReadOnlyList<LobbyPlayerInfo> players)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].SteamId == ClientSession.SteamId && players[i].IsHost)
                    return true;
            }
            return false;
        }

        private void OnError(string message)
        {
            if (!_alive) return;
            if (_startPending)
            {
                _startPending = false;
                if (_startWatchdog != null) StopCoroutine(_startWatchdog);
                RenderPlayers();
            }
            FailLobby(string.IsNullOrWhiteSpace(message)
                ? "The room rejected that request. Retry, or return to the browser."
                : message);
        }

        private void OnDisconnected(Exception? ex)
        {
            if (!_alive) return;
            if (_startPending)
            {
                _startPending = false;
                if (_startWatchdog != null) StopCoroutine(_startWatchdog);
                RenderPlayers();
            }
            _awaitingLobby = true;
            SetRetryVisible(true);
            SetStatus(ex == null
                ? "The room connection closed. Retry, or return to the browser."
                : "The room connection dropped. Retry, or return to the browser.", true);
        }

        private void OnStartClicked()
        {
            if (!_alive || _lobby == null || _startPending || !_lobby.IsConnected)
                return;
            _startPending = true;
            _btnStart?.SetEnabled(false);
            SetStatus("Starting the match…", false);
            if (_startWatchdog != null) StopCoroutine(_startWatchdog);
            _startWatchdog = StartCoroutine(StartWatchdog());
            _ = StartMatchRequest();
        }

        private async Task StartMatchRequest()
        {
            try { await _lobby!.HostStartAsync(); }
            catch (Exception ex)
            {
                if (_alive) OnError($"Couldn’t start the match: {ex.Message}");
            }
        }

        private System.Collections.IEnumerator StartWatchdog()
        {
            yield return new WaitForSeconds(LobbyTimeoutSeconds);
            if (_alive && _startPending)
            {
                _startPending = false;
                RenderPlayers();
                SetStatus("The match did not start in time. Check the room, then try again.", true);
            }
        }

        private void RenderPlayers()
        {
            if (_playerList == null) return;
            _playerList.Clear();
            var players = _snapshot?.Players ?? Array.Empty<LobbyPlayerInfo>();
            bool isLocalHost = false;
            for (int i = 0; i < players.Count; i++)
                isLocalHost |= players[i].SteamId == ClientSession.SteamId && players[i].IsHost;

            for (int i = 0; i < MaxSlots; i++)
                _playerList.Add(CreateSlot(i, players));

            if (_btnStart != null)
            {
                _btnStart.style.display = isLocalHost ? DisplayStyle.Flex : DisplayStyle.None;
                _btnStart.SetEnabled(isLocalHost && players.Count >= 2 && !_startPending);
            }
            if (isLocalHost)
                SetStatus(players.Count >= 2 ? "Your room is ready. Start when everyone is here." : "Waiting for another player to join…", false);
            else if (players.Count > 0)
                SetStatus("Connected. Waiting for the host to start…", false);
        }

        private VisualElement CreateSlot(int index, IReadOnlyList<LobbyPlayerInfo> players)
        {
            var slot = new VisualElement { name = "player-slot" };
            slot.AddToClassList("player-slot");
            var slotIndex = new Label($"P{index + 1}") { name = "slot-index" };
            slotIndex.AddToClassList("slot-index");
            var name = new Label("Open slot") { name = "slot-name", enableRichText = false };
            name.AddToClassList("slot-name");
            if (index < players.Count)
            {
                var player = players[index];
                name.text = player.Name;
                if (player.IsHost)
                {
                    var badge = new Label("HOST") { name = "host-badge" };
                    badge.AddToClassList("host-badge");
                    slot.Add(badge);
                }
            }
            slot.Add(slotIndex);
            slot.Add(name);
            return slot;
        }

        private void FailLobby(string message)
        {
            if (!_alive) return;
            _attempt++;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            SetStatus(message, true);
            SetRetryVisible(true);
        }

        private void SetStatus(string message, bool error)
        {
            if (_lblStatus == null) return;
            _lblStatus.text = message;
            if (error) _lblStatus.AddToClassList("error");
            else _lblStatus.RemoveFromClassList("error");
        }

        private void SetRetryVisible(bool visible)
        {
            if (_btnRetry == null) return;
            _btnRetry.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _btnRetry.SetEnabled(visible && !_leaving);
        }

        private bool IsCurrent(int generation, CancellationToken ct) =>
            _alive && !_leaving && generation == _attempt && !ct.IsCancellationRequested;

        private void Leave()
        {
            if (!_alive || _leaving) return;
            _leaving = true;
            _alive = false;
            _attempt++;
            _lifecycleCts?.Cancel();
            if (MatchConfig.IsHost)
                ServerHost.Instance?.Stop();
            if (_lobby != null)
                _ = LeaveRoomAsync(_lobby);
            FrontendController.Show(FrontendPage.ServerBrowser);
        }

        private static async Task LeaveRoomAsync(LobbyClient lobby)
        {
            try
            {
                await Task.WhenAny(lobby.LeaveLobbyAsync(), Task.Delay(2000));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyRoom] Room teardown failed: {ex.Message}");
            }
        }

        private void OnDisable()
        {
            _alive = false;
            _attempt++;
            _lifecycleCts?.Cancel();
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            if (_startWatchdog != null) StopCoroutine(_startWatchdog);
            if (_backButton != null) _backButton.clicked -= Leave;
            if (_btnLeave != null) _btnLeave.clicked -= Leave;
            if (_btnRetry != null) _btnRetry.clicked -= RetryConnection;
            if (_btnStart != null) _btnStart.clicked -= OnStartClicked;
            if (_lobby != null) UnsubscribeLobby(_lobby);
            _lifecycleCts?.Dispose();
            _lifecycleCts = null;
        }

        private void UnsubscribeLobby(LobbyClient lobby)
        {
            lobby.Connected -= OnConnected;
            lobby.PlayerJoined -= OnPlayerJoined;
            lobby.PlayerLeft -= OnPlayerLeft;
            lobby.LobbyUpdated -= OnLobbyUpdated;
            lobby.MatchStarting -= OnMatchStarting;
            lobby.StageSelect -= OnServerStageSelect;
            lobby.MatchStarted -= OnServerMatchStarted;
            lobby.Error -= OnError;
            lobby.Disconnected -= OnDisconnected;
        }
    }
}
