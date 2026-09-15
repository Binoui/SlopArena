#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
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
            if (string.IsNullOrEmpty(ClientSession.AuthToken))
            {
                SetStatus("Your room session expired. Return to the browser to reconnect.", true);
                SetRetryVisible(false);
                return;
            }

            _lobby = ClientSession.ActiveLobby ??= new LobbyClient(
                ClientSession.MasterServerUrl, ClientSession.AuthToken);
            SubscribeLobby(_lobby);
            _awaitingLobby = true;
            SetStatus(_lobby.IsConnected ? "Rejoining the room…" : "Connecting to the room…", false);
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
            lobby.Error += OnError;
            lobby.Disconnected += OnDisconnected;
        }

        private async void ConnectAndJoin(int generation, CancellationToken ct)
        {
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
            if (_lobby != null)
            {
                UnsubscribeLobby(_lobby);
                _ = _lobby.DisconnectAsync();
            }
            _lobby = new LobbyClient(ClientSession.MasterServerUrl, ClientSession.AuthToken!);
            ClientSession.ActiveLobby = _lobby;
            SubscribeLobby(_lobby);
            _awaitingLobby = true;
            _startPending = false;
            SetRetryVisible(false);
            SetStatus("Retrying the room connection…", false);
            StartLobbyWatchdog(_attempt);
            ConnectAndJoin(_attempt, _lifecycleCts!.Token);
        }

        private void Update() => _lobby?.Pump();

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
            SceneManager.LoadScene("CharSelect");
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
            var name = new Label("Open slot") { name = "slot-name" };
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
            ClientSession.ActiveLobby = null;
            SceneManager.LoadScene("ServerBrowser");
        }

        private static async Task LeaveRoomAsync(LobbyClient lobby)
        {
            try
            {
                await Task.WhenAny(lobby.LeaveLobbyAsync(), Task.Delay(2000));
                await lobby.DisconnectAsync();
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
            lobby.Error -= OnError;
            lobby.Disconnected -= OnDisconnected;
        }
    }
}
