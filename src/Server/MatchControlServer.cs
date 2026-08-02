using System.Net;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.Server
{
    /// <summary>
    /// HTTP control server on the game server (issue #35). Listens on TCP
    /// <c>config.Port</c> (the registered base port; UDP matches bind
    /// <c>config.Port + offset</c>, so TCP and UDP coexist on the same number)
    /// and exposes <c>POST /match/start</c> for the master server.
    ///
    /// The handler parses the body with <see cref="MatchStartRequestCodec"/> (the
    /// pure, unit-tested seam), asks the orchestrator to assign a UDP port with
    /// the roster, and replies with <c>{ "port": N }</c>. This keeps the game
    /// server stateless between matches (ADR-0008): one shot in, one port out.
    ///
    /// The prefix binds all interfaces (<c>http://*:{port}/</c>) because the
    /// master server POSTs to the LAN IP returned by
    /// <c>GameServerRegistration.GetPublicIpAddress()</c> — a loopback-only
    /// listener would be connection-refused on the real host.
    /// </summary>
    public sealed class MatchControlServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly MultiMatchOrchestrator _orchestrator;
        private readonly string _defaultArena;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        /// <param name="orchestrator">Receives the parsed roster and assigns the match port.</param>
        /// <param name="port">TCP port to listen on (the game server's registered base port).</param>
        /// <param name="defaultArena">Arena used when the body omits one.</param>
        public MatchControlServer(MultiMatchOrchestrator orchestrator, int port, string defaultArena)
        {
            _orchestrator = orchestrator;
            _defaultArena = defaultArena;
            // '*' binds all interfaces on Linux (no Windows urlacl needed).
            _listener.Prefixes.Add($"http://*:{port}/");
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener.Start();
            _ = Task.Run(() => RunAsync(_cts.Token));
            Console.WriteLine($"[MatchControl] Listening for match-start on TCP port {_listener.Prefixes.First()}");
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }

                try
                {
                    await HandleAsync(ctx);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MatchControl] Handler error: {ex.Message}");
                    try { ctx.Response.StatusCode = 500; await ctx.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}")); } catch { }
                }
                finally
                {
                    try { ctx.Response.Close(); } catch { }
                }
            }
        }

        /// <summary>
        /// Pure handler (extracted for testability): parse + assign, no socket
        /// I/O. Returns the assigned UDP port (>=1) or 0 on a bad request, with a
        /// human-readable error. The caller is responsible for writing the HTTP
        /// response. Throws on an internal error (orchestrator failure).
        /// </summary>
        public (int port, string? error) TryStartMatch(string jsonBody)
        {
            MatchStartRequest? req;
            try
            {
                using var doc = JsonDocument.Parse(jsonBody);
                req = MatchStartRequestCodec.TryParse(doc.RootElement);
            }
            catch (JsonException)
            {
                return (0, "Malformed JSON body.");
            }

            if (req is null)
                return (0, "Invalid match-start body (need matchId, arenaName, and 2-4 players with a known characterClass + entityId).");

            var arena = string.IsNullOrEmpty(req.ArenaName) ? _defaultArena : req.ArenaName;
            int port = _orchestrator.AssignMatch(req.MatchId, arena, req.Players);
            if (port < 0)
                return (0, "No match slots available on this game server.");

            return (port, null);
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (!ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || !path.EndsWith("/match/start", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            string body;
            using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                body = await sr.ReadToEndAsync();

            var (port, error) = TryStartMatch(body);
            if (error is not null)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                var errBytes = System.Text.Encoding.UTF8.GetBytes($"{{\"error\":\"{error}\"}}");
                await ctx.Response.OutputStream.WriteAsync(errBytes);
                return;
            }

            ctx.Response.ContentType = "application/json";
            var ok = System.Text.Encoding.UTF8.GetBytes($"{{\"port\":{port}}}");
            await ctx.Response.OutputStream.WriteAsync(ok);
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
            ((IDisposable)_listener).Dispose();
        }
    }
}
