namespace SlopArena.Shared;

/// <summary>
/// A player present in a SignalR lobby, mirroring the master server's
/// <c>MasterServer.Lobbies.LobbyPlayer</c> wire format (ADR-0004, issue #33).
/// Lobby state lives on the master server; the client only mirrors snapshots
/// pushed via <c>LobbyUpdated</c>/<c>PlayerJoined</c>/<c>MatchStarting</c>.
/// </summary>
/// <param name="SteamId">Guest SteamId assigned by the master server.</param>
/// <param name="Name">Display name (guest username, e.g. "Guest-12345").</param>
/// <param name="CharacterSelection">
/// Picked character class name, or null while not yet chosen (char-select
/// lands in a later ticket; the hub always sends null for now).
/// </param>
/// <param name="IsHost">True for the lobby host (first joiner; promoted on leave).</param>
public sealed record LobbyPlayerInfo(
    long SteamId,
    string Name,
    string? CharacterSelection,
    bool IsHost);
