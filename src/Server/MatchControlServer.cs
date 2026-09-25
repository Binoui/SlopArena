using System.Net;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
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
    /// pure, unit-tested seam), asks the orchestrator to assign a UDP port with the
    /// roster, and replies with <c>{ "port": N, "content": { ... } }</c>. This keeps
    /// the game server stateless between matches (ADR-0008): one shot in, one port out.
    ///
    /// VPS mode authenticates with a separate match-control key before reading
    /// a bounded body. The listener binds the private control network through
    /// Compose; TCP match control must never be publicly published there.
    /// </summary>
    public sealed class MatchControlServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly MultiMatchOrchestrator _orchestrator;
        private readonly string _defaultArena;
        private readonly byte[]? _controlKey;
        private const int MaxBodyBytes = 64 * 1024;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        /// <param name="orchestrator">Receives the parsed roster and assigns the match port.</param>
        /// <param name="port">TCP port to listen on (the game server's registered base port).</param>
        /// <param name="defaultArena">Arena used when the body omits one.</param>
        public MatchControlServer(MultiMatchOrchestrator orchestrator, int port, string defaultArena, string? controlKey = null)
        {
            _orchestrator = orchestrator;
            _defaultArena = defaultArena;
            _controlKey = controlKey is null ? null : Encoding.UTF8.GetBytes(controlKey);
            if (_controlKey?.Length > 4096)
                throw new ArgumentException("Control key is too long.", nameof(controlKey));
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
                catch (Exception)
                {
                    Console.WriteLine("[MatchControl] Match-start handler failed.");
                    try { ctx.Response.StatusCode = 500; } catch { }
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
        public (int port, MatchContentHandleMap? content, string? error) TryStartMatchWithContent(string jsonBody)
        {
            MatchStartRequest? req;
            try
            {
                using var doc = JsonDocument.Parse(jsonBody);
                req = MatchStartRequestCodec.TryParse(doc.RootElement);
            }
            catch (JsonException) { return (0, null, "Malformed JSON body."); }
            if (req is null)
                return (0, null, "Invalid match-start body (need matchId, arenaName, and 2-4 players with a known characterClass + entityId).");
            var arena = string.IsNullOrEmpty(req.ArenaName) ? _defaultArena : req.ArenaName;
            if (!_orchestrator.TryAssignMatch(req.MatchId, arena, req.Players, (byte)req.MaxStocks, out int port, out var content, out var error))
                return (0, null, error ?? "Failed to build match content.");
            return (port, content, null);
        }

        private static bool IsAuthorized(string? authorization, byte[] controlKey)
        {
            if (authorization is null || authorization.Length > 4103 ||
                !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return false;
            var token = authorization.AsSpan(7).Trim();
            if (Encoding.UTF8.GetByteCount(token) != controlKey.Length)
                return false;
            Span<byte> candidate = stackalloc byte[controlKey.Length];
            Encoding.UTF8.GetBytes(token, candidate);
            return CryptographicOperations.FixedTimeEquals(candidate, controlKey);
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST" || ctx.Request.Url?.AbsolutePath != "/match/start")
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            if (_controlKey is not null && !IsAuthorized(ctx.Request.Headers["Authorization"], _controlKey))
            {
                ctx.Response.StatusCode = 401;
                return;
            }

            if (ctx.Request.ContentLength64 > MaxBodyBytes)
            {
                ctx.Response.StatusCode = 413;
                return;
            }

            var bytes = ArrayPool<byte>.Shared.Rent(MaxBodyBytes + 1);
            string body;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                int length = 0, read;
                do
                {
                    read = await ctx.Request.InputStream.ReadAsync(
                        bytes.AsMemory(length, MaxBodyBytes + 1 - length), timeout.Token);
                    length += read;
                } while (read != 0 && length <= MaxBodyBytes);
                if (length > MaxBodyBytes)
                {
                    ctx.Response.StatusCode = 413;
                    return;
                }
                body = new UTF8Encoding(false, true).GetString(bytes, 0, length);
            }
            catch (OperationCanceledException)
            {
                ctx.Response.StatusCode = 408;
                return;
            }
            catch (DecoderFallbackException)
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            var result = TryStartMatchWithContent(body);
            ctx.Response.ContentType = "application/json";
            if (result.error is not null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.OutputStream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new { error = result.error }));
                return;
            }

            var ok = Encoding.UTF8.GetBytes($"{{\"port\":{result.port},\"content\":{MatchContentHandleMapCodec.Serialize(result.content!)}}}");
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
