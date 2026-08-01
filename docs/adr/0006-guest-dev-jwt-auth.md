# ADR-0006: Guest/Dev JWT Auth

**Status:** Accepted — 2026-08-01
**Deciders:** @Binoui

## Context

The master server has JWT infrastructure but no login endpoint. Steam auth is marked "future" with a `Steam__ApiKey` secret placeholder. Three options for client authentication in the demo:

1. **Guest/dev auth (JWT)** — anonymous guest accounts: client hits `POST /auth/guest`, gets a JWT + temporary SteamId (Guid). No Steam SDK, no credentials.
2. **Steam auth** — full Steam Web API authentication. Needs Steamworks.NET in Unity and a Steam auth endpoint on the master server.
3. **Username/password** — simple registration + login. No third-party dependency, but throwaway once Steam auth lands.

## Decision

**Guest/dev auth with JWT.** The master server gains a `POST /auth/guest` endpoint that:

1. Generates a temporary `SteamId` (Guid — matches the existing `User.SteamId` field type).
2. Creates a `User` record in PostgreSQL with default MMR (1000).
3. Issues a JWT containing the SteamId claim.
4. Returns `{ token, steamId }` to the client.

The client stores the JWT and includes it as a Bearer token in all master server requests (SignalR and HTTP). The JWT expires (configurable, e.g., 24h for dev).

## Consequences

- **Fast to build** — one endpoint, reuses existing JWT + User model. No Steam SDK, no Unity native plugin.
- **MMR still works** — each guest gets a persistent User row. ELO updates after matches. Stats survive across sessions if the client stores the token (or re-auths with the same SteamId).
- **No account recovery** — if the client loses the JWT/SteamId, the account is gone. Acceptable for dev/demo.
- **Steam migration path** — when Steam auth lands, add `POST /auth/steam` (validate Steam ticket → find-or-create User → issue JWT). Guest auth can coexist or be deprecated. The JWT issuance and all downstream code (lobby, match, MMR) stays unchanged.
- **Abuse risk** — anonymous accounts mean anyone can create unlimited identities. Rate limiting (already in master server) mitigates. Acceptable for demo.
