---
name: sloparena-build-export
description: Build, export, and release SlopArena — Shared build, tests, server publish (mini PC deploy), Unity player build, GitHub release, CI status. The three flows: build-release.sh (exe zip), deploy-server.sh (alfred dedicated server), master deploy (manual).
---

# SlopArena Build, Export & Release

## When to use

- Building or testing the Shared library or the .NET server
- Publishing the game server for release (zip bundled server, or alfred dedicated server)
- Building the Unity player (Linux/Windows)
- Cutting a GitHub release, or checking CI status
- Answering "how do I ship main to players / to alfred"

## The three flows (mental model)

```
A. New exe zip (friends)      → scripts/build-release.sh <version>
B. Dedicated server on alfred → scripts/deploy-server.sh [host]
C. Master server on alfred    → manual (master repo, see below)

Client-only change  → A only
src/Server|Shared   → A (bundled server) + B (dedicated server)
Master repo change  → C only
```

## Build & Test Core

```bash
# Shared library (canonical source of truth) — post-build copies the DLL
# into client/Unity/Assets/Plugins/SlopArena.Shared/ automatically.
dotnet build src/Shared/ --nologo

# Test suite (~451 tests)
dotnet test tests/Shared.Tests/ --nologo
```

Always build `src/Shared/` after any Shared change so the Unity plugin DLL stays in sync.

## Flow A — Release zip (Windows exe)

```bash
# Preconditions: Unity Editor CLOSED (locks the project; a second instance
# aborts with "Another Unity instance is running with this project open").
# ProjectSettings.asset must be CLEAN (the script stamps bundleVersion then
# reverts via git checkout, and refuses to run otherwise).
./scripts/build-release.sh 0.2.0-demo.1
# → build/release/SlopArena-<version>.zip (90MB), contains:
#   SlopArena.exe + SlopArena_Data/ + StreamingAssets/Server/ (self-contained
#   win-x64 game server, embedded host-and-play) + arenas + README/HOSTING.txt
```

What the script does: build Shared → run tests → publish win-x64 self-contained
server to `StreamingAssets/Server` → publish linux-x64 to `build/minipc` →
stage arenas → stamp `bundleVersion` → Unity `-buildWindows64Player` → restore
stamp → unstage build artifacts → zip with docs.

**Leak guard (Task 7.2):** the script deletes `server.json` from both publish
outputs — the csproj copies dev defaults (`localhost:5000`) which must never
ship or clobber the live config. Verify after building:

```bash
unzip -l build/release/*.zip | grep -c server.json        # expect 0
unzip -q build/release/*.zip -d /tmp/scan && grep -rl "localhost:5000" /tmp/scan
```

Publish (public — needs explicit operator go):

```bash
gh release create v0.2.0-demo.1 build/release/SlopArena-0.2.0-demo.1.zip \
  --title "SlopArena 0.2.0-demo.1" \
  --notes "$(sed 's/<version>/0.2.0-demo.1/' docs/release/RELEASE_NOTES.template.md)"
```

The `--notes` text above is a placeholder: pushing the `v*` tag (which
`gh release create` does automatically) fires `.github/workflows/patch-notes.yml`
within a minute or two, which overwrites the release notes with a generated
factual changelog (parsed from Conventional Commit subjects since the
previous tag) plus a short DeepSeek-written context blurb, and copies through
the static Online/How-to-play/Known-issues sections from
`docs/release/RELEASE_NOTES.template.md`. Requires a `DEEPSEEK_API_KEY` repo
secret (Settings → Secrets → Actions) — without it the job fails loudly
(nuget-publish still succeeds independently). To preview or re-run locally:
`DEEPSEEK_API_KEY=... GH_TOKEN=$(gh auth token) python3 scripts/generate_patch_notes.py v0.2.0-demo.1`.

## Flow B — Dedicated server on alfred (one command)

```bash
scripts/deploy-server.sh            # ssh alias "alfred" by default
# publish linux-x64 → rsync binaries + arenas → restart server-1 → verify
# registration + heartbeat freshness in postgres
```

Why the restart: the game server registers with the master **once at startup
and never retries** — after any deploy (or master redeploy) a restart is
required or it stays unregistered.

Safety: never uses rsync `--delete*` (would wipe live config); publish output
has `server.json` deleted so `/srv/sloparena/server/server.json` (the live
config: `masterServerUrl`, `publicIp`, `maxConcurrentMatches: 4`) is never
clobbered.

## Flow C — Master server deploy (manual, master repo)

```bash
cd ~/Documents/projects/SlopArena-MasterServer
# /tmp, NOT build/: publishing inside the repo pulls MasterServer.Tests
# bin/obj into the output and grows recursively on re-publish → MSB3030.
dotnet publish -c Release -o /tmp/minipc-master
rsync -avz --exclude 'appsettings.Production.json' /tmp/minipc-master/ \
  alfred:/srv/sloparena/master/publish/
# NEVER rsync --delete / --delete-excluded here (config lives inside publish/)
ssh alfred 'cd /root/homelab/sloparena && docker compose restart master server-1'
# server-1 too: it must re-register (registers once, never retries)
```

## Unity Player Build (manual, not scripted)

```bash
"$UNITY_EDITOR" -batchmode -quit -projectPath client/Unity \
  -buildLinux64Player build/linux/SlopArena.x86_64
# or -buildWindows64Player build/windows/SlopArena.exe
```

`$UNITY_EDITOR` = `/home/binoui/Unity/Hub/Editor/6000.0.78f1/Editor/Unity`.
**Linux editors need the Windows Build Support (Mono) module** to build
Windows players — install via
`/opt/unityhub/unityhub-bin -- --headless install-modules --version 6000.0.78f1 -m windows-mono`.
Player builds fail on editor-only API in runtime scripts
(`UnityEditor.*` — grep `Assets/Scripts/Runtime/`); `dotnet build` does NOT
compile Unity scripts, so validate with a Unity batchmode import
(`-batchmode -quit -nographics`) before claiming compile-clean.

## CI Status (as of 2026-08)

- `.github/workflows/ci.yml` (this repo): push to main + PR → build Shared, run
  Shared.Tests (~451), build Server.
- `.github/workflows/patch-notes.yml`: push of tag `v*` → generates and
  publishes release notes (see Flow A Publish above). Needs `DEEPSEEK_API_KEY`.
- `.github/workflows/{nuget-publish, discord-push}.yml`: existing.
- Master repo `.github/workflows/build.yml`: build + test on push/PR to main;
  on `v*` tag push, publishes `dotnet publish -c Release` output as an Actions
  artifact.
- **No Unity build in CI** (exe is built locally by Flow A). Deploy is manual
  (home infra is not CI-reliable).

## Operations (alfred)

```bash
ssh alfred 'cd /root/homelab/sloparena && docker compose ps'   # 3 containers Up
curl -s https://sloparena.barakaslurp.fr/health                # {"status":"ok",...}
# registration + heartbeat freshness (age should be seconds):
ssh alfred 'docker exec sloparena-postgres psql -U sloparena -d sloparena -c \
  "SELECT \"Name\", \"IpAddress\", \"Port\", NOW() - \"LastHeartbeat\" AS age FROM \"GameServers\";"'
# game ports MUST be open in UFW too (Bbox forward alone is not enough):
ssh alfred 'sudo ufw status | grep 7777'   # 7777/tcp + 7777:7791/udp ALLOW
```

Backups: `/etc/cron.d/sloparena-backup` — weekly pg_dump Mondays 04:30, keep 90
days. See `docs/systems/production-hosting.md` (runbook) and
`docs/systems/troubleshooting.md` (failure playbook) for depth.

## Gotchas (all hit for real 2026-08-02)

- **Unity `.meta` files must be committed** — Unity regenerates GUIDs otherwise, breaking prefab/script references.
- **`Library/` is gitignored** — never commit it; it's a local cache.
- **Shared DLL drift** — if the Unity client shows stale behavior, rebuild `src/Shared/` (the plugin copy is post-build).
- **UFW drops game ports** — "Join hangs" = client log shows the join line then silence; fix is `ufw allow 7777/tcp + 7777:7791/udp` on alfred.
- **rsync `--delete-excluded` deletes excluded files** — it wiped `appsettings.Production.json` once. Never use `--delete*` on the master or server deploys.
- **Version-stamp drift after failed build** — `build-release.sh` aborts leave `bundleVersion` stamped + `StreamingAssets/` staged + PipelineAsset/URP re-serialized. Restore: `git checkout -- client/Unity/ProjectSettings/ProjectSettings.asset client/Unity/Assets/Settings client/Unity/Assets/UniversalRenderPipelineGlobalSettings.asset`, `rm -rf client/Unity/Assets/StreamingAssets/... client/Unity/Assets/packages-merged-link*`.
- **Editor lock** — a running Unity Editor on the project aborts batch builds; close it first.
- **`data/arenas/` stubs** — 4 of 7 files (cross/pit/sanctum/split) are <500B placeholders that fail `[ArenaRegistry] Failed to load`; expected, not a deploy bug. Playable: training, colosseum, Island_arena.
