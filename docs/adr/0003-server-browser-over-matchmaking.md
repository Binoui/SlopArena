# ADR-0003: Server Browser over Matchmaking

**Status:** Accepted — 2026-08-01
**Deciders:** @Binoui

## Context

The PvP demo needs a way for players to find opponents and connect to game servers. Two models were considered:

1. **Matchmaking queue** — players queue, server pairs them (FIFO or MMR-based) and assigns a game server.
2. **Server browser** — players host game servers that register with the master server; others browse and join. Like Counter-Strike community servers.

The master server already has `GameServerRegistration` (register + heartbeat), the `GameServer` PostgreSQL model (IP, port, region, current/max matches), and `MultiMatchOrchestrator` (port allocation, 15 concurrent matches). Matchmaking would require building a queue, pairing logic, and match assignment on top of this. A server browser needs only a `GET /servers` list endpoint and client UI.

## Decision

**Server browser model.** Players host or browse game servers. The master server lists active servers (heartbeat-fresh, not full). Players pick one and connect directly via UDP.

No matchmaking queue. No MMR-based pairing. The existing ELO/MMR system still updates after matches — it just doesn't gate pairing.

## Consequences

- **Simpler master server** — one new endpoint (`GET /servers`), no queue state, no pairing logic.
- **Player-driven** — host creates a server, friends/community can join by browsing. No waiting in a queue alone.
- **MMR is cosmetic for now** — it tracks and updates but doesn't influence who you play against. Fine for a demo.
- **Future matchmaking** — a queue can be layered on top later without changing the server browser. They coexist: "Quick Play" (matchmaking) vs "Server Browser."
- **Host-and-play requirement** — since players host, the Unity client must be able to start a game server. See ADR-0005.
