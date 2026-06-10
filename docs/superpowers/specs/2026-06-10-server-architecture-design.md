# Server Architecture Design

**Date:** 2026-06-10  
**Status:** Approved  
**Author:** Benjamin Houx

## Context

SlopArena needs a multiplayer backend to support:
- **1v1 ranked matchmaking** with MMR-based pairing
- **Community-hosted dedicated servers** with custom rules
- **Multi-level chat system** (global, DM, party, server, spectator)
- **Spectator mode** with ghost rendering (players can watch live matches)

### Constraints

- Solo developer, passion project
- Budget target: <€40/month (~$40-45/month)
- Open-source (community can fork and modify servers)
- F2P game with zero monetization (donation-based only)
- Steam-only distribution (Steam auth, no custom auth needed)
- Competitive focus (low latency matters for 1v1, not for chat/spectating)

### Design Goals

- Minimize operational costs while supporting 300-500 peak concurrent players
- Simple architecture (easy to deploy, debug, and maintain for solo dev)
- Scalable without serverless complexity (linear VPS scaling)
- Community-driven servers for casual play (zero hosting cost for non-ranked)
- Anti-cheat: server-authoritative only (no aggressive anti-cheat at launch)

---

## Architecture Overview

### Selected Approach: **Monolithic Pragmatic**

All services start on 1-2 VPS instances, scale by adding cloned VPS as needed.

**Components:**

```
┌─ Master Server (Hetzner €4.51/month) ──────────┐
│  • ASP.NET Core REST API                       │
│  • SignalR WebSocket hub (chat)                │
│  • PostgreSQL (Steam auth + MMR + matches)     │
│  • Matchmaking queue (MMR-based pairing)       │
│  • Server browser API                          │
│  • Heartbeat monitor for game servers          │
└────────────────────────────────────────────────┘
           ↓ Assigns players to game servers
┌─ Game Server EU (Hetzner €5.83/month) ─────────┐
│  • 10-15 concurrent match instances            │
│  • UDP 60Hz per match (existing Server/ code)  │
│  • Spectator support (up to 20 per match)      │
│  • Auto-registers with master server           │
└────────────────────────────────────────────────┘
┌─ Game Server US (DigitalOcean $6/month) ───────┐
┌─ Game Server Asia (Vultr $6/month, phase 2) ───┐

┌─ Community Servers (user-hosted) ──────────────┐
│  • Standalone binary / Docker image            │
│  • Register with master server                 │
│  • Appear in server browser                    │
│  • Custom rules via config file                │
└────────────────────────────────────────────────┘
```

**Total estimated cost:**
- Phase 1 (EU only): €10.34/month (~$11)
- Phase 2 (EU + US): $18/month
- Phase 3 (EU + US + Asia): $24/month
- 500+ players: $35-40/month (5 game servers)

**Why this approach:**
- Predictable fixed costs
- Simple to deploy and debug (SSH + systemd)
- Scales linearly (1 VPS = +150-250 concurrent players)
- No vendor lock-in or serverless complexity
- Can migrate to container orchestration later if needed

**Alternatives considered:**
- Serverless game instances (Fly.io Firecracker): rejected due to unpredictable costs and cold start latency
- P2P for 1v1: rejected due to ping asymmetry and lack of region optimization
- Hybrid (ranked on VPS, casual P2P): rejected to keep architecture simple

---

## Master Server

### Responsibilities

1. **Authentication** — Validate Steam tokens, issue internal JWTs
2. **Matchmaking** — Queue players, pair by MMR, assign to optimal game server
3. **Chat** — WebSocket hub for global/DM/party chat
4. **Server registry** — Track all game servers (official + community)
5. **Server browser API** — List servers and active matches for spectators
6. **MMR persistence** — Store player stats and match history

### Tech Stack

- **Runtime:** ASP.NET Core 8.0 (Minimal APIs)
- **WebSocket:** SignalR for chat
- **Database:** PostgreSQL 16 (local on same VPS)
- **Reverse proxy:** Nginx with Let's Encrypt SSL

### REST API Endpoints

**Authentication:**
- `POST /auth/steam` — Body: `{steam_token}` → Returns: `{jwt, steam_id, username, mmr}`

**Matchmaking:**
- `GET /matchmaking/join?region=EU` — Joins ranked 1v1 queue
- `GET /matchmaking/status` — Poll queue status → Returns: `{status: "searching"|"found", match_info?}`
- `POST /matchmaking/leave` — Leaves queue

**Server Registry (used by game servers):**
- `POST /servers/register` — Body: `{name, region, port, max_matches, is_official, custom_rules}` → Returns: `{server_id, api_token}`
- `POST /servers/{id}/heartbeat` — Body: `{current_matches}` (sent every 10s)
- `POST /match/result` — Body: `{player1_steam_id, player2_steam_id, winner_steam_id}` → Updates MMR

**Server Browser (used by clients):**
- `GET /servers/list?region=EU&official_only=false` → Returns: list of servers
- `GET /servers/{id}/matches` → Returns: active matches on that server (for spectators)

### SignalR Hub (Chat)

**Methods:**
- `SendGlobalMessage(message)` — Broadcast to all connected clients
- `SendDM(targetSteamId, message)` — Private message
- `JoinParty(partyId)` / `LeaveParty(partyId)` — Party chat management
- `SendPartyMessage(partyId, message)` — Message to party

**Connection:** Clients connect to `wss://master.sloparena.com/chat` with JWT auth.

### Matchmaking Logic

**Queue structure:**
```csharp
struct QueueEntry
{
    ulong SteamId;
    int MMR;
    string PreferredRegion;
    DateTime JoinedAt;
}
```

**Pairing algorithm (runs every 2 seconds):**
1. Sort queue by join time (FIFO)
2. For each player, find best match:
   - MMR within ±100 initially
   - After 30s waiting: expand by +50 per 30s (±150 at 1min, ±200 at 1.5min, etc.)
   - Prefer same region, fallback to closest available
3. When match found:
   - Pick least-loaded game server in target region
   - Generate match token (UUID)
   - Send match info to both clients via SignalR: `{server_ip, server_port, match_token}`
   - Remove from queue

**Region assignment:**
- If both players prefer same region → use it
- If different → pick region with lower combined latency (heuristic: EU-US midpoint = US East, EU-Asia = EU)

### Database Schema

```sql
CREATE TABLE users (
    steam_id BIGINT PRIMARY KEY,
    username VARCHAR(64) NOT NULL,
    mmr INT NOT NULL DEFAULT 1000,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    last_login TIMESTAMP
);

CREATE TABLE matches (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    player1_steam_id BIGINT NOT NULL REFERENCES users(steam_id),
    player2_steam_id BIGINT NOT NULL REFERENCES users(steam_id),
    winner_steam_id BIGINT REFERENCES users(steam_id), -- NULL if draw/disconnect
    server_region VARCHAR(16) NOT NULL,
    started_at TIMESTAMP NOT NULL DEFAULT NOW(),
    ended_at TIMESTAMP
);

CREATE INDEX idx_matches_player1 ON matches(player1_steam_id);
CREATE INDEX idx_matches_player2 ON matches(player2_steam_id);

CREATE TABLE game_servers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(128) NOT NULL,
    ip_address VARCHAR(45) NOT NULL,
    port INT NOT NULL,
    region VARCHAR(16) NOT NULL,
    is_official BOOLEAN NOT NULL DEFAULT false,
    max_concurrent_matches INT NOT NULL,
    current_matches INT NOT NULL DEFAULT 0,
    custom_rules JSONB,
    last_heartbeat TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_servers_region ON game_servers(region);
```

### Heartbeat & Health Monitoring

**Game server heartbeats:**
- Every 10 seconds: `POST /servers/{id}/heartbeat` with `{current_matches}`
- Master updates `last_heartbeat` timestamp
- If no heartbeat for 30s → mark server as "down" (exclude from matchmaking)
- Matches on crashed servers → no MMR change (considered invalid)

**Master server monitoring:**
- UptimeRobot pings `https://master.sloparena.com/health` every 5 minutes
- Returns `{status: "ok", uptime_seconds, active_players, active_matches}`

---

## Game Server

### Architecture

**Multi-match process model:**

Each game server VPS runs a **single orchestrator process** that spawns **1 thread per active match**. Each thread is an isolated instance of the existing `Server/Program.cs` UDP loop.

**Capacity:** 10-15 concurrent matches per VPS (configurable via `max_concurrent_matches`).

**Port allocation:**
- Base port: 7777
- Match ports: 7777, 7778, 7779, ..., 7791 (15 slots)
- Each match binds to its own port

### Match Lifecycle

1. **Match assignment** — Master server sends: `POST /matches/assign` with `{match_id, player1_steam_id, player2_steam_id, token}`
2. **Thread spawn** — Orchestrator spawns a new thread, binds UDP port
3. **Player connection** — Both clients connect via UDP, send `ClientJoinPacket` with token
4. **Simulation start** — When both connected, simulation begins at 60Hz
5. **Match end** — After one player wins or timeout (5 min no input), send result to master, terminate thread

### Simulation (60Hz UDP)

**Existing code in `Server/Program.cs`** is already suitable:
- Server-authoritative tick-based simulation
- `Simulation.SimulateTick()` from `Shared/`
- Clients send `ClientInputPacket` (14 bytes) every tick
- Server responds with `CharacterStatePacket` (31 bytes per player)

**No changes needed** for basic 1v1 functionality.

### Spectator Support

**New feature to add:**

**Spectator connection:**
- Spectators send `SpectatorJoinPacket` with `match_token` to the game server
- Server adds their `IPEndPoint` to a `List<SpectatorEndPoint>` (max 20)
- If >20 spectators → reject with "match full" packet

**Spectator packets (30Hz, not 60Hz):**
```csharp
struct SpectatorStatePacket
{
    uint TickNumber;
    CharacterStatePacket Player1; // 31 bytes
    CharacterStatePacket Player2; // 31 bytes
    byte SpectatorCount;
    // Total: ~65 bytes per spectator per packet
}
// Sent at 30Hz = 65 bytes × 30 = 1.95 KB/s per spectator
// 20 spectators = ~39 KB/s upload (acceptable on VPS)
```

**Spectator ghost system:**
- Spectators send their ghost position at 10Hz: `SpectatorGhostPacket {x, y, z}`
- Server broadcasts ghost positions to all spectators (not to players)
- Rendering is client-side in Godot (transparent avatar + username label)

**Spectator chat:**
- Phase 1-2: Use WebSocket chat from master server (OK for text, 2s latency is fine)
- Phase 3 (optional): Add UDP chat packets for instant spectator chat

**Bandwidth math:**
- Players: 31 bytes × 60Hz × 2 = 3.7 KB/s upload
- Spectators (20 max): 65 bytes × 30Hz × 20 = **39 KB/s upload**
- Total per match: ~43 KB/s = **0.35 Mbps** (well within VPS limits)

### Registration & Heartbeat

**On startup:**
1. Read `server.json` config
2. `POST /servers/register` to master:
   ```json
   {
     "name": "Official EU #1",
     "ip_address": "auto-detect or config",
     "port": 7777,
     "region": "EU",
     "is_official": true,
     "max_concurrent_matches": 15,
     "custom_rules": null
   }
   ```
3. Receive `server_id` and `api_token`
4. Start heartbeat loop (every 10s):
   ```http
   POST /servers/{server_id}/heartbeat
   Authorization: Bearer {api_token}
   Body: {"current_matches": 8}
   ```

**On match end:**
```http
POST /match/result
Body: {
  "match_id": "uuid",
  "player1_steam_id": 76561198012345678,
  "player2_steam_id": 76561198087654321,
  "winner_steam_id": 76561198012345678,
  "duration_seconds": 187
}
```

Master server updates MMR using a simple ELO-like formula:
```csharp
int CalculateMMRChange(int winnerMMR, int loserMMR)
{
    int expected = (int)(1 / (1 + Math.Pow(10, (loserMMR - winnerMMR) / 400.0)) * 100);
    int K = 32; // K-factor (higher = faster MMR swings)
    return K * (1 - expected / 100);
}
// Winner gains N, loser loses N
```

---

## Server Browser & Spectator Flow

### Server List API

**Client requests: `GET /servers/list?region=EU`**

Response:
```json
{
  "servers": [
    {
      "id": "uuid",
      "name": "Official EU #1",
      "region": "EU",
      "is_official": true,
      "current_matches": 8,
      "max_matches": 15,
      "avg_ping_ms": 25,
      "custom_rules": null
    },
    {
      "id": "uuid",
      "name": "[FR] Serveur Baguette",
      "region": "EU",
      "is_official": false,
      "current_matches": 3,
      "max_matches": 10,
      "custom_rules": ["no_ults", "manki_only"],
      "avg_ping_ms": 18
    }
  ]
}
```

### Match Browser (for spectators)

**Client requests: `GET /servers/{server_id}/matches`**

Response:
```json
{
  "matches": [
    {
      "match_id": "uuid",
      "server_ip": "1.2.3.4",
      "port": 7778,
      "player1": {
        "steam_id": "76561198012345678",
        "username": "xXPr0Xx",
        "character": "Manki",
        "mmr": 1450
      },
      "player2": {
        "steam_id": "76561198087654321",
        "username": "SlopGod",
        "character": "Manki",
        "mmr": 1520
      },
      "duration_seconds": 145,
      "spectator_count": 3
    }
  ]
}
```

### Spectator UI Flow (Godot Client)

1. **Main menu** → "Watch Matches" button
2. **Match browser screen:**
   - Fetch `/servers/list` (all regions or filtered)
   - For each server, fetch `/servers/{id}/matches`
   - Display list with: player names, MMR, characters, duration, spectator count, ping
   - Refresh every 3 seconds
   - Sort options: by spectators (most popular), by MMR (highest skill), by duration (most recent)
3. **Click on match** → Send `SpectatorJoinPacket` to `server_ip:port`
4. **Spectator mode scene:**
   - Receive `SpectatorStatePacket` at 30Hz
   - Render both players + arena
   - Camera: free-fly or follow player (toggle with Tab)
   - Spectator ghosts: transparent avatars floating around
   - Chat window (WebSocket to master server, spectator channel)
   - Emote system (client-side animations for ghost avatars)

### Featured Matches (Phase 3)

**Algorithm to highlight "interesting" matches:**

```csharp
float CalculateMatchScore(Match m)
{
    float spectatorBonus = m.SpectatorCount * 2;
    float skillBonus = (m.Player1MMR + m.Player2MMR) / 200f;
    float durationBonus = m.DurationSeconds > 120 ? 5 : 0; // not a stomp
    return spectatorBonus + skillBonus + durationBonus;
}
// Top 5 matches get "🔥 FEATURED" tag in browser UI
```

---

## Community Servers

### Distribution

**Two formats:**

1. **Standalone binary** (`SlopArena-Server.exe` / Linux binary)
   - Downloadable from GitHub Releases
   - Double-click to run (creates default `server.json` if missing)
   - Console interface with admin commands

2. **Docker image** (`ghcr.io/binoui/sloparena-server:latest`)
   - `docker-compose.yml` provided with env var overrides
   - Ideal for VPS hosting

### Configuration File (`server.json`)

```json
{
  "server_name": "My Custom Server",
  "region": "EU",
  "port": 7777,
  "max_concurrent_matches": 10,
  "master_server_url": "https://master.sloparena.com",
  "is_official": false,
  "custom_rules": {
    "allowed_characters": ["Manki"],
    "allowed_maps": ["pit", "rooftop"],
    "disable_ultimates": false,
    "damage_multiplier": 1.0,
    "time_limit_seconds": 300
  },
  "moderation": {
    "banned_steam_ids": [],
    "require_min_account_age_days": 0,
    "discord_webhook_url": ""
  }
}
```

**Custom rules validation:**
- Master server does NOT enforce community server rules
- Server sends rules as JSON to master, displayed in browser as tags
- Clients trust the server config (server is authoritative)

### Registration & Visibility

**On startup:**
1. Community server reads `server.json`
2. `POST /servers/register` to master (same flow as official servers)
3. Appears in server browser with "Community" tag
4. Players can filter: "Show official only" checkbox

**Moderation:**
- Each community server manages its own ban list (stored in `banned_steam_ids`)
- No centralized ban system (community server owners are responsible)
- If a server is abusive (spam, hate speech), manual blacklist on master server

### Admin Console

**Commands (stdin or HTTP REST API on localhost:5000):**
- `/ban <steam_id> <reason>` — Adds to ban list, kicks if connected
- `/kick <steam_id>` — Disconnects player
- `/list` — Shows active matches and players
- `/stop` — Graceful shutdown (finishes active matches, stops accepting new)

**Logging:**
- All matches logged to `logs/matches.log`
- Format: `[timestamp] Match {id} | P1: {steam_id} | P2: {steam_id} | Winner: {steam_id} | Duration: {seconds}s`

### Future: Modding Support

**Phase 3+ considerations:**
- Custom maps: JSON `ArenaDefinition` files uploaded to server
- Custom game modes: config flags like `respawn_enabled`, `team_mode`, etc.
- Community-compiled mods: **high risk**, evaluate later (sandboxing, security review needed)

---

## Infrastructure & Deployment

### VPS Provider Selection

**Recommended: Hetzner Cloud** (best EU performance/price)
- **CX11** (2 vCPU, 2GB RAM): €4.51/month — Master server
- **CX21** (2 vCPU, 4GB RAM): €5.83/month — Game server (10-15 matches)

**Alternatives:**
- **DigitalOcean** ($6/droplet) — US presence, simple UI
- **Vultr** ($6/instance) — Asia-Pacific regions
- **OVH** (€3-5/month) — cheapest but network quality varies

### Deployment Plan

**Phase 1: MVP (EU only) — €10.34/month (~$11)**
```
master.sloparena.com (Hetzner CX11 Frankfurt)
├─ ASP.NET Core (REST + SignalR)
├─ PostgreSQL 16
├─ Nginx reverse proxy (SSL)
└─ Docker Compose

game-eu-1.sloparena.com (Hetzner CX21 Frankfurt)
├─ SlopArena-Server binary
├─ Systemd service (auto-restart)
└─ Firewall: UDP 7777-7791, HTTPS 443
```

**Phase 2: Add US — $18/month total**
```
+ game-us-1.sloparena.com (DigitalOcean NYC $6)
```

**Phase 3: Add Asia — $24/month total**
```
+ game-asia-1.sloparena.com (Vultr Singapore $6)
```

### CI/CD Pipeline

**GitHub Actions workflow (`.github/workflows/deploy.yml`):**

```yaml
on:
  push:
    branches: [main]

jobs:
  deploy-master:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build master server Docker image
        run: docker build -t sloparena-master ./MasterServer
      - name: Deploy to Hetzner
        run: |
          ssh deploy@master.sloparena.com "cd /opt/sloparena && docker-compose pull && docker-compose up -d"
  
  deploy-gameservers:
    runs-on: ubuntu-latest
    steps:
      - name: Build game server binary
        run: dotnet publish Server/SlopArena.Server.csproj -c Release -o publish/
      - name: Deploy to EU
        run: scp -r publish/ deploy@game-eu-1.sloparena.com:/opt/sloparena/
      - name: Restart service
        run: ssh deploy@game-eu-1.sloparena.com "systemctl restart sloparena-gameserver"
```

**Secrets management:**
- GitHub Secrets: SSH keys, Steam API key, JWT secret, DB password
- On VPS: environment variables in systemd service or docker-compose

### Database Backups

**Automated backups (master server VPS):**
```bash
# Cron job (daily at 3 AM UTC)
0 3 * * * pg_dump sloparena_db | gzip > /backup/sloparena_$(date +\%Y\%m\%d).sql.gz

# Upload to Backblaze B2 (10GB free tier)
0 4 * * * rclone sync /backup/ b2:sloparena-backups --max-age 7d
```

**Retention:** 7 days rolling (sufficient for F2P game with low data criticality).

### Monitoring

**Essential (Phase 1):**
- **UptimeRobot** (free): Pings `https://master.sloparena.com/health` every 5 min
- **Simple dashboard** in master server: `/admin/dashboard` (basic auth)
  - Active players count
  - Active matches count
  - Game servers status (up/down, load)
  - Last 10 errors from logs

**Optional (Phase 2+):**
- **Grafana + Prometheus**: Detailed metrics (latency, packet loss, MMR distribution)
- **Sentry**: Crash reporting for server exceptions

### Scaling Triggers

**When to add a new game server:**
- All existing game servers are >80% capacity for 1 hour
- Average queue time exceeds 30 seconds

**When to remove a game server:**
- All game servers are <30% capacity for 24 hours
- Graceful shutdown: stop accepting new matches, wait for active matches to finish

**Estimated capacity per phase:**

| Phase | Infrastructure | Max Players | Cost |
|-------|---------------|-------------|------|
| 1 | 1 master + 1 EU game | 150-200 | $11/mo |
| 2 | 1 master + 2 game (EU/US) | 300-400 | $18/mo |
| 3 | 1 master + 3 game (EU/US/Asia) | 450-600 | $24/mo |
| 4 | 1 master + 5 game (multi-region) | 750-1000 | $40/mo |

---

## Security & Anti-Cheat

### Authentication

**Steam-only authentication:**
- Client obtains Steam session ticket via Steamworks SDK
- Sends ticket to master server: `POST /auth/steam`
- Master validates via Steam Web API: `ISteamUserAuth/AuthenticateUserTicket`
- Returns JWT (HS256, 24h expiry) for subsequent requests

**No custom passwords** → no password leaks, no forgot-password flows.

### Server-Authoritative Simulation

**Already implemented in `Shared/Simulation.cs`:**
- All physics, hitboxes, damage calculated server-side
- Clients send only inputs (movement flags, button presses)
- Impossible to cheat position, health, cooldowns

**What this prevents:**
- Speedhacks (server enforces tick rate)
- God mode (server controls health)
- Teleportation (server computes position from inputs)

**What this does NOT prevent:**
- Aimbots (client-side rendering, not critical in WASD brawler)
- Wallhacks (client receives all entity positions for prediction)
- Input macros (hard to detect, low impact)

### Anti-Cheat Strategy

**Phase 1-2: No active anti-cheat** (server-authoritative is sufficient)

**Phase 3 (if cheating becomes a problem):**
- **Enable VAC** (Valve Anti-Cheat) on official servers (free with Steamworks)
- **Heuristics**: Server tracks suspicious metrics:
  - Perfect input timing (inhuman consistency)
  - Impossible reaction times (<100ms consistently)
  - Report system (players can flag opponents)
- **Manual review**: Flagged accounts reviewed by moderators (community-driven)

**Community servers:** Each server owner handles their own moderation (bans, kicks).

### Rate Limiting & DDoS Protection

**Master server:**
- Cloudflare Free Tier in front of master server (DDoS protection + SSL)
- Rate limit: 10 req/s per IP for matchmaking endpoints
- SignalR connection limit: 1000 concurrent WebSocket connections (enough for 500 players)

**Game servers:**
- No public HTTP endpoints (only UDP game traffic)
- UDP packet validation: discard malformed packets silently
- IP-based rate limit: max 120 packets/s per IP (2× normal rate, allows burst)
- If >200 packets/s from one IP → temporary IP block (1 minute)

---

## Phased Rollout Plan

### Phase 1: MVP (2-3 months)

**Features:**
- Master server: Steam auth, matchmaking, MMR persistence
- 1 game server (EU)
- Basic ranked 1v1 matchmaking
- Global chat only (SignalR WebSocket)
- No spectators yet
- No community servers yet

**Deliverables:**
- `MasterServer/` (ASP.NET Core project)
- `Server/Program.cs` refactor (multi-match orchestrator)
- Database migrations
- Deployment scripts
- Minimal Godot UI (matchmaking screen, connecting screen)

**Success criteria:**
- 10-20 concurrent players can matchmake and play
- Average queue time <1 minute
- No crashes or desyncs during matches
- Budget: <$15/month

---

### Phase 2: Community & Chat (1-2 months)

**Features:**
- DM and party chat (SignalR)
- Server browser UI in Godot
- Community server support (standalone binary + Docker)
- Game server US (2nd region)
- Improved matchmaking (region preference, longer wait = wider MMR range)

**Deliverables:**
- `server.json` config format
- Server registration REST API
- Server browser UI (list servers, filter by region/rules)
- GitHub Release: `SlopArena-Server-v1.0.zip`
- Docker image: `ghcr.io/binoui/sloparena-server:latest`

**Success criteria:**
- 5-10 community servers appear in browser
- 50-100 concurrent players across all servers
- Chat is stable (no disconnects, messages delivered)

---

### Phase 3: Spectator Mode (1-2 months)

**Features:**
- Match browser (see active matches on any server)
- Spectator UDP packets (30Hz state updates)
- Spectator ghost rendering (clients see each other)
- Spectator chat (WebSocket, separate from player chat)
- Featured matches algorithm

**Deliverables:**
- `SpectatorStatePacket` implementation
- Godot spectator camera (free-fly + follow mode)
- Ghost avatar rendering (transparent player model + username label)
- Match browser UI (sort by popularity, skill, duration)

**Success criteria:**
- 10-20 spectators can watch a single match without lag
- Ghosts render smoothly (10Hz position updates)
- Chat works for spectators

---

### Phase 4: Polish & Scale (ongoing)

**Features:**
- Game server Asia (3rd region)
- Improved MMR algorithm (decay, seasonal resets)
- Match history UI (view past games)
- Leaderboards (top 100 players per region)
- Admin dashboard (ban management, server health)
- Discord integration (match notifications, stats bot)

**Success criteria:**
- 300-500 concurrent players
- Budget: <$40/month
- Average queue time: <30 seconds
- No critical bugs reported

---

## Open Questions & Future Work

### Resolved in Design Discussion

- **P2P vs. dedicated servers for 1v1:** Dedicated servers (better ping, no NAT issues)
- **Serverless vs. VPS:** VPS (predictable costs, simpler for solo dev)
- **Chat architecture:** Centralized WebSocket (SignalR on master server)
- **Spectator capacity:** 20 per match (30Hz updates)
- **Authentication:** Steam-only (no custom auth)
- **Anti-cheat:** Server-authoritative only (no VAC at launch)
- **Community servers:** Standalone binary + Docker (open-source, config-driven)

### Deferred to Future Phases

- **Replay system:** Store match state history for playback (Phase 4+)
- **Tournament mode:** Bracket system, admin controls (Phase 4+)
- **Custom maps:** JSON-based map uploads (Phase 3+)
- **Voice chat:** WebRTC voice for party/spectators (Phase 4+, high complexity)
- **Mobile spectator app:** Watch matches from phone (Phase 5+, nice-to-have)
- **Serverless migration:** If costs spike, evaluate Fly.io/Railway (only if needed)

### Technical Debt to Monitor

- **Database scaling:** Postgres on same VPS as master is fine for <10k users, may need separate DB server at scale
- **Chat scaling:** SignalR on single server limits to ~1000 concurrent connections, may need Redis backplane at scale
- **MMR algorithm:** Simple ELO is fine for MVP, consider Glicko-2 or TrueSkill later for better accuracy
- **Game server crash recovery:** Currently matches are lost on crash, could add save-state snapshots (low priority)

---

## Conclusion

This architecture provides a **simple, scalable, and cost-effective** backend for SlopArena's multiplayer needs. Key strengths:

1. **Budget-friendly:** $11-40/month scales to 1000 players
2. **Solo-dev friendly:** No complex orchestration, easy to debug
3. **Community-driven:** Open-source servers, no hosting cost for casual play
4. **Competitive-ready:** Low-latency 1v1 matchmaking with MMR
5. **Spectator-first:** Built-in support for watching matches with social features

The phased rollout plan allows shipping an MVP quickly (Phase 1) while iterating on community features (Phase 2) and spectator mode (Phase 3) based on player feedback.

Next step: Implementation plan (break down into tasks, estimate time per component).
