# ADR-0005: Embedded Host-and-Play

**Status:** Accepted — 2026-08-01
**Deciders:** @Binoui

## Context

In the server browser model, a player hosts a game server for others to join. The `ServerApp` exists as a standalone .NET console binary. Three options for how the host runs it:

1. **Embedded host-and-play** — Unity client spawns the game server (subprocess or in-process), registers with master server, and the host player connects to localhost. One machine, one player, host-and-play.
2. **Separate ServerApp process** — player manually runs the console binary, then launches Unity and connects to localhost. Two processes, more friction.
3. **Dedicated servers only** — servers run on a VPS, players never host. Requires server infrastructure.

The production target is dedicated servers, but that's too much infrastructure for the current demo stage.

## Decision

**Embedded host-and-play.** When a player clicks "Host" in the Unity client:

1. Unity spawns `ServerApp` as a subprocess (or starts the server in-process via library reference).
2. The game server registers with the master server (existing `GameServerRegistration`).
3. The master server creates a SignalR lobby for this server.
4. The host player connects to the SignalR lobby and to the game server at `localhost:<assigned-port>`.
5. Other players browse the server list, join the SignalR lobby, and connect to the game server at `<host-ip>:<port>`.

This is a placeholder for future dedicated servers — the server binary, registration, and match logic are identical. The only difference is who launches it.

## Consequences

- **Subprocess management** — Unity must launch, monitor, and cleanly shut down the ServerApp process. Needs handling for crash, port conflicts, and graceful shutdown on application quit.
- **Network visibility** — for remote players to connect, the host's machine must be reachable (port forwarding or LAN). For the demo, LAN/localhost is sufficient. Production dedicated servers solve this.
- **No behavioral difference** — the game server code doesn't know or care whether it was launched by a player or a VPS. The `ServerApp` binary is the same.
- **Future migration** — swapping embedded for dedicated is an infrastructure change (run ServerApp on a VPS, remove the host flow from Unity). No game logic changes.
