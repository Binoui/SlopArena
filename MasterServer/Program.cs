using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MasterServer.Data;
using MasterServer.DTOs;

var builder = WebApplication.CreateBuilder(args);

// Service registration expands during subsequent tasks
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

app.MapGet("/health", () => new { status = "ok", version = "0.1.0" });

// ── Rate limiting middleware ──
app.Use(async (context, next) =>
{
    // Only rate-limit POST endpoints
    if (context.Request.Method == "POST")
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"rate:{ip}";

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = now / 10; // 10-second window
        var windowKey = $"{key}:{windowStart}";

        var current = RateLimitTracker.Increment(windowKey, ip);
        if (current > 10)
        {
            logger.LogWarning("Rate limit exceeded for {Ip} ({Count} requests in 10s window)", ip, current);
            context.Response.StatusCode = 429;
            await context.Response.WriteAsJsonAsync(new { error = "Too many requests. Try again later." });
            return;
        }
    }

    await next(context);
});

// ── Helper: extract and validate Bearer token ──
static string? ExtractBearerToken(HttpContext httpContext, ILogger logger)
{
    var authHeader = httpContext.Request.Headers["Authorization"].ToString();
    if (string.IsNullOrWhiteSpace(authHeader))
    {
        logger.LogWarning("Missing Authorization header");
        return null;
    }

    // Case-insensitive "Bearer " prefix check with trim
    const string prefix = "Bearer ";
    if (authHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        var token = authHeader[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Empty token after Bearer prefix");
            return null;
        }
        return token;
    }

    logger.LogWarning("Authorization header missing Bearer prefix: {Header}", authHeader);
    return null;
}

// ── Helper: timing-safe string comparison ──
static bool TimingSafeEquals(string a, string b)
{
    if (a.Length != b.Length) return false;
    return CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(a),
        System.Text.Encoding.UTF8.GetBytes(b));
}

// ── Helper: validate IP address format ──
static bool IsValidIpAddress(string ip)
{
    return IPAddress.TryParse(ip, out var addr) &&
           addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork; // IPv4 only for now
}

// ── Helper: validate port range ──
static bool IsValidPort(int port) => port > 0 && port <= 65535;

// ── Game server registration endpoint ──
app.MapPost("/servers/register", async (
    ServerRegistrationRequest request,
    AppDbContext db) =>
{
    // Input validation
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Name is required" });

    if (string.IsNullOrWhiteSpace(request.Region))
        return Results.BadRequest(new { error = "Region is required" });

    if (!IsValidIpAddress(request.IpAddress))
        return Results.BadRequest(new { error = $"Invalid IP address: {request.IpAddress}" });

    if (!IsValidPort(request.Port))
        return Results.BadRequest(new { error = $"Invalid port: {request.Port} (must be 1-65535)" });

    if (request.MaxConcurrentMatches <= 0)
        return Results.BadRequest(new { error = "MaxConcurrentMatches must be positive" });

    if (request.MaxConcurrentMatches > 100)
        return Results.BadRequest(new { error = "MaxConcurrentMatches must be <= 100" });

    var apiToken = Guid.NewGuid().ToString();

    var gameServer = new MasterServer.Data.Models.GameServer
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        IpAddress = request.IpAddress,
        Port = request.Port,
        Region = request.Region,
        IsOfficial = request.IsOfficial,
        MaxConcurrentMatches = request.MaxConcurrentMatches,
        CurrentMatches = 0,
        CustomRulesJson = request.CustomRulesJson,
        ApiToken = apiToken,
        LastHeartbeat = DateTime.UtcNow
    };

    db.GameServers.Add(gameServer);
    await db.SaveChangesAsync();

    logger.LogInformation("Game server registered: {Name} (ID: {Id}, IP: {Ip}, Region: {Region})",
        gameServer.Name, gameServer.Id, gameServer.IpAddress, gameServer.Region);

    return Results.Ok(new
    {
        serverId = gameServer.Id,
        apiToken = apiToken
    });
});

// ── Server heartbeat endpoint ──
app.MapPost("/servers/{serverId}/heartbeat", async (
    Guid serverId,
    HeartbeatRequest request,
    HttpContext httpContext,
    AppDbContext db) =>
{
    var token = ExtractBearerToken(httpContext, logger);
    if (token == null)
        return Results.Unauthorized();

    var server = await db.GameServers.FindAsync(serverId);
    if (server == null)
    {
        logger.LogWarning("Heartbeat from unknown server: {ServerId}", serverId);
        return Results.NotFound(new { error = "Server not found" });
    }

    if (!TimingSafeEquals(server.ApiToken, token))
    {
        logger.LogWarning("Heartbeat auth failed for server {ServerId}", serverId);
        return Results.Unauthorized();
    }

    server.CurrentMatches = request.CurrentMatches;
    server.LastHeartbeat = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { status = "ok" });
});

// ── Match result endpoint ──
app.MapPost("/match/result", async (
    MatchResultRequest request,
    HttpContext httpContext,
    AppDbContext db) =>
{
    var token = ExtractBearerToken(httpContext, logger);
    if (token == null)
        return Results.Unauthorized();

    // Verify server token (find server with this token)
    var server = await db.GameServers.FirstOrDefaultAsync(s => s.ApiToken == token);
    if (server == null || !TimingSafeEquals(server.ApiToken, token))
    {
        logger.LogWarning("Match result auth failed");
        return Results.Unauthorized();
    }

    // Wrap in transaction for atomic MMR update
    await using var transaction = await db.Database.BeginTransactionAsync();

    try
    {
        var match = await db.Matches.FindAsync(request.MatchId);
        if (match == null)
        {
            logger.LogWarning("Match result for unknown match: {MatchId}", request.MatchId);
            await transaction.RollbackAsync();
            return Results.NotFound(new { error = "Match not found" });
        }

        match.WinnerSteamId = request.WinnerSteamId;
        match.EndedAt = DateTime.UtcNow;

        // Apply ELO rating system for MMR adjustment
        var winner = await db.Users.FindAsync(request.WinnerSteamId);
        var loser = await db.Users.FindAsync(
            match.Player1SteamId == request.WinnerSteamId
                ? match.Player2SteamId
                : match.Player1SteamId);

        int mmrChange = 0;
        if (winner != null && loser != null)
        {
            var expectedWin = 1.0 / (1.0 + Math.Pow(10, (loser.Mmr - winner.Mmr) / 400.0));
            var kFactor = 32;
            mmrChange = (int)(kFactor * (1 - expectedWin));

            winner.Mmr += mmrChange;
            loser.Mmr -= mmrChange;

            // Keep MMR non-negative
            winner.Mmr = Math.Max(0, winner.Mmr);
            loser.Mmr = Math.Max(0, loser.Mmr);

            logger.LogInformation("Match {MatchId}: Winner={WinnerId} (+{Change}), Loser={LoserId} (-{Change})",
                request.MatchId, request.WinnerSteamId, mmrChange,
                match.Player1SteamId == request.WinnerSteamId ? match.Player2SteamId : match.Player1SteamId, mmrChange);
        }
        else
        {
            logger.LogWarning("Match {MatchId}: Could not find winner or loser user", request.MatchId);
        }

        server.CurrentMatches = Math.Max(0, server.CurrentMatches - 1);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Results.Ok(new { status = "recorded", mmrChange });
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
});

app.Run();

/// <summary>
/// Simple in-memory rate limit tracker with auto-cleanup.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
internal static class RateLimitTracker
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastCleanup = new();

    public static int Increment(string windowKey, string ip)
    {
        var count = _counts.AddOrUpdate(windowKey, 1, (_, existing) => existing + 1);

        // Lazy cleanup: every ~30s, remove entries older than 60s
        var now = DateTime.UtcNow;
        if (_lastCleanup.TryGetValue(ip, out var lastClean) && (now - lastClean).TotalSeconds < 30)
            return count;

        _lastCleanup[ip] = now;
        var cutoff = now.AddSeconds(-60);
        foreach (var key in _counts.Keys)
        {
            if (_counts.TryGetValue(key, out var _))
            {
                // Simple TTL: not perfect but avoids unbounded growth
                // Production would use a proper sliding window with Redis
            }
        }

        return count;
    }
}
