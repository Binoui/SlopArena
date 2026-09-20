using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared;
using SlopArena.Client;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    /// <summary>Server discovery, host-and-play, and advanced address entry.</summary>
    public class ServerBrowserUI : MonoBehaviour
    {
        private const int DefaultServerPort = 9876;
        private const int HostRegistrationTimeoutMs = 15000;

        /// <summary>
        /// One-shot status shown on the next browser activation (issue #213):
        /// Results sets it when a completed PvP match cannot return to its
        /// LobbyRoom, so the browser explains why the room is gone.
        /// </summary>
        public static string? PendingReturnNotice;

        /// <summary>Notice captured for this activation; shown until the scan settles.</summary>
        private string? _pendingReturnNotice;

        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private string _masterServerUrl = "https://sloparena.barakaslurp.fr";

        private MasterServerClient _masterClient;
        private VisualElement _serverList;
        private Label _lblStatus;
        private Button _btnRefresh;
        private Button _btnBack;
        private Button _btnHost;
        private Button _btnHostCancel;
        private TextField _hostIpField;
        private Label _lblHostStatus;
        private Button _btnDirectConnect;
        private VisualElement _directConnectModal;
        private TextField _ipField;
        private Label _directConnectStatus;
        private Focusable _focusBeforeModal;
        private Button _modalClose;
        private Button _modalJoin;
        private bool _modalPresented;

        private CancellationTokenSource _lifecycleCts;
        private CancellationTokenSource _refreshCts;
        private CancellationTokenSource _hostCts;
        private CancellationTokenSource _addressCts;
        private bool _alive;
        private bool _refreshing;
        private bool _joining;
        private bool _hostStarting;
        private bool _hostHandedOff;
        private int _operationVersion;
        private ServerHost _ownedHost;

        private void OnEnable()
        {
            _refreshing = _joining = _hostStarting = false;
            _alive = true;
            _hostHandedOff = false;
            _lifecycleCts = new CancellationTokenSource();

            var root = _uiDocument.rootVisualElement;
            _serverList = root.Q<VisualElement>("server-list");
            _lblStatus = root.Q<Label>("lbl-status");
            _btnRefresh = root.Q<Button>("btn-refresh");
            _btnBack = root.Q<Button>("btn-back");
            _btnHost = root.Q<Button>("btn-host");
            _btnHostCancel = root.Q<Button>("btn-host-cancel");
            _hostIpField = root.Q<TextField>("host-ip-field");
            _lblHostStatus = root.Q<Label>("lbl-host-status");
            _btnDirectConnect = root.Q<Button>("btn-direct-connect");
            _directConnectModal = root.Q<VisualElement>("direct-connect-modal");
            _ipField = root.Q<TextField>("ip-field");
            _directConnectStatus = root.Q<Label>("direct-connect-status");

            if (_btnRefresh != null) _btnRefresh.clicked += RefreshServers;
            if (_btnBack != null) _btnBack.clicked += LeaveBrowser;
            if (_btnHost != null) _btnHost.clicked += OnHostClicked;
            if (_btnDirectConnect != null) _btnDirectConnect.clicked += OpenDirectConnect;
            if (_btnHostCancel != null) _btnHostCancel.clicked += CancelHost;
            _modalClose = root.Q<Button>("btn-modal-close");
            _modalJoin = root.Q<Button>("btn-join");
            if (_modalClose != null) _modalClose.clicked += CloseDirectConnect;
            if (_modalJoin != null) _modalJoin.clicked += JoinDirectConnect;

            // Page re-entry is a fresh browser (ADR-0032): rows left by a
            // previous visit's scan must not survive their operation.
            _serverList?.Clear();
            if (_directConnectStatus != null)
            {
                _directConnectStatus.text = string.Empty;
                _directConnectStatus.RemoveFromClassList("error");
            }

            if (_btnHostCancel != null)
                _btnHostCancel.style.display = DisplayStyle.None;
            if (_lblHostStatus != null)
            {
                _lblHostStatus.style.display = DisplayStyle.None;
                _lblHostStatus.text = string.Empty;
            }
            if (_directConnectModal != null)
            {
                _directConnectModal.style.display = DisplayStyle.None;
                // Configure the modal's cancel route separately; its callback stops
                // NavigationCancel before it can bubble to the browser.
                MenuNavigation.Configure(_directConnectModal, null, CloseDirectConnect);
                _directConnectModal.RegisterCallback<KeyDownEvent>(OnModalKeyDown);
            }

            var initial = _btnRefresh ?? _btnHost ?? _btnDirectConnect;
            if (initial != null)
                MenuNavigation.Configure(root, initial, LeaveBrowser);

            ChatSession.ConfigureMasterServerUrl(_masterServerUrl);
            _masterClient = ChatSession.Instance?.MasterClient;
            // The return explanation is captured for this activation and shown
            // until the scan settles; SetBrowserStatus suppresses loading
            // replacements while it is pending (issue #213).
            _pendingReturnNotice = PendingReturnNotice;
            PendingReturnNotice = null;
            if (_pendingReturnNotice != null)
            {
                Debug.Log($"[ServerBrowser] {_pendingReturnNotice}");
                SetBrowserStatus(_pendingReturnNotice, loading: false);
            }
            RefreshServers();
        }
        private async void RefreshServers()
        {
            if (!_alive || _refreshing || _joining)
                return;

            _refreshing = true;
            int operation = ++_operationVersion;
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
            CancellationToken ct = _refreshCts.Token;
            SetBrowserStatus("Looking for public rooms…", loading: true);

            try
            {
                var chat = ChatSession.Instance;
                SetBrowserStatus("Connecting to the room directory…", loading: true);
                if (chat == null || !await chat.EnsureConnectedAsync() || !IsCurrent(operation, ct))
                {
                    if (IsCurrent(operation, ct))
                        ShowBrowserFailure(chat?.NeedsDisplayName == true
                            ? "Choose a display name on the HOME screen before browsing rooms."
                            : "Couldn’t reach the room directory. Check your connection, then retry.");
                    return;
                }
                _masterClient = chat.MasterClient;
                if (_masterClient == null)
                {
                    ShowBrowserFailure("Couldn’t reach the room directory. Check your connection, then retry.");
                    return;
                }
                SetBrowserStatus("Scanning for public rooms…", loading: true);
                var servers = await _masterClient.GetServersAsync(ct);
                if (!IsCurrent(operation, ct))
                    return;
                if (servers == null)
                {
                    ShowBrowserFailure("Couldn’t load public rooms. Check your connection, then retry.");
                    return;
                }

                _serverList?.Clear();
                if (servers.Count == 0)
                {
                    _pendingReturnNotice = null;
                    SetBrowserStatus("No public rooms right now. Host a match or retry the scan.", loading: false);
                    return;
                }

                _pendingReturnNotice = null;
                if (_lblStatus != null)
                {
                    _lblStatus.style.display = DisplayStyle.None;
                    _lblStatus.RemoveFromClassList("error");
                }
                foreach (var server in servers)
                    _serverList?.Add(CreateServerRow(server));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Leaving the screen or starting a newer scan is an expected cancellation.
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerBrowser] Refresh failed: {ex.Message}");
                if (IsCurrent(operation, ct))
                    ShowBrowserFailure("Couldn’t load public rooms. Check your connection, then retry.");
            }
            finally
            {
                if (operation == _operationVersion)
                    _refreshing = false;
            }
        }

        private VisualElement CreateServerRow(ServerInfo server)
        {
            var row = new VisualElement { name = "server-row" };
            row.AddToClassList("server-row");

            var name = new Label(server.Name ?? "Unnamed room") { name = "server-name" };
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
            if (!_alive || _joining || _hostStarting)
                return;
            _joining = true;
            SetBrowserStatus($"Joining {server.Name}…", loading: true);
            SetBrowserActionsEnabled(false);

            var chat = ChatSession.Instance;
            ClientSession.AuthToken = chat?.AuthToken;
            ClientSession.SteamId = chat?.SteamId ?? 0;
            ClientSession.Username = chat?.Self?.DisplayName;
            ClientSession.SelectedServerId = server.Id;
            ClientSession.SelectedServerName = server.Name;
            Debug.Log($"[ServerBrowser] Joining server: {server.Name} ({server.IpAddress}:{server.Port})");
            FrontendController.Show(FrontendPage.LobbyRoom);
        }

        private void OpenDirectConnect()
        {
            if (!_alive || _joining || _hostStarting || _directConnectModal == null)
                return;
            _focusBeforeModal = _directConnectModal.panel?.focusController?.focusedElement;
            var root = _uiDocument.rootVisualElement;
            root.Q<VisualElement>("flow-header")?.SetEnabled(false);
            root.Q<VisualElement>("browser-layout")?.SetEnabled(false);
            _directConnectModal.style.display = DisplayStyle.Flex;
            _modalPresented = true;
            UiModalState.Push();
            if (_directConnectStatus != null)
            {
                _directConnectStatus.text = string.Empty;
                _directConnectStatus.RemoveFromClassList("error");
                _directConnectStatus.RemoveFromClassList("success");
            }
            _ipField?.Focus();
        }

        private void OnModalKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Tab || _directConnectModal == null)
                return;

            Focusable[] focusables = { _ipField, _modalClose, _modalJoin };
            int current = Array.IndexOf(focusables, _directConnectModal.panel?.focusController?.focusedElement);
            int direction = evt.shiftKey ? -1 : 1;
            int next = current < 0 ? 0 : (current + direction + focusables.Length) % focusables.Length;
            focusables[next]?.Focus();
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        private void CloseDirectConnect()
        {
            if (_directConnectModal == null)
            {
                return;
            }
            if (_addressCts != null)
            {
                _addressCts.Cancel();
                _addressCts.Dispose();
                _addressCts = null;
                _joining = false;
                _modalJoin?.SetEnabled(true);
            }
            _directConnectModal.style.display = DisplayStyle.None;
            if (_modalPresented)
            {
                _modalPresented = false;
                UiModalState.Pop();
            }
            var root = _uiDocument.rootVisualElement;
            root.Q<VisualElement>("flow-header")?.SetEnabled(true);
            root.Q<VisualElement>("browser-layout")?.SetEnabled(true);
            if (_focusBeforeModal is VisualElement previous && previous.panel != null)
                previous.Focus();
            else
                (_btnDirectConnect ?? _btnRefresh)?.Focus();
        }

        private async void JoinDirectConnect()
        {
            if (!_alive || _joining || _ipField == null)
                return;
            if (!TryParseServerAddress(_ipField.value, out var ip, out var port, out var error))
            {
                SetAddressStatus(error, true);
                _ipField.Focus();
                return;
            }

            _refreshCts?.Cancel();
            _operationVersion++;
            _refreshing = false;
            _joining = true;
            _addressCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
            var request = _addressCts;
            var ct = request.Token;
            _modalJoin?.SetEnabled(false);
            SetAddressStatus("Finding the registered server…", false);
            try
            {
                var chat = ChatSession.Instance;
                if (chat == null || !await chat.EnsureConnectedAsync())
                {
                    if (_alive && !ct.IsCancellationRequested)
                        SetAddressStatus(chat?.NeedsDisplayName == true
                            ? "Choose a display name on the HOME screen before browsing rooms."
                            : "Couldn’t reach the room directory. Check your connection, then retry.", true);
                    return;
                }
                _masterClient = chat.MasterClient;
                if (_masterClient == null)
                {
                    SetAddressStatus("Couldn’t reach the room directory. Check your connection, then retry.", true);
                    return;
                }
                var address = IPAddress.Parse(ip);
                if (!_alive || ct.IsCancellationRequested) return;
                var servers = await _masterClient.GetServersAsync(ct);
                if (!_alive || ct.IsCancellationRequested) return;
                var server = servers?.Find(candidate => candidate.Port == port &&
                    IPAddress.TryParse(candidate.IpAddress, out var candidateIp) &&
                    candidateIp.Equals(address));
                if (server == null)
                {
                    SetAddressStatus(servers == null
                        ? "Couldn’t load the room directory. Retry when you’re online."
                        : "No registered server at that address. Check the address with the host, then retry.", true);
                    return;
                }
                _joining = false;
                JoinServer(server);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerBrowser] Address lookup failed: {ex.Message}");
                if (_alive && !ct.IsCancellationRequested)
                    SetAddressStatus("Couldn’t find the server. Check your connection, then retry.", true);
            }
            finally
            {
                if (ReferenceEquals(_addressCts, request))
                {
                    _addressCts = null;
                    request.Dispose();
                    _joining = false;
                    _modalJoin?.SetEnabled(true);
                }
            }
        }

        private void SetAddressStatus(string text, bool error)
        {
            if (_directConnectStatus == null) return;
            _directConnectStatus.text = text;
            _directConnectStatus.EnableInClassList("error", error);
        }

        private void OnHostClicked()
        {
            if (!_alive || _hostStarting || _joining)
                return;
            _hostStarting = true;
            _hostHandedOff = false;
            _hostCts?.Cancel();
            _hostCts?.Dispose();
            _hostCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
            SetHostBusy(true, "Preparing your room…");
            StartHostingFlow(_hostCts.Token);
        }

        private async void StartHostingFlow(CancellationToken ct)
        {
            var host = ServerHost.Create();
            _ownedHost = host;
            string? authToken = null;
            long steamId = 0;
            try
            {
                var chat = ChatSession.Instance;
                ChatSession.ConfigureMasterServerUrl(_masterServerUrl);
                SetHostStatus("Signing in as a guest…", ct);
                bool authenticated = chat != null && await chat.EnsureConnectedAsync();
                if (!IsCurrentHost(ct))
                    return;
                if (!authenticated || chat?.AuthToken == null)
                {
                    FinishHostFailure(chat?.NeedsDisplayName == true
                        ? "Choose a display name on the HOME screen before hosting a room."
                        : "Couldn’t sign in to the room directory. Retry when you’re online.", ct);
                    return;
                }
                authToken = chat.AuthToken;
                steamId = chat.SteamId ?? 0;

                if (!IsCurrentHost(ct))
                    return;
                var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Guid serverId = Guid.Empty;
                string registrationError = string.Empty;
                int? crashCode = null;
                string crashStderr = string.Empty;
                Action<Guid> onRegistered = id => { serverId = id; registered.TrySetResult(true); };
                Action<string> onRegistrationFailed = reason => { registrationError = reason ?? string.Empty; registered.TrySetResult(false); };
                Action<int, string> onCrashed = (code, stderr) => { crashCode = code; crashStderr = stderr ?? string.Empty; registered.TrySetResult(false); };
                host.Registered += onRegistered;
                host.RegistrationFailed += onRegistrationFailed;
                host.Crashed += onCrashed;
                try
                {
                    string publicIp = _hostIpField?.value?.Trim() ?? string.Empty;
                    host.StartHosting(GenerateServerName(), string.IsNullOrEmpty(publicIp) ? null : publicIp);
                    SetHostStatus("Waiting for your room to appear…", ct);
                    var winner = await Task.WhenAny(registered.Task, Task.Delay(HostRegistrationTimeoutMs, ct));
                    if (!IsCurrentHost(ct))
                        return;
                    if (winner != registered.Task)
                    {
                        FinishHostFailure("Your room did not appear in time. Check your connection, then retry.", ct);
                        return;
                    }
                    if (serverId == Guid.Empty)
                    {
                        string detail = crashCode.HasValue
                            ? $"The server stopped (code {crashCode.Value}). {Truncate(crashStderr)}"
                            : $"The room directory rejected the server. {Truncate(registrationError)}";
                        FinishHostFailure(detail, ct);
                        return;
                    }

                    if (!IsCurrentHost(ct))
                        return;
                    // The persistent ServerHost is now owned by LobbyRoomUI. Do not stop it in OnDisable.
                    _hostHandedOff = true;
                    _hostStarting = false;
                    _ownedHost = null;
                    MatchConfig.Mode = GameMode.PvP;
                    MatchConfig.IsHost = true;
                    MatchConfig.ServerIP = "127.0.0.1";
                    MatchConfig.ServerPort = host.AssignedPort;
                    ClientSession.AuthToken = authToken;
                    ClientSession.SteamId = steamId;
                    ClientSession.SelectedServerId = serverId;
                    ClientSession.SelectedServerName = GenerateServerName();
                    // Host ownership transfer: the persistent ServerHost is now
                    // owned by the Lobby Room page (ADR-0005, ADR-0032). Page
                    // deactivation must not stop the handed-off process.
                    FrontendController.Show(FrontendPage.LobbyRoom);
                }
                finally
                {
                    host.Registered -= onRegistered;
                    host.RegistrationFailed -= onRegistrationFailed;
                    host.Crashed -= onCrashed;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // CancelHost/OnDisable performs the synchronous process cleanup.
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerBrowser] Host flow failed: {ex.Message}");
                if (IsCurrentHost(ct))
                    FinishHostFailure("Couldn’t start your room. Check your connection, then retry.", ct);
            }
            finally
            {
                if (IsCurrentHost(ct) && !_hostHandedOff && _ownedHost != null)
                {
                    _ownedHost.Stop();
                    _ownedHost = null;
                }
            }
        }

        private void CancelHost()
        {
            if (!_hostStarting)
                return;
            _hostCts?.Cancel();
            StopOwnedHost();
            _hostStarting = false;
            SetHostBusy(false, "Host setup cancelled. Start a room when you’re ready.");
        }

        private void FinishHostFailure(string message, CancellationToken operationToken)
        {
            if (!IsCurrentHost(operationToken))
                return;
            SetHostBusy(false, message);
            StopOwnedHost();
            _hostStarting = false;
        }

        private void StopOwnedHost()
        {
            if (_hostHandedOff || _ownedHost == null)
                return;
            _ownedHost.Stop();
            _ownedHost = null;
        }

        private void LeaveBrowser()
        {
            if (!_alive)
                return;
            _refreshCts?.Cancel();
            if (_hostStarting)
            {
                _hostCts?.Cancel();
                StopOwnedHost();
                _hostStarting = false;
            }
            FrontendController.Show(FrontendPage.Home);
        }

        private void SetHostBusy(bool busy, string status)
        {
            if (_btnHost != null) _btnHost.SetEnabled(!busy);
            if (_btnHostCancel != null)
            {
                _btnHostCancel.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
                _btnHostCancel.SetEnabled(busy);
            }
            if (_btnDirectConnect != null) _btnDirectConnect.SetEnabled(!busy);
            if (_lblHostStatus != null)
            {
                _lblHostStatus.style.display = DisplayStyle.Flex;
                _lblHostStatus.text = status;
            }
        }

        private void SetHostStatus(string status, CancellationToken operationToken)
        {
            if (IsCurrentHost(operationToken) && _lblHostStatus != null)
                _lblHostStatus.text = status;
        }

        private void SetBrowserActionsEnabled(bool enabled)
        {
            _btnRefresh?.SetEnabled(enabled);
            _btnHost?.SetEnabled(enabled && !_hostStarting);
            _btnDirectConnect?.SetEnabled(enabled && !_hostStarting);
        }

        private void SetBrowserStatus(string text, bool loading)
        {
            if (_lblStatus == null)
                return;
            // A pending return explanation stays visible until the scan
            // settles (issue #213).
            if (loading && _pendingReturnNotice != null)
                return;
            _lblStatus.style.display = DisplayStyle.Flex;
            _lblStatus.text = text;
            if (loading) _lblStatus.RemoveFromClassList("error");
        }

        private void ShowBrowserFailure(string text)
        {
            if (_lblStatus == null)
                return;
            if (_pendingReturnNotice != null)
            {
                text = $"{_pendingReturnNotice}\n{text}";
                _pendingReturnNotice = null;
            }
            _lblStatus.style.display = DisplayStyle.Flex;
            _lblStatus.text = text;
            _lblStatus.AddToClassList("error");
            _btnRefresh?.SetEnabled(true);
        }

        private bool IsCurrent(int operation, CancellationToken ct) =>
            _alive && operation == _operationVersion && !ct.IsCancellationRequested;

        private bool IsCurrentHost(CancellationToken ct) =>
            _alive && _hostStarting && _hostCts != null &&
            _hostCts.Token == ct && !ct.IsCancellationRequested;

        private void OnDisable()
        {
            _operationVersion++;
            _alive = false;
            if (_btnRefresh != null) _btnRefresh.clicked -= RefreshServers;
            if (_btnBack != null) _btnBack.clicked -= LeaveBrowser;
            if (_btnHost != null) _btnHost.clicked -= OnHostClicked;
            if (_btnDirectConnect != null) _btnDirectConnect.clicked -= OpenDirectConnect;
            if (_btnHostCancel != null) _btnHostCancel.clicked -= CancelHost;
            if (_modalClose != null) _modalClose.clicked -= CloseDirectConnect;
            if (_modalJoin != null) _modalJoin.clicked -= JoinDirectConnect;
            if (_modalPresented)
            {
                _modalPresented = false;
                UiModalState.Pop();
            }
            _addressCts?.Cancel();
            _addressCts?.Dispose();
            _addressCts = null;
            _lifecycleCts?.Cancel();
            _refreshCts?.Cancel();
            if (_hostStarting)
            {
                StopOwnedHost();
                _hostStarting = false;
            }
            _masterClient = null;
            // Page re-entry re-runs OnEnable against this same component, so
            // disposed sources must not survive the deactivation cycle
            // (issue #212): a stale disposed CTS would throw on the next
            // Cancel and break Back-during-pending.
            _lifecycleCts?.Dispose();
            _refreshCts?.Dispose();
            _hostCts?.Dispose();
            _lifecycleCts = null;
            _refreshCts = null;
            _hostCts = null;
        }

        private static bool TryParseServerAddress(string raw, out string host, out int port, out string error)
        {
            host = string.Empty;
            port = DefaultServerPort;
            error = "Enter an IPv4 address, with an optional port (for example 192.168.1.20:9876).";
            string value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                error = "Enter the server address before joining.";
                return false;
            }

            if (value[0] == '[')
            {
                int close = value.IndexOf(']');
                if (close <= 1)
                {
                    error = "Use an IPv4 address with an optional port, like 192.168.1.20:9876.";
                    return false;
                }
                host = value.Substring(1, close - 1);
                if (value.Length > close + 1)
                {
                    if (value[close + 1] != ':' || !int.TryParse(value.Substring(close + 2), out port))
                    {
                        error = "The optional port must be a number from 1 to 65535.";
                        return false;
                    }
                }
            }
            else
            {
                int firstColon = value.IndexOf(':');
                int lastColon = value.LastIndexOf(':');
                if (firstColon >= 0 && firstColon == lastColon)
                {
                    string maybePort = value.Substring(lastColon + 1);
                    if (!int.TryParse(maybePort, out port))
                    {
                        error = "The optional port must be a number from 1 to 65535.";
                        return false;
                    }
                    host = value.Substring(0, lastColon);
                }
                else
                {
                    host = value;
                }
            }

            if (port < 1 || port > 65535)
            {
                error = "The port must be between 1 and 65535.";
                return false;
            }
            if (!IPAddress.TryParse(host, out var address))
            {
                error = "Enter a valid IPv4 address. Hostnames are not supported by this client yet.";
                return false;
            }
            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                error = "This client uses IPv4 addresses. Enter an IPv4 address with an optional port.";
                return false;
            }
            return true;
        }

        private static string GenerateServerName() =>
            $"{Environment.MachineName}'s Server";

        private static string Truncate(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : (value.Length > 200 ? value.Substring(0, 200) + "…" : value);
    }
}
