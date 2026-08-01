using System.Collections.Generic;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>
/// Deserializes SignalR lobby payloads (<see cref="JsonElement"/> from the
/// <c>On&lt;JsonElement&gt;</c> handlers) into the plain Shared DTOs.
///
/// The master server serializes with System.Text.Json's default camelCase policy
/// (ASP.NET Core SignalR), so wire keys are <c>steamId</c>, <c>name</c>,
/// <c>characterSelection</c>, <c>isHost</c>, <c>serverId</c>, <c>players</c>.
/// Kept dependency-free of any SignalR/Unity types so it is unit-testable from
/// <c>tests/Shared.Tests</c>.
/// </summary>
public static class LobbyPayloadCodec
{
    /// <summary>
    /// Parse a <c>LobbyPlayer</c>-shaped element. Returns null on any shape
    /// mismatch so a malformed push never crashes the UI thread.
    /// </summary>
    public static LobbyPlayerInfo? TryParsePlayer(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("steamId", out var steam) || steam.ValueKind != JsonValueKind.Number)
            return null;
        if (!element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            return null;
        if (!element.TryGetProperty("isHost", out var host) || host.ValueKind != JsonValueKind.False && host.ValueKind != JsonValueKind.True)
            return null;

        // characterSelection is nullable; treat absent/null/empty as null.
        string? selection = null;
        if (element.TryGetProperty("characterSelection", out var sel) &&
            sel.ValueKind == JsonValueKind.String)
        {
            selection = string.IsNullOrEmpty(sel.GetString()) ? null : sel.GetString();
        }

        return new LobbyPlayerInfo(
            steam.GetInt64(),
            name.GetString()!,
            selection,
            host.GetBoolean());
    }

    /// <summary>
    /// Parse a <c>LobbySnapshot</c>-shaped element: <c>{ serverId, players[] }</c>.
    /// Returns null on shape mismatch. An empty players array is valid.
    /// </summary>
    public static LobbySnapshot? TryParseSnapshot(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("serverId", out var sid) || sid.ValueKind != JsonValueKind.String)
            return null;
        if (!Guid.TryParse(sid.GetString(), out var serverId))
            return null;

        if (!element.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<LobbyPlayerInfo>();
        foreach (var item in players.EnumerateArray())
        {
            var p = TryParsePlayer(item);
            if (p is null)
                return null;
            list.Add(p);
        }

        return new LobbySnapshot(serverId, list);
    }

    /// <summary>
    /// Parse a <c>MatchStartingConfig</c>-shaped element: same shape as a
    /// snapshot (<c>{ serverId, players[] }</c>).
    /// </summary>
    public static MatchStartingConfig? TryParseMatchStarting(JsonElement element)
    {
        var snap = TryParseSnapshot(element);
        return snap is null ? null : new MatchStartingConfig(snap.ServerId, snap.Players);
    }
}
