using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>
/// Payload of the <see cref="SlopArena.Client.Network.LobbyClient.MatchStarted"/>
/// push broadcast when the host starts the actual match from char select
/// (ADR-0008, issue #34/#35). Carries the final roster with character classes,
/// the UDP port the match is running on, and the arena the game server loaded.
/// Clients connect to the game server (IP known from the server browser entry)
/// at <see cref="MatchPort"/>.
/// </summary>
/// <param name="MatchPort">UDP port the game server assigned to this match (0 while unknown).</param>
/// <param name="ArenaName">Arena the game server loaded for this match (empty until assigned).</param>
public sealed record MatchStartedConfig(
    Guid ServerId,
    IReadOnlyList<LobbyPlayerInfo> Players,
    int MatchPort = 0,
    string ArenaName = "",
    int MaxStocks = MatchDefaults.DefaultMaxStocks);
