# ADR-0004: Master Server Manages Lobbies via SignalR

**Status:** Accepted — 2026-08-01
**Deciders:** @Binoui

## Context

Players need a lobby room after connecting to a game server — a place to wait for opponents, see who's present, pick characters, and have the host start the match. Two options for where lobby state lives:

1. **Game server manages lobby (UDP)** — `MatchInstance` gains lobby/charselect/countdown/fight/results states. All lobby communication flows over the existing UDP game protocol. Master server only does server browser listing.
2. **Master server manages lobby (SignalR)** — The master server runs a SignalR hub for lobby state: player join/leave, host start, character selection sync, chat. The game server only handles the match itself (countdown → fight → results).

The user wants server chat and global chat soon. SignalR gives real-time updates natively and is already referenced in the master server's `Program.cs` (`builder.Services.AddSignalR()`).

## Decision

**Master server manages lobbies via SignalR.** The master server runs a `LobbyHub` that handles:

- Player join/leave lobby (associated with a game server)
- Player list updates (real-time push to all lobby members)
- Host controls (start match, kick, settings)
- Character selection sync (each player's pick broadcast to others)
- Chat (lobby-scoped and global) — not built now, but the hub is ready for it
- Match lifecycle signals (match started, match ended, return to lobby)

The game server (`MatchInstance`) receives match-start commands from the master server (via HTTP or SignalR backchannel) and runs the simulation. When the match ends, it signals the master server, which returns all players to the lobby.

## Consequences

- **New client dependency** — Unity client needs a SignalR client library (`Microsoft.AspNetCore.SignalR.Client`). This is a managed NuGet package, well-supported in Unity.
- **Chat comes for free** — adding chat later is just new SignalR methods on the same hub. No new transport.
- **Lobby state is not on the game server** — `MatchInstance` stays focused on simulation. It receives "start match with these players/characters" and runs.
- **Master server is the lobby authority** — if the master server goes down, lobbies dissolve. Game servers can still run in-progress matches to completion. Acceptable for a demo.
- **Two connections per client** — SignalR to master server (lobby/meta) + UDP to game server (match). This is standard for games (e.g., Rocket League, CS).
- **Global lobby list** — the master server can also serve a global "open lobbies" list, complementing the server browser.
