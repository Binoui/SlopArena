# ADR-0007: Stock Mode for 2-4 Players

**Status:** Accepted — 2026-08-01
**Deciders:** @Binoui

## Context

The current `MatchInstance` is hardcoded for exactly 2 players (entity IDs 1 and 2, sends both states to both players, 3-death win). The demo requires 2-4 players with a win condition that works naturally for variable counts.

Three options were considered:
1. **Stock mode (last standing)** — each player has N stocks. KO costs a stock. Last player with stocks wins.
2. **Timed + score** — highest KO count when time expires.
3. **First to N KOs** — first player to reach a kill target wins.

## Decision

**Stock mode, last player standing, 2-4 players.**

- Each player starts with a configurable stock count (default: 3).
- Getting KO'd (void death or blast zone) costs one stock.
- A player with 0 stocks is eliminated (spectator mode).
- Last player with stocks remaining wins.
- If multiple players reach 0 stocks simultaneously, the one with the most remaining stocks wins; tie → sudden death (1 stock, first KO wins) or shared victory.

**Server changes required:**
- `MatchInstance` must support 2-4 entities, not hardcoded 2.
- Entity IDs: players assigned dynamically (1-4) as they join the match.
- `SendState` broadcasts all entity states to all clients (not just "your state + opponent state").
- Win condition checks remaining stocks across all active players.
- Eliminated players (0 stocks) stop receiving input but still receive state (spectator).

## Consequences

- **Natural for multiplayer** — stock mode scales from 2 to 4+ without rule changes. No kingmaker problem.
- **Server refactor** — `MatchInstance` must be generalized from 2-player to N-player. The entity list, input queue, state broadcast, and win check all change.
- **Packet size** — `CharacterStatePacket` is 44 bytes per entity. 4 players = 176 bytes per tick. Still well within UDP limits.
- **Client HUD** — must show damage % and stocks for all players (2-4), not just self. New HUD layout.
- **Spectator state** — eliminated players need a spectating view. Minimal: keep rendering other players, disable input.
- **Free-for-all only** — no teams for now. Team mode is a future extension.
