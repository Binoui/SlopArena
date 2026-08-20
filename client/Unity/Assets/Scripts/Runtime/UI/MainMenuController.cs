using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Client.Network;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private string _masterServerUrl = "https://sloparena.barakaslurp.fr";

        private Label _lblHostStatus;
        private TextField _hostIpField;   // host-and-play public IP/domain (ADR-0009)
        private bool _hosting;   // guards against double-click during the async start

        private void OnEnable()
        {
            MatchConfig.Reset();
            var root = _uiDocument.rootVisualElement;

            var submenu             = root.Q<VisualElement>("submenu");
            var directConnectModal  = root.Q<VisualElement>("direct-connect-modal");
            var btnTraining         = root.Q<Button>("btn-training");
            var btnMultiplayer      = root.Q<Button>("btn-multiplayer");
            var btnHost             = root.Q<Button>("btn-host");
            var btnServerBrowser    = root.Q<Button>("btn-serverbrowser");
            var btnDirectConnect    = root.Q<Button>("btn-direct-connect");
            var btnModalClose       = root.Q<Button>("btn-modal-close");
            var btnJoin             = root.Q<Button>("btn-join");
            var ipField             = root.Q<TextField>("ip-field");
            var hostIpField         = root.Q<TextField>("host-ip-field");
            var directConnectStatus = root.Q<Label>("direct-connect-status");
            _hostIpField            = hostIpField;
            _lblHostStatus          = root.Q<Label>("lbl-host-status");

            btnTraining.clicked += () =>
            {
                MatchConfig.Mode   = GameMode.Training;
                MatchConfig.IsHost = true;
                SceneManager.LoadScene("CharSelect");
            };

            btnMultiplayer.clicked += () =>
            {
                bool visible = submenu.style.display == DisplayStyle.Flex;
                submenu.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            };
            btnDirectConnect.clicked += () =>
            {
                directConnectModal.style.display = DisplayStyle.Flex;
                directConnectStatus.text = string.Empty;
                directConnectStatus.RemoveFromClassList("error");
                directConnectStatus.RemoveFromClassList("success");
                ipField.Focus();
            };

            btnModalClose.clicked += () =>
            {
                directConnectModal.style.display = DisplayStyle.None;
            };


            // Embedded host-and-play (ADR-0005, issue #39): start the game
            // server as a subprocess, guest-auth with the master server, wait
            // for registration, then auto-join our own lobby at localhost.
            btnHost.clicked += OnHostClicked;

            btnServerBrowser.clicked += () =>
            {
                SceneManager.LoadScene("ServerBrowser");
            };

            btnJoin.clicked += () =>
            {
                string ip = ipField.value.Trim();
                directConnectStatus.RemoveFromClassList("error");
                directConnectStatus.RemoveFromClassList("success");

                if (string.IsNullOrEmpty(ip))
                {
                    directConnectStatus.text = "Enter an IP address or hostname.";
                    directConnectStatus.AddToClassList("error");
                    ipField.Focus();
                    return;
                }

                directConnectStatus.text = "Joining " + ip + "...";
                directConnectStatus.AddToClassList("success");
                MatchConfig.Mode     = GameMode.PvP;
                MatchConfig.IsHost   = false;
                MatchConfig.ServerIP = ip;
                SceneManager.LoadScene("Lobby");
            };
        }

        private void OnHostClicked()
        {
            if (_hosting) return;
            _hosting = true;
            _lblHostStatus.style.display = DisplayStyle.Flex;
            _lblHostStatus.text = "Starting game server...";
            StartHostingFlow();
        }

        private async void StartHostingFlow()
        {
            // Embedded host-and-play (ADR-0005): spawn the game server as a
            // subprocess, wait for it to register with the master server, then
            // auto-join our own lobby at localhost.
            var host = ServerHost.Create();

            // Guest auth (needed to join the lobby). The server registers with
            // the master server independently; we only need its server-id.
            string? authToken = null;
            long steamId = 0;
            using (var masterClient = new MasterServerClient(_masterServerUrl))
            {
                ClientSession.MasterServerUrl = _masterServerUrl;
                bool authed = await masterClient.AuthenticateGuestAsync();
                if (!authed)
                {
                    _hosting = false;
                    _lblHostStatus.text = "Failed to authenticate with master server.";
                    return;
                }
                authToken = masterClient.Token;
                steamId = masterClient.SteamId ?? 0;
            }

            var tcs = new TaskCompletionSource<bool>();
            Guid registeredServerId = Guid.Empty;
            string? registrationError = null;
            int? crashCode = null;
            string? crashStderr = null;

            Action<Guid> onRegistered = id =>
            {
                registeredServerId = id;
                tcs.TrySetResult(true);
            };
            Action<string> onRegFailed = reason =>
            {
                registrationError = reason;
                tcs.TrySetResult(false);
            };
            Action<int, string> onCrashed = (code, stderr) =>
            {
                crashCode = code;
                crashStderr = stderr;
                tcs.TrySetResult(false);
            };

            host.Registered        += onRegistered;
            host.RegistrationFailed += onRegFailed;
            host.Crashed            += onCrashed;

            // Spawn the server subprocess now that listeners are wired.
            string hostIp = _hostIpField.value.Trim();
            host.StartHosting(GenerateServerName(), string.IsNullOrEmpty(hostIp) ? null : hostIp);

            // Wait for registration, crash, or a 15s timeout. Pump() runs in
            // ServerHost.Update, so events fire on the main thread.
            Task winner = await Task.WhenAny(tcs.Task, Task.Delay(15000));

            host.Registered         -= onRegistered;
            host.RegistrationFailed  -= onRegFailed;
            host.Crashed             -= onCrashed;

            if (winner != tcs.Task)
            {
                _hosting = false;
                _lblHostStatus.text = "Server did not register in time. Is the master server running?";
                host.Stop();
                return;
            }

            if (registeredServerId != Guid.Empty)
            {
                // Host auto-joins their own lobby at localhost.
                MatchConfig.Mode     = GameMode.PvP;
                MatchConfig.IsHost   = true;
                MatchConfig.ServerIP = "127.0.0.1";
                MatchConfig.ServerPort = host.AssignedPort;

                ClientSession.AuthToken           = authToken;
                ClientSession.SteamId             = steamId;
                ClientSession.SelectedServerId    = registeredServerId;
                ClientSession.SelectedServerName  = GenerateServerName();

                Debug.Log($"[MainMenu] Hosted server registered (ID {registeredServerId}) on port {host.AssignedPort}. Joining lobby.");
                SceneManager.LoadScene("LobbyRoom");
                return;
            }

            // Failure: registration failed or crash — no silent failure.
            _hosting = false;
            if (crashCode != null)
                _lblHostStatus.text = $"Server crashed (code {crashCode}). {Truncate(crashStderr)}";
            else
                _lblHostStatus.text = $"Could not start server: {registrationError}";
            host.Stop();
        }

        private static string GenerateServerName() =>
            $"{System.Environment.MachineName}'s Server";

        private static string Truncate(string? s) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length > 200 ? s.Substring(0, 200) + "…" : s);
    }
}
