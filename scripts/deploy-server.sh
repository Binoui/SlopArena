#!/usr/bin/env bash
# Deploy the dedicated game server to the mini PC (alfred): publish linux-x64,
# rsync binaries + arenas, restart the container, verify registration.
#
# Usage: scripts/deploy-server.sh   (optionally: scripts/deploy-server.sh <host>)
#   <host> defaults to the ssh alias "alfred".
#
# This is flow B of the release pipeline (see docs/systems/release-pipeline.md):
#   A = new exe zip (scripts/build-release.sh)   — friends artifact
#   B = this script                              — dedicated server on alfred
#   C = master server deploy                     — manual, master repo
#
# Safety guarantees baked in (all three bit us on 2026-08-02):
#   - never uses rsync --delete / --delete-excluded  (would wipe live config)
#   - the publish output has server.json deleted (csproj copies dev defaults;
#     the live /srv/sloparena/server/server.json is the source of truth and
#     must never be clobbered — it holds masterServerUrl + publicIp)
#   - restarts server-1 AFTER the deploy: the game server registers with the
#     master ONCE at startup and never retries, so a restart is required
#   - verifies registration + heartbeat freshness at the end
set -euo pipefail

HOST="${1:-alfred}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/build/minipc"
COMPOSE="/root/homelab/sloparena/docker-compose.yml"
verify_roster_payloads() {
  local tree="$1"
  local package_ids
  test -f "$tree/roster/manifest.json"
  cmp "$ROOT/content-cooked/roster/manifest.json" "$tree/roster/manifest.json"
  package_ids="$(jq -er '.entries[].packageId' "$ROOT/content-cooked/roster/manifest.json")"
  while IFS= read -r package_id; do
    test -n "$package_id"
    for package_file in manifest.json character.runtime.json poses.bin client.bindings; do
      test -f "$tree/$package_id/$package_file"
    done
    cmp "$ROOT/content-cooked/$package_id/manifest.json" "$tree/$package_id/manifest.json"
  done <<< "$package_ids"
}
echo "== Verify complete cooked roster =="
dotnet test "$ROOT/tests/Shared.Tests/" --nologo

echo "== Publish linux-x64 (framework-dependent) =="
dotnet publish "$ROOT/src/Server/SlopArena.Server.csproj" -c Release \
  -r linux-x64 --self-contained false -o "$OUT" --nologo
# server.json is copied by the csproj (dev defaults, localhost:5000); drop it —
# the live config on alfred is authoritative and must never be overwritten.
rm -f "$OUT/server.json"
verify_roster_payloads "$OUT/content-cooked"

echo "== Prepare target dir =="
ssh "$HOST" "sudo mkdir -p /srv/sloparena/server && sudo chown alfred:alfred /srv/sloparena/server"

echo "== Rsync binaries (no --delete: never wipe live config/arenas) =="
rsync -avz "$OUT/" "$HOST:/srv/sloparena/server/"
rsync -avz "$ROOT/data/arenas/"*.arena "$HOST:/srv/sloparena/server/arenas/"

echo "== Restart server-1 (required: registers once, never retries) =="
ssh "$HOST" "cd /root/homelab/sloparena && docker compose -f $COMPOSE restart server-1"
sleep 5

echo "== Verify registration + heartbeat =="
ssh "$HOST" "docker exec sloparena-postgres psql -U sloparena -d sloparena -c \
  \"SELECT \\\"Name\\\", \\\"IpAddress\\\", \\\"Port\\\", \\\"IsOfficial\\\", NOW() - \\\"LastHeartbeat\\\" AS age FROM \\\"GameServers\\\";\""

echo "== Server logs (tail) =="
ssh "$HOST" "docker compose -f $COMPOSE logs --tail 6 server-1 2>&1 | grep -viE 'ArenaRegistry|ArenaCollision'"
echo "DONE: server-1 deployed to $HOST"
