# SlopArena Master Server

> **Status:** Deployed 2026-08-01 (issue [#28](https://github.com/Binoui/SlopArena/issues/28)) — foundation for the [PvP Demo](../plans/2026-08-01-pvp-roadmap-v2.md) epic.
> **Repo:** https://github.com/Binoui/SlopArena-MasterServer

The master server is the matchmaking/meta API for online PvP. It runs **separately** from the game server (`src/Server`): the game server registers with it and heartbeats; clients will auth and browse servers against it. Two protocols, clear boundary — SignalR/REST on the master server, UDP on the game server.

---

## Stack

- ASP.NET Core 8 Web API (minimal API in `Program.cs`)
- PostgreSQL via EF Core 8 + Npgsql
- Bearer-token auth: `/servers/register` issues a plain `apiToken` GUID; heartbeat + match-result authenticate by timing-safe comparison of that raw GUID against the stored `GameServers.ApiToken`. JWT packages are referenced but **not wired yet** (no `AddAuthentication` in `Program.cs`) — guest JWT auth lands in roadmap Phase 1.1.
- SignalR registered, **no hubs yet** — lobby hub lands in a later ticket (roadmap Phase 2)

---

## Local deployment

**Prerequisites:** PostgreSQL (tested on 18.4; Npgsql EF Core 8 supports 13+) running on `localhost:5432`, .NET 8 SDK + runtime, `dotnet-ef` 8.0.0.

```bash
# Clone (already at ~/Documents/projects/SlopArena-MasterServer on this machine)
git clone https://github.com/Binoui/SlopArena-MasterServer

cd SlopArena-MasterServer

# Create the database once (trust auth in dev; password in the connection string is ignored)
psql -U postgres -h localhost -c "CREATE DATABASE sloparena;"

# Apply EF Core migrations → tables: GameServers, Users, Matches
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update

# Start the server (Development profile → http://localhost:5000)
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --urls http://localhost:5000
```

**Master server URL:** `http://localhost:5000`

---

## Configuration

| Where | Key | Dev value |
|---|---|---|
| `appsettings.Development.json` | `ConnectionStrings:DefaultConnection` | `Host=localhost;Port=5432;Database=sloparena;Username=postgres;Password=password` |
| `.env` (gitignored, overrides appsettings when sourced) | `ConnectionStrings__DefaultConnection`, `Jwt__Secret` | dev values; see `.env.example` |
| **this repo** `src/Server/server.json` | `master_server_url` | `http://localhost:5000` |
| **this repo** `src/Server/MultiMatchOrchestrator.cs` → `ServerConfig.MasterServerUrl` | default | `http://localhost:5000` |

The game server (`src/Server`) points at the master server via `ServerConfig.MasterServerUrl`, loaded from `server.json`. Change `master_server_url` there to redirect registration/heartbeats (e.g. staging).

---

## Endpoints

| Method | Path | Auth | Returns |
|---|---|---|---|
| GET | `/health` | none | `{ "status": "ok", "version": "0.1.0" }` |
| POST | `/servers/register` | none (issues token) | `{ "serverId": "<guid>", "apiToken": "<guid>" }` |
| POST | `/servers/{serverId}/heartbeat` | Bearer `apiToken` | `{ "status": "ok" }` |
| POST | `/match/result` | Bearer `apiToken` | `{ "status": "recorded", "mmrChange": <int> }` (match row must already exist, else 404) |

**Not yet implemented** (later roadmap tickets): `POST /auth/guest` (Phase 1.1), `GET /servers` browser list (Phase 1.2), `LobbyHub` SignalR hub (Phase 2.1).

### Smoke test

```bash
# Health
curl -s http://localhost:5000/health
# → {"status":"ok","version":"0.1.0"}

# Register a fake game server
curl -s -X POST http://localhost:5000/servers/register \
  -H "Content-Type: application/json" \
  -d '{"name":"fake-eu-1","ipAddress":"127.0.0.1","port":9876,"region":"eu-west","isOfficial":false,"maxConcurrentMatches":15,"customRulesJson":null}'
# → {"serverId":"...","apiToken":"..."}
```

---

## Database schema (applied 2026-08-01)

- `GameServers` — registered game servers (id, name, ip, port, region, apiToken, lastHeartbeat, capacity)
- `Users` — players (steamId PK, username, mmr, createdAt, lastLogin)
- `Matches` — completed matches (id, player1/2SteamId, winnerSteamId, region, startedAt, endedAt) with FKs to `Users` (restrict delete)

---

## Notes

- The master server is the **source of truth for matchmaking/meta only**. Match simulation stays server-authoritative on the game server over UDP — never replicate gameplay state to the master server.
- The `Jwt__Secret` in `.env` is a placeholder for the future guest-auth flow (Phase 1.1) — not used by the current bearer-token auth. Dev-only; regenerate with `openssl rand -base64 64` for any non-local deployment.
