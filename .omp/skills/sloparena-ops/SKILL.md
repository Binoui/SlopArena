---
name: sloparena-ops
description: Operate and debug the SlopArena self-hosted backend on the mini PC (alfred) — status checks, log locations, connectivity tests, the diagnostic ladder, and every known failure mode with fixes. Use when the game can't connect, a service is down, registration is stale, or anyone asks "is the server up?".
---

# SlopArena Ops (alfred mini-PC backend)

## When to use

- Player reports "can't join / hangs / can't see servers"
- Checking whether the master, game server, or tunnel is up
- After any deploy/restart, verifying registration + heartbeat
- Anything in `docs/systems/troubleshooting.md` (this skill is the compressed playbook)

## Topology

```
Bbox router ──forwards TCP 7777, UDP 7777-7780──▶ alfred (<alfred-lan-ip>, ssh alias "alfred")
                                                   ├─ sloparena-postgres (127.0.0.1:5432, host-only)
                                                   ├─ sloparena-master   (127.0.0.1:5000, host net)
                                                   ├─ sloparena-server-1 (TCP+UDP 7777, host net)
                                                   └─ cloudflared (tunnel → sloparena.barakaslurp.fr → localhost:5000)
```

Compose stack: `/root/homelab/sloparena/docker-compose.yml` (on the box, NOT
in the game repo). All commands run as `ssh alfred '...'`.

## The diagnostic ladder (fastest → deepest)

```bash
# 1. Tunnel / master API reachable from anywhere
curl -s https://sloparena.barakaslurp.fr/health          # {"status":"ok","version":"0.1.0"}

# 2. All containers Up
ssh alfred 'cd /root/homelab/sloparena && docker compose ps'

# 3. Game server registered AND heartbeating (age = seconds = alive)
ssh alfred 'docker exec sloparena-postgres psql -U sloparena -d sloparena -c \
  "SELECT \"Name\", \"IpAddress\", \"Port\", \"IsOfficial\", NOW() - \"LastHeartbeat\" AS age FROM \"GameServers\";"'
# age > 30s → heartbeat dead → server crashed, unregistered, or unreachable

# 4. TCP reachability (0 = OK, 124 = dropped by firewall/forward)
timeout 8 bash -c 'echo > /dev/tcp/<alfred-lan-ip>/7777' && echo "LAN OK"
timeout 8 bash -c 'echo > /dev/tcp/<public-ip>/7777' && echo "public OK"   # hairpin via Bbox

# 5. UDP round-trip (gameplay traffic; TCP 7777 is only match-control)
ssh alfred 'python3 -c "import socket;s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.bind((\"0.0.0.0\",7777));s.settimeout(10);print(s.recvfrom(64))"' &
sleep 1.5 && echo -n probe | timeout 8 bash -c 'cat > /dev/udp/<alfred-lan-ip>/7777' && wait

# 6. Client log (the player's actual experience)
# Windows exe:        %USERPROFILE%\AppData\LocalLow\SlopArena\SlopArena\Player.log
# Steam Proton:       <steamapps>/compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/LocalLow/SlopArena/SlopArena/Player.log
# Unity Editor (lin): ~/.config/unity3d/Editor.log
# look for: [ServerBrowser], [Registration], [LobbyClient], Exception, Error
```

## Failure catalog (each hit for real 2026-08-02)

### Join hangs — UFW dropping game ports
Symptom: client log ends at `[ServerBrowser] Joining server: ...` then silence;
LAN TCP probe times out; loopback works.
Fix: `ssh alfred 'sudo ufw allow 7777/tcp && sudo ufw allow 7777:7791/udp'` —
alfred's UFW has `INPUT policy DROP`; the Bbox forward alone is NOT enough.
Verify: `sudo ufw status | grep 7777`, re-run probes. Rule must match
`server.json` port + `maxConcurrentMatches` (4 matches = 7777-7780; 15 = 7777-7791).

### Registration fails `400 Invalid IP address: <domain>`
Master's validator rejected DNS hostnames (fixed: `Uri.CheckHostName is IPv4 or
Dns`). If it recurs, the deployed master is stale → redeploy (flow C in
sloparena-build-export). Then `docker compose restart server-1` — the game
server registers ONCE at startup and NEVER retries.

### Heartbeat stale / server not in GameServers
Server crashed, or was restarted before registering. Check
`docker compose logs server-1` for the crash; restart: `docker compose restart
server-1`. Verify age returns to seconds.

### rsync wiped the master config
`--delete` / `--delete-excluded` on the master publish dir deletes
`appsettings.Production.json` (it lives inside `publish/`). Never use
`--delete*` on that rsync. If gone: rebuild with the DB password from
`/root/homelab/sloparena/.env` + fresh JWT (`openssl rand -base64 48`), chmod
600. Old JWTs die — clients re-auth.

### False 429s in master tests
`RateLimitTracker` is a DI singleton now (per app instance); if it regresses to
static, parallel test factories share one bucket → 429s. Check Program.cs
registers it and the middleware uses `context.RequestServices`.

### Unity player build fails
- `build target was unsupported` → Windows Build Support module missing on
  Linux: `/opt/unityhub/unityhub-bin -- --headless install-modules --version
  6000.0.78f1 -m windows-mono`
- `error CS0234: UnityEditor...` in a runtime script → editor-only API leaked
  into player build; guard with `#if UNITY_EDITOR`.
- `dotnet build` does NOT compile Unity scripts — validate with Unity
  batchmode import (`-batchmode -quit -nographics`) or the player build.

### Version stamp / PipelineAsset drift after a failed release build
`build-release.sh` stamps `bundleVersion` pre-build, reverts post-build; a
failure leaves the tree dirty. Restore:
```bash
git checkout -- client/Unity/ProjectSettings/ProjectSettings.asset \
  client/Unity/Assets/Settings client/Unity/Assets/UniversalRenderPipelineGlobalSettings.asset
rm -rf client/Unity/Assets/StreamingAssets/Server client/Unity/Assets/StreamingAssets/arenas \
  client/Unity/Assets/StreamingAssets.meta client/Unity/Assets/packages-merged-link*
```

### Dev machine DNS broken (known)
Resolver is misconfigured (systemd-resolved/NetworkManager). Use
`dig @1.1.1.1` and `curl --resolve HOST:443:188.114.97.2` when verifying the
tunnel. Not a server fault.

## Arena note
`data/arenas/` has 7 files; 4 are <500B stubs (cross/pit/sanctum/split) that
fail `[ArenaRegistry] Failed to load` — expected. Playable: training,
colosseum, Island_arena.

## Deploying (cross-ref)
- Dedicated server: `scripts/deploy-server.sh` (publish → rsync → restart → verify)
- Exe zip: `scripts/build-release.sh <version>`
- Master: manual, master repo (see sloparena-build-export skill, flow C)
- Depth: `docs/systems/production-hosting.md`, `docs/systems/troubleshooting.md`
