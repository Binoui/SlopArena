# ADR-0008: Lobby Room Match Flow

**Status:** Accepted — 2026-08-01
**Deciders:** @Binoui

## Context

The match lifecycle needs a defined flow from server browser to match and back. The current `MatchInstance` has countdown → fight → 3-death → post-match, but no lobby state, no character select, and the client ignores `MatchState` entirely.

## Decision

**Match flow: Server Browser → Lobby Room → Character Select → Countdown → Fight → Results → Lobby Room**

States managed by the master server SignalR hub (ADR-0004) and the game server:

```
┌─────────────┐     ┌─────────────┐     ┌──────────────┐     ┌───────────┐
│ Server      │────▶│ Lobby Room  │────▶│ Char Select  │────▶│ Countdown │
│ Browser     │     │ (SignalR)   │     │ (SignalR)    │     │ (Game Srv)│
└─────────────┘     └─────┬───────┘     └──────────────┘     └─────┬─────┘
                          │                                        │
                          │◀────────────────────────────────────────┤
                          │                                        ▼
                          │           ┌──────────┐           ┌───────────┐
                          └───────────│ Results  │◀──────────│  Fight    │
                                      │(SignalR) │           │(Game Srv) │
                                      └──────────┘           └───────────┘
```

1. **Server Browser** — client queries master server `GET /servers`, picks a server (or hosts one).
2. **Lobby Room** — players join the SignalR lobby for that server. See player list. Host can start.
3. **Character Select** — host presses "Start." All players in the lobby pick characters simultaneously. Selections sync via SignalR.
4. **Countdown** — master server signals game server to start the match with the player list + character classes. Game server runs 3-second countdown (180 ticks).
5. **Fight** — game server simulates. Broadcasts all entity states to all clients each tick. Clients render + send input.
6. **Results** — game server detects winner (stock mode, ADR-0007). Signals master server. Master server pushes results to all clients via SignalR.
7. **Lobby Room** — all players return to the lobby. Host can start another match. Players can leave.

## Consequences

- **Two protocols, clear boundary** — SignalR (master server) handles meta/lobby/char-select/results. UDP (game server) handles match simulation only. No overlap.
- **Game server is stateless between matches** — it receives "start match with these players/chars," runs, reports result, waits for next start command.
- **Character class reaches the server** — the master server passes character selections to the game server at match start. Fixes the hardcoded-Manki blocker.
- **Client must handle both protocols** — SignalR client for lobby/meta, UDP client for match. The `ISimulationBridge` pattern already abstracts the match loop; lobby state is a new client-side concern.
- **Host can start with 2-4 players** — minimum 2 to start a match. Lobby supports up to 4.
- **Leaving mid-match** — a player disconnecting during fight is handled by the game server (entity goes idle, eventually KO'd). The lobby continues for remaining players.
