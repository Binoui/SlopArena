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

        // lockedIn is optional (older master servers may not send it); default false.
        bool lockedIn = false;
        if (element.TryGetProperty("lockedIn", out var li) &&
            (li.ValueKind == JsonValueKind.False || li.ValueKind == JsonValueKind.True))
        {
            lockedIn = li.GetBoolean();
        }

        // entityId is optional (only sent on the MatchStarted push, issue #35);
        // default 0 when absent (lobby/char-select snapshots).
        int entityId = 0;
        if (element.TryGetProperty("entityId", out var eid) &&
            eid.ValueKind == JsonValueKind.Number)
        {
            entityId = eid.GetInt32();
        }

        return new LobbyPlayerInfo(
            steam.GetInt64(),
            name.GetString()!,
            selection,
            lockedIn,
            host.GetBoolean(),
            entityId);
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

    /// <summary>
    /// Parse a <c>MatchStartedConfig</c>-shaped element: a snapshot
    /// (<c>{ serverId, players[] }</c>) plus the match-start fields
    /// <c>matchPort</c> (int) and <c>arenaName</c> (string) added by issue #35.
    /// Both are optional (default 0 / "") so older pushes still parse.
    /// </summary>
    public static MatchStartedConfig? TryParseMatchStarted(JsonElement element)
    {
        var snap = TryParseSnapshot(element);
        if (snap is null) return null;
        int matchPort = 0;
        if (element.TryGetProperty("matchPort", out var mp))
        {
            if (mp.ValueKind != JsonValueKind.Number || !mp.TryGetInt32(out matchPort) || matchPort < 0) return null;
        }
        string arenaName = string.Empty;
        if (element.TryGetProperty("arenaName", out var an))
        {
            if (an.ValueKind != JsonValueKind.String) return null;
            arenaName = an.GetString() ?? string.Empty;
        }
        int maxStocks = MatchDefaults.DefaultMaxStocks;
        if (element.TryGetProperty("maxStocks", out var ms))
        {
            if (ms.ValueKind != JsonValueKind.Number || !ms.TryGetInt32(out maxStocks) || maxStocks < 1 || maxStocks > 99) return null;
        }
        MatchContentHandleMap? content = null;
        if (matchPort > 0)
        {
            if (!element.TryGetProperty("content", out var c) || !MatchContentHandleMapCodec.TryParse(c, out content) || content == null) return null;
        }
        else if (element.TryGetProperty("content", out var localContent) && !MatchContentHandleMapCodec.TryParse(localContent, out content)) return null;
        return new MatchStartedConfig(snap.ServerId, snap.Players, matchPort, arenaName, maxStocks, content);
    }
}
