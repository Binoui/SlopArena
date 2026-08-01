using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Server browser screen: queries the master server for active game servers,
    /// displays them, and lets the player join one.
    /// </summary>
    public class ServerBrowserUI : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private string _masterServerUrl = "http://localhost:5000";

        private MasterServerClient _masterClient;
        private VisualElement _serverList;
        private Label _lblStatus;

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            _serverList = root.Q<VisualElement>("server-list");
            _lblStatus   = root.Q<Label>("lbl-status");
            var btnRefresh = root.Q<Button>("btn-refresh");
            var btnBack    = root.Q<Button>("btn-back");

            btnRefresh.clicked += RefreshServers;
            btnBack.clicked += () => SceneManager.LoadScene("MainMenu");

            _masterClient = new MasterServerClient(_masterServerUrl);
            ClientSession.MasterServerUrl = _masterServerUrl;
            RefreshServers();
        }

        private async void RefreshServers()
        {
            _serverList.Clear();
            _lblStatus.style.display = DisplayStyle.Flex;

            if (!_masterClient.IsAuthenticated)
            {
                _lblStatus.text = "Authenticating...";
                bool authed = await _masterClient.AuthenticateGuestAsync();
                if (!authed)
                {
                    _lblStatus.text = "Failed to connect to master server.";
                    return;
                }
            }

            _lblStatus.text = "Loading servers...";
            var servers = await _masterClient.GetServersAsync();
            if (servers == null)
            {
                _lblStatus.text = "Failed to load server list.";
                return;
            }

            if (servers.Count == 0)
            {
                _lblStatus.text = "No servers available.";
                return;
            }

            _lblStatus.style.display = DisplayStyle.None;

            foreach (var server in servers)
                _serverList.Add(CreateServerRow(server));
        }

        private VisualElement CreateServerRow(ServerInfo server)
        {
            var row = new VisualElement();
            row.AddToClassList("server-row");

            var name = new Label(server.Name) { name = "server-name" };
            name.AddToClassList("server-name");

            var info = new Label($"{server.Region}  —  {server.CurrentMatches}/{server.MaxConcurrentMatches}")
            {
                name = "server-info"
            };
            info.AddToClassList("server-info");

            var join = new Button(() => JoinServer(server))
            {
                text = "JOIN",
                name = "btn-join"
            };
            join.AddToClassList("server-join");

            row.Add(name);
            row.Add(info);
            row.Add(join);

            return row;
        }

        private void JoinServer(ServerInfo server)
        {
            MatchConfig.Mode     = GameMode.PvP;
            MatchConfig.IsHost   = false;
            MatchConfig.ServerIP = server.IpAddress;
            MatchConfig.ServerPort = server.Port;

            // Carry the guest auth + selected server to the SignalR lobby room.
            ClientSession.AuthToken          = _masterClient.Token;
            ClientSession.SteamId            = _masterClient.SteamId ?? 0;
            ClientSession.SelectedServerId   = server.Id;
            ClientSession.SelectedServerName = server.Name;

            Debug.Log($"[ServerBrowser] Joining server: {server.Name} ({server.IpAddress}:{server.Port})");

            SceneManager.LoadScene("LobbyRoom");
        }

        private void OnDisable()
        {
            _masterClient?.Dispose();
        }
    }
}
