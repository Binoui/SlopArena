# Phase 1 Integration Test

**Date:** 2026-06-10
**Status:** Ready for testing

## Purpose

Verify end-to-end communication between Master Server (ASP.NET Core) and Game Server (UDP orchestrator).

## Prerequisites

- .NET 8 SDK
- Docker (for PostgreSQL)
- 3 terminal windows

## Test Steps

### Step 1: Start PostgreSQL

```bash
cd MasterServer
docker run -d --name sloparena-postgres \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=sloparena \
  -e POSTGRES_DB=sloparena \
  -p 5432:5432 \
  postgres:16-alpine
```

### Step 2: Apply EF Migrations

```bash
cd MasterServer
dotnet ef database update
```

Expected output: "Done." or similar success message.

### Step 3: Start Master Server

```bash
# Terminal 1
cd MasterServer
ASPNETCORE_URLS="http://0.0.0.0:5000" dotnet run
```

Expected output:
```
Now listening on: http://0.0.0.0:5000
```

### Step 4: Verify Health Endpoint

```bash
# Terminal 2
curl http://localhost:5000/health
```

Expected response:
```json
{"status":"ok","version":"0.1.0"}
```

### Step 5: Start Game Server

```bash
# Terminal 3
cd Server
dotnet run
```

Expected output:
```
=== SlopArena Game Server ===
Server: SlopArena Local Dev
Region: EU
Port range: 7777-7791
Max concurrent matches: 15
Master server: http://localhost:5000

Registering with master server...
[Registration] Registered as 'SlopArena Local Dev' (ID: <guid>, IP: <ip>)
Registered successfully (Server ID: <guid>).

Orchestrator running. Press Ctrl+C to stop.

[Heartbeat] Loop started (every 10s).
```

**If registration fails:** Master server must be running first. Check `http://localhost:5000/health`.

### Step 6: Verify Registration in Database

```bash
docker exec -it sloparena-postgres psql -U postgres -d sloparena \
  -c "SELECT id, name, region, ip_address, port, is_official, current_matches, last_heartbeat FROM game_servers;"
```

Expected: 1 row with your game server's details.

### Step 7: Verify Heartbeat Updates

Wait 20+ seconds (2 heartbeat cycles), then re-run the SQL query above.

Expected: `last_heartbeat` timestamp updated, `current_matches` = 0.

### Step 8: Test Server Registration Again (Idempotency)

Restart the game server (Ctrl+C in Terminal 3, then `dotnet run` again).

Expected: A second game server row appears (no duplicate prevention — each restart creates a new registration).

### Step 9: Cleanup

```bash
# Stop game server: Ctrl+C in Terminal 3
# Stop master server: Ctrl+C in Terminal 1

# Remove PostgreSQL container
docker rm -f sloparena-postgres
```

## Success Criteria

- [ ] Master server starts and health endpoint returns OK
- [ ] Database migrations apply successfully
- [ ] Game server registers with master (receives server_id + api_token)
- [ ] Registration appears in `game_servers` table
- [ ] Heartbeats update `last_heartbeat` every 10 seconds
- [ ] `current_matches` field reflects actual match count (0 when idle)
- [ ] Ctrl+C gracefully stops the game server

## Known Limitations

- **No match assignment endpoint yet** — matches must be assigned programmatically via `orchestrator.AssignMatch()`. A REST endpoint on the master server to trigger match assignment is deferred to Phase 2 (Task 6: Matchmaking Service).
- **No player authentication** — Steam auth is deferred to Phase 2 (Task 3).
- **No SignalR chat** — Deferred to Phase 2 (Task 8).
- **Single-region** — Only EU game server expected in Phase 1.

## Troubleshooting

### "Connection refused" on register
Master server not running or port mismatch. Check `server.json` → `master_server_url`.

### "Failed: Unauthorized" on heartbeat
API token mismatch. Delete the server row in Postgres and restart game server to re-register.

### "Cannot bind port 7777"
Port already in use. Kill the process or change `port` in `server.json`.

### EF migrations fail
Add `"ConnectionStrings:DefaultConnection"` to `MasterServer/appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=sloparena;Username=postgres;Password=sloparena"
  }
}
```
