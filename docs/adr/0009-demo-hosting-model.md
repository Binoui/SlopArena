# ADR-0009: Demo Hosting Model — OfficialServers + Technical Host-and-Play

**Status:** Accepted — 2026-08-02
**Deciders:** @Binoui
**Amends:** ADR-0005 (embedded host-and-play demo posture)

## Context

ADR-0005 chose embedded host-and-play for the demo, noting "LAN/localhost is
sufficient." The demo goal is friends over the internet. Player-hosted matches
are unreachable from outside a LAN today: `GameServerRegistration` advertises
the machine's LAN IP, and remote reachability requires per-host port
forwarding — a non-starter for non-technical players.

## Decision

Two-tier hosting for the demo:
1. **OfficialServers** — operator-run dedicated `GameServer` instances on the
   home mini PC (always on, registered with a reachable public IP/domain).
   Non-technical players only Join; no NAT work on their side.
2. **HostAndPlay** — stays supported for technical players who port-forward.
   The player sets their public IP/domain in the host UI; the bundled
   self-contained server binary is spawned from StreamingAssets.

## Consequences

- Player-facing docs use "Join" (OfficialServers) as the primary path; "Host"
  is documented separately for technical users.
- The game server must accept a `publicIp` override (domain allowed) in
  `server.json`; `ServerHost` must launch a bundled binary in release builds.
- The master server URL in release builds points at `https://sloparena.barakaslurp.fr`.
- Future migration to VPS hosting is infra-only (ADR-0005 consequence unchanged).
