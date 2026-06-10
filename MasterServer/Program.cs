using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

app.MapGet("/health", () => new { status = "ok", version = "0.1.0" });

// Game server registration endpoint
app.MapPost("/servers/register", async (
    ServerRegistrationRequest request,
    AppDbContext db) =>
{
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

    return Results.Ok(new
    {
        serverId = gameServer.Id,
        apiToken = apiToken
    });
});

// Server heartbeat endpoint
app.MapPost("/servers/{serverId}/heartbeat", async (
    Guid serverId,
    HeartbeatRequest request,
    HttpContext httpContext,
    AppDbContext db) =>
{
    var token = httpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

    var server = await db.GameServers.FindAsync(serverId);
    if (server == null)
    {
        return Results.NotFound(new { error = "Server not found" });
    }

    if (server.ApiToken != token)
    {
        return Results.Unauthorized();
    }

    server.CurrentMatches = request.CurrentMatches;
    server.LastHeartbeat = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { status = "ok" });
});

// Match result endpoint
app.MapPost("/match/result", async (
    MatchResultRequest request,
    HttpContext httpContext,
    AppDbContext db) =>
{
    var token = httpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

    // Verify server token (find server with this token)
    var server = await db.GameServers.FirstOrDefaultAsync(s => s.ApiToken == token);
    if (server == null)
    {
        return Results.Unauthorized();
    }

    // Update match record
    var match = await db.Matches.FindAsync(request.MatchId);
    if (match == null)
    {
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
    }

    await db.SaveChangesAsync();

    server.CurrentMatches = Math.Max(0, server.CurrentMatches - 1);
    await db.SaveChangesAsync();

    return Results.Ok(new { status = "recorded", mmrChange });
});

app.Run();
