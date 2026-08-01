#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client.Network;
using SlopArena.Client;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Lobby room screen driven by the SignalR <see cref="LobbyClient"/>
    /// (ADR-0008, issue #33). Reached from the Server Browser after joining a
    /// game server: shows the live player list pushed by the master server's
    /// <c>LobbyHub</c>, lets the host start the match, and lets anyone leave.
    /// </summary>
    public class LobbyRoomUI : MonoBehaviour
    {
        private const int MaxSlots = 4;

        [SerializeField] private UIDocument _uiDocument;

        private LobbyClient _lobby;
        private VisualElement _playerList;
        private Button _btnStart;
        private Button _btnLeave;
        private Label _lblStatus;
        private Label _lblServer;

        private LobbySnapshot _snapshot;

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            _playerList = root.Q<VisualElement>("player-list");
            _btnStart    = root.Q<Button>("btn-start");
            _btnLeave    = root.Q<Button>("btn-leave");
            _lblStatus   = root.Q<Label>("lbl-status");
            _lblServer   = root.Q<Label>("lbl-server");

            _lblServer.text = string.IsNullOrEmpty(ClientSession.SelectedServerName)
                ? ClientSession.SelectedServerId.ToString()
                : ClientSession.SelectedServerName;

            var btnBack = root.Q<Button>("btn-back");
            btnBack.clicked += Leave;

            _btnLeave.clicked += Leave;
            _btnStart.clicked += OnStartClicked;
            _btnStart.style.display = DisplayStyle.None; // shown once we learn we're host

            RenderPlayers();

            if (string.IsNullOrEmpty(ClientSession.AuthToken))
            {
                _lblStatus.text = "Not authenticated. Returning to server browser.";
                SceneManager.LoadScene("ServerBrowser");
                return;
            }

            _lobby.Connected    += OnConnected;
            _lobby.PlayerJoined += OnPlayerJoined;
            _lobby.PlayerLeft   += OnPlayerLeft;
            _lobby.LobbyUpdated += OnLobbyUpdated;
            _lobby.MatchStarting += OnMatchStarting;
            _lobby.Error        += OnError;
            _lobby.Disconnected += OnDisconnected;

            _lblStatus.text = "Connecting to lobby...";
            ConnectAndJoin();
        }

        private async void ConnectAndJoin()
        {
            bool ok = await _lobby.ConnectAsync();
            if (!ok)
            {
                _lblStatus.text = "Could not reach the master server.";
                return;
            }
            await _lobby.JoinLobbyAsync(ClientSession.SelectedServerId);
        }

        private void Update()
        {
            // Marshals hub events from background threads onto the Unity main thread.
            _lobby?.Pump();
        }

        // ── Hub event handlers (fired on main thread via Pump) ──

        private void OnConnected()
        {
            _lblStatus.text = "Connected.";
        }

        // PlayerJoined/PlayerLeft are surfaced separately; LobbyUpdated is the
        // authoritative snapshot, so these just log — they let a later ticket
        // animate join/leave without waiting for the full snapshot.
        private void OnPlayerJoined(LobbyPlayerInfo player)
        {
            Debug.Log($"[LobbyRoom] {player.Name} (SteamId {player.SteamId}) joined.");
        }

        private void OnPlayerLeft(long steamId)
        {
            Debug.Log($"[LobbyRoom] SteamId {steamId} left.");
        }
        private void OnLobbyUpdated(LobbySnapshot snapshot)
        {
            _snapshot = snapshot;
            RenderPlayers();
        }

        private void OnMatchStarting(MatchStartingConfig config)
        {
            Debug.Log($"[LobbyRoom] Match starting on server {config.ServerId} with {config.Players.Count} players.");
            // Stash the roster for char-select / match start (later tickets).
            MatchConfig.Mode = GameMode.PvP;
            SceneManager.LoadScene("CharSelect");
        }

        private void OnError(string message)
        {
            _lblStatus.text = message;
        }

        private void OnDisconnected(System.Exception ex)
        {
            _lblStatus.text = ex == null ? "Disconnected." : $"Disconnected: {ex.Message}";
        }

        // ── UI ──

        private void OnStartClicked()
        {
            _btnStart.SetEnabled(false);
            _lblStatus.text = "Starting match...";
            _ = _lobby.HostStartAsync();
        }

        private void RenderPlayers()
        {
            _playerList.Clear();

            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            bool isLocalHost = false;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].SteamId == ClientSession.SteamId && players[i].IsHost)
                    isLocalHost = true;
            }

            for (int i = 0; i < MaxSlots; i++)
                _playerList.Add(CreateSlot(i, players));

            // Start button: host-only, enabled with 2+ players.
            if (isLocalHost)
            {
                _btnStart.style.display = DisplayStyle.Flex;
                _btnStart.SetEnabled(players.Count >= 2);
            }
            else
            {
                _btnStart.style.display = DisplayStyle.None;
            }

            if (!isLocalHost && players.Count > 0)
                _lblStatus.text = "Waiting for host to start...";
        }

        private VisualElement CreateSlot(int index, IReadOnlyList<LobbyPlayerInfo> players)
        {
            var slot = new VisualElement();
            slot.AddToClassList("player-slot");

            var slotIndex = new Label($"P{index + 1}") { name = "slot-index" };
            slotIndex.AddToClassList("slot-index");

            var portrait = new VisualElement { name = "slot-portrait" };
            portrait.AddToClassList("slot-portrait");

            var name = new Label { name = "slot-name" };
            name.AddToClassList("slot-name");

            if (index < players.Count)
            {
                var p = players[index];
                name.text = p.Name;

                // Portrait reflects a picked character; char-select lands in a
                // later ticket, so CharacterSelection is null until then.
                if (!string.IsNullOrEmpty(p.CharacterSelection))
                {
                    var portraitLabel = new Label(p.CharacterSelection);
                    portrait.Add(portraitLabel);
                }

                if (p.IsHost)
                {
                    var badge = new Label("HOST") { name = "host-badge" };
                    badge.AddToClassList("host-badge");
                    slot.Add(badge);
                }
            }
            else
            {
                name.text = "Empty";
                name.AddToClassList("slot-name--empty");
            }

            slot.Add(slotIndex);
            slot.Add(portrait);
            slot.Add(name);
            return slot;
        }

        private async void Leave()
        {
            if (_lobby != null)
            {
                try { await _lobby.LeaveLobbyAsync(); } catch { /* best effort */ }
                await _lobby.DisconnectAsync();
            }
            SceneManager.LoadScene("ServerBrowser");
        }

        private async void OnDisable()
        {
            _btnStart.clicked -= OnStartClicked;
            _btnLeave.clicked -= Leave;
            if (_lobby != null)
            {
                _lobby.Connected    -= OnConnected;
                _lobby.PlayerJoined -= OnPlayerJoined;
                _lobby.PlayerLeft   -= OnPlayerLeft;
                _lobby.LobbyUpdated -= OnLobbyUpdated;
                _lobby.MatchStarting -= OnMatchStarting;
                _lobby.Error        -= OnError;
                _lobby.Disconnected -= OnDisconnected;
                await _lobby.DisconnectAsync();
            }
        }
    }
}
