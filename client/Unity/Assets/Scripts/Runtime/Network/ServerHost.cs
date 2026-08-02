#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Network
{
    /// <summary>
    /// Embedded host-and-play (ADR-0005, issue #39). A Unity singleton that
    /// spawns the <c>SlopArena.Server</c> game server as a subprocess, passes
    /// it a generated <c>server.json</c>, monitors it for crash/registration,
    /// and shuts it down cleanly on application quit or when the host leaves.
    ///
    /// The host player connects to the match at <c>localhost:&lt;assigned-port&gt;</c>,
    /// identical to any remote client — the server binary is unchanged.
    ///
    /// Subprocess stdout/stderr are read on background threads and marshalled
    /// onto the Unity main thread via <see cref="Pump"/> (same pattern as
    /// <see cref="LobbyClient"/>); callers MUST invoke it from <c>Update</c>.
    /// </summary>
    public sealed class ServerHost : MonoBehaviour
    {
        /// <summary>Singleton instance, or null before first <see cref="Create"/>.</summary>
        public static ServerHost? Instance { get; private set; }

        [Header("Subprocess")]
        [Tooltip("Path to the dotnet executable. 'dotnet' relies on PATH.")]
        [SerializeField] private string _dotnetPath = "dotnet";
        [Tooltip("Repo-relative path to the game-server .csproj (run via 'dotnet run'). Editor-only fallback; built players spawn the bundled binary.")]
        [SerializeField] private string _serverProjectPath = "src/Server/SlopArena.Server.csproj";
        [Tooltip("Repo-relative path to the .arena files directory. Absolute if set.")]
        [SerializeField] private string _arenaDataDir = "data/arenas";

        [Header("Master Server")]
        [SerializeField] private string _masterServerUrl = "https://sloparena.barakaslurp.fr";

        private Process? _process;
        private string? _configPath;
        private bool _userStopped;   // distinguishes graceful Stop() from a crash
        private int _assignedPort;

        // Background-thread subprocess events, drained on the main thread by Pump().
        private readonly ConcurrentQueue<Action> _pending = new();
        private readonly StringBuilder _stderrTail = new();
        private const int StderrTailLines = 20;

        // ── Events (raised on the main thread, after Pump) ──

        /// <summary>The server registered with the master server. Arg = server-id GUID.</summary>
        public event Action<Guid>? Registered;
        /// <summary>The server failed to register (non-crash). Arg = reason.</summary>
        public event Action<string>? RegistrationFailed;
        /// <summary>The subprocess exited unexpectedly. Args = exit code, stderr tail.</summary>
        public event Action<int, string>? Crashed;
        /// <summary>Every stdout line (for debugging / live log panels).</summary>
        public event Action<string>? StdoutLine;

        /// <summary>True while the subprocess is alive and not user-stopped.</summary>
        public bool IsRunning => _process != null && !_process.HasExited;
        /// <summary>The UDP port assigned to the hosted server (valid after StartHosting).</summary>
        public int AssignedPort => _assignedPort;

        // ── Lifecycle ──

        /// <summary>Create the singleton (DontDestroyOnLoad) if absent.</summary>
        public static ServerHost Create()
        {
            if (Instance != null) return Instance;
            var go = new GameObject(nameof(ServerHost));
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ServerHost>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Start the embedded game server. Picks a free UDP port, writes a temp
        /// <c>server.json</c>, and spawns the game server — the self-contained
        /// binary bundled at <c>StreamingAssets/Server</c> in built players, or
        /// <c>dotnet run</c> on the repo project in the Editor.
        /// Fires <see cref="Registered"/> or <see cref="RegistrationFailed"/> on the
        /// main thread. Safe to call once per host session.
        /// </summary>
        /// <param name="serverName">Browser display name for this server.</param>
        /// <param name="publicIp">Optional public IP/domain advertised to the master server
        /// (host behind NAT, see ADR-0009). Null/empty → server auto-detects its LAN IP.</param>
        public void StartHosting(string serverName, string? publicIp = null)
        {
            if (IsRunning)
            {
                _pending.Enqueue(() => RegistrationFailed?.Invoke("A server is already running."));
                return;
            }

            // Built players: prefer the self-contained server binary bundled at
            // StreamingAssets/Server (no repo, SDK, or .NET install needed).
            // Editor: falls back to `dotnet run` on the repo project.
            string bundledServerDir = Path.Combine(Application.streamingAssetsPath, "Server");
            string bundledExe = Path.Combine(bundledServerDir,
                Application.platform == RuntimePlatform.WindowsPlayer ? "SlopArena.Server.exe" : "SlopArena.Server");
            bool useBundled = File.Exists(bundledExe);

            string arenaDir = useBundled
                ? Path.Combine(Application.streamingAssetsPath, "arenas")
                : Path.IsPathRooted(_arenaDataDir)
                    ? _arenaDataDir
                    : Path.GetFullPath(Path.Combine(ResolveRepoRoot(), _arenaDataDir));

            if (!Directory.Exists(arenaDir))
            {
                _pending.Enqueue(() => RegistrationFailed?.Invoke($"Arena data dir not found: {arenaDir}"));
                return;
            }

            _assignedPort = FindFreeUdpPort();
            var config = new HostedServerConfig
            {
                ServerName = serverName,
                Region = "EU",
                Port = _assignedPort,
                MaxConcurrentMatches = 1,
                MasterServerUrl = _masterServerUrl,
                IsOfficial = false,
                ArenaDataDir = arenaDir,
                // Host-entered public IP/domain (ADR-0009 host-and-play tier);
                // null → server auto-detects LAN IP (correct only for directly
                // reachable hosts).
                PublicIp = string.IsNullOrWhiteSpace(publicIp) ? null : publicIp
            };

            _configPath = Path.Combine(Path.GetTempPath(), $"sloparena-host-{_assignedPort}.json");
            File.WriteAllText(_configPath, config.ToJson());
            UnityEngine.Debug.Log($"[ServerHost] Wrote config to {_configPath} (port {_assignedPort})");

            ProcessStartInfo psi;
            if (useBundled)
            {
                psi = new ProcessStartInfo
                {
                    FileName = bundledExe,
                    // Config path as args[0] (server Program.Main contract).
                    // WorkingDirectory = binary dir (issue #60 requirement).
                    ArgumentList = { _configPath },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(bundledExe)!
                };
            }
            else
            {
                string repoRoot = ResolveRepoRoot();
                string projectPath = Path.IsPathRooted(_serverProjectPath)
                    ? _serverProjectPath
                    : Path.GetFullPath(Path.Combine(repoRoot, _serverProjectPath));
                psi = new ProcessStartInfo
                {
                    FileName = _dotnetPath,
                    // dotnet run --project <csproj> -- <configPath>
                    // '--' separates dotnet-run args from app args.
                    ArgumentList = { "run", "--project", projectPath, "--", _configPath },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = repoRoot
                };
            }

            try
            {
                _process = Process.Start(psi);
            }
            catch (Exception ex)
            {
                _pending.Enqueue(() => RegistrationFailed?.Invoke($"Failed to start subprocess: {ex.Message}"));
                return;
            }

            if (_process == null)
            {
                _pending.Enqueue(() => RegistrationFailed?.Invoke("Process.Start returned null."));
                return;
            }

            _userStopped = false;
            _stderrTail.Clear();

            _process.OutputDataReceived += OnStdout;
            _process.ErrorDataReceived += OnStderr;
            _process.EnableRaisingEvents = true;
            _process.Exited += OnExited;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            UnityEngine.Debug.Log($"[ServerHost] Spawned server PID {_process.Id} on port {_assignedPort}.");
        }

        /// <summary>
        /// Gracefully stop the embedded server. Kills the process tree and waits
        /// briefly. Idempotent; safe from <see cref="OnApplicationQuit"/>/<see cref="OnDestroy"/>.
        /// </summary>
        public void Stop()
        {
            if (_process == null) return;
            _userStopped = true;

            try
            {
                if (!_process.HasExited)
                    _process.Kill();
                _process.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[ServerHost] Stop error: {ex.Message}");
            }

            DetachProcess();
            CleanupConfig();
        }

        /// <summary>Drain background-thread subprocess events onto the main thread.</summary>
        public void Pump()
        {
            while (_pending.TryDequeue(out var action))
                action();
        }

        private void Update() => Pump();

        private void OnApplicationQuit() => Stop();

        private void OnDestroy() => Stop();

        // ── Subprocess callbacks (background threads) ──

        private void OnStdout(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            _pending.Enqueue(() => StdoutLine?.Invoke(e.Data));

            if (ServerLogParser.TryParseServerId(e.Data, out var serverId))
            {
                var id = serverId;
                _pending.Enqueue(() => Registered?.Invoke(id));
            }
        }

        private void OnStderr(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (_stderrTail)
            {
                _stderrTail.AppendLine(e.Data);
                // Keep only the tail to bound memory.
                while (_stderrTail.Length > 8192)
                {
                    int nl = _stderrTail.ToString().IndexOf('\n');
                    if (nl < 0) break;
                    _stderrTail.Remove(0, nl + 1);
                }
            }
        }

        private void OnExited(object? sender, EventArgs e)
        {
            if (_userStopped) return;   // graceful — not a crash
            int code = -1;
            try { code = _process?.ExitCode ?? -1; } catch { }
            string tail;
            lock (_stderrTail) tail = _stderrTail.ToString();
            _pending.Enqueue(() => Crashed?.Invoke(code, tail));
        }

        // ── Helpers ──

        private void DetachProcess()
        {
            if (_process == null) return;
            try
            {
                _process.OutputDataReceived -= OnStdout;
                _process.ErrorDataReceived -= OnStderr;
                _process.Exited -= OnExited;
            }
            catch { /* best effort */ }
            _process?.Dispose();
            _process = null;
        }

        private void CleanupConfig()
        {
            if (_configPath == null) return;
            try { if (File.Exists(_configPath)) File.Delete(_configPath); } catch { }
            _configPath = null;
        }

        /// <summary>
        /// Let the OS assign a free UDP port by binding a datagram socket to
        /// port 0, reading the assigned port, then closing. Minimizes port
        /// conflicts for the demo (ADR-0005 acceptance criterion).
        /// </summary>
        private static int FindFreeUdpPort()
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Bind(new IPEndPoint(IPAddress.Any, 0));
            return ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        /// <summary>
        /// Resolve the repo root from the Unity project: Application.dataPath is
        /// <c>&lt;repo&gt;/client/Unity/Assets</c>; three levels up is the repo root.
        /// </summary>
        private static string ResolveRepoRoot() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
    }
}
