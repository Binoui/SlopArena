using System.Collections.Generic;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>
/// A player in the master server's match-start command to the game server
/// (ADR-0008, issue #35). The master server assigns <see cref="EntityId"/>
/// (1..N by lobby join order) and sends the locked-in character class so the
/// game server spawns the right entity instead of hardcoded Manki.
/// </summary>
/// <param name="SteamId">Guest SteamId (identifies the player across master + game server).</param>
/// <param name="CharacterClass">Locked-in character class the game server must spawn.</param>
/// <param name="EntityId">Server entity ID (1..N) the player drives and the server broadcasts state for.</param>
public sealed record MatchPlayer(long SteamId, CharacterClass CharacterClass, int EntityId);

/// <summary>
/// Body of the master server's <c>POST /match/start</c> call to the game server
/// (ADR-0008, issue #35). The game server spawns one entity per player with the
/// given character class + entity ID, runs the match on a dedicated UDP port,
/// and replies with that port so the master server can broadcast it to clients.
/// </summary>
/// <param name="MatchId">Opaque match identifier (for logging + orchestrator bookkeeping).</param>
/// <param name="ArenaName">Arena the game server should load for this match.</param>
/// <param name="Players">Ordered roster (index 0 = host) the game server spawns.</param>
/// <param name="MaxStocks">Stocks per player (default 3, issue #37).</param>
public sealed record MatchStartRequest(string MatchId, string ArenaName, IReadOnlyList<MatchPlayer> Players, int MaxStocks = MatchDefaults.DefaultMaxStocks);

/// <summary>
/// Parses the <c>POST /match/start</c> JSON body (<see cref="MatchStartRequest"/>).
/// Pure + dependency-free so it is unit-testable from <c>tests/Shared.Tests</c>
/// without the game server runtime. Wire keys are camelCase
/// (<c>matchId</c>, <c>arenaName</c>, <c>players</c>, <c>steamId</c>,
/// <c>characterClass</c>, <c>entityId</c>), matching System.Text.Json's default
/// policy on the master server.
/// </summary>
public static class MatchStartRequestCodec
{
    /// <summary>
    /// Parse a <see cref="MatchStartRequest"/>-shaped element. Returns null on
    /// any shape mismatch (including an unrecognised character class) so a
    /// malformed command never spawns a partial match.
    /// </summary>
    public static MatchStartRequest? TryParse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("matchId", out var mid) || mid.ValueKind != JsonValueKind.String)
            return null;
        string matchId = mid.GetString()!;
        if (string.IsNullOrEmpty(matchId))
            return null;

        // arenaName is optional: a missing/empty value is treated as "" so the
        // game server can apply its own default arena (issue #35 review). Only
        // a present-but-non-string value is malformed.
        string arenaName = string.Empty;
        if (element.TryGetProperty("arenaName", out var arena))
        {
            if (arena.ValueKind != JsonValueKind.String)
                return null;
            arenaName = arena.GetString() ?? string.Empty;
        }

        if (!element.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<MatchPlayer>();
        foreach (var item in players.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            if (!item.TryGetProperty("steamId", out var steam) || steam.ValueKind != JsonValueKind.Number)
                return null;

            if (!item.TryGetProperty("characterClass", out var cc) || cc.ValueKind != JsonValueKind.String)
                return null;
            string classStr = cc.GetString()!;
            if (string.IsNullOrEmpty(classStr))
                return null;
            // Case-insensitive enum parse; reject unknown classes so a typo can't
            // silently spawn Manki (the exact bug this ticket fixes).
            if (!System.Enum.TryParse<CharacterClass>(classStr, ignoreCase: true, out var characterClass))
                return null;
            if (characterClass == CharacterClass.None)
                return null;

            if (!item.TryGetProperty("entityId", out var eid) || eid.ValueKind != JsonValueKind.Number)
                return null;
            int entityId = eid.GetInt32();
            if (entityId <= 0)
                return null;

            list.Add(new MatchPlayer(steam.GetInt64(), characterClass, entityId));
        }

        if (list.Count is < 2 or > 4)
            return null;

        // maxStocks is optional (absent → default 3, issue #37). Present but
        // non-numeric (incl. fractional/out-of-int-range) or out of the [1,99]
        // byte range is malformed — never throw, per the codec's null contract.
        int maxStocks = 3;
        if (element.TryGetProperty("maxStocks", out var ms))
        {
            // Number-kind check first: TryGetInt32 throws on non-numeric kinds
            // (e.g. a string), and returns false for fractional/overflow values.
            if (ms.ValueKind != JsonValueKind.Number
                || !ms.TryGetInt32(out maxStocks)
                || maxStocks < 1 || maxStocks > 99)
                return null;
        }

        return new MatchStartRequest(matchId, arenaName, list, maxStocks);
    }
}
