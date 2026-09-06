#!/usr/bin/env bash
# Build a Windows demo release: SlopArena-<version>.zip in build/release/.
# Usage: scripts/build-release.sh <version>   e.g. scripts/build-release.sh 0.2.0-demo.1
set -euo pipefail

VERSION="${1:?usage: build-release.sh <version>}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY_EDITOR:-/home/binoui/Unity/Hub/Editor/6000.0.78f1/Editor/Unity}"
PROJ="$ROOT/client/Unity"
REL="$ROOT/build/release/SlopArena-$VERSION"
SA="$PROJ/Assets/StreamingAssets"
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
verify_roster_payloads "$ROOT/content-cooked"


echo "== Shared build =="
dotnet build "$ROOT/src/Shared/" --nologo

echo "== Tests =="
dotnet test "$ROOT/tests/Shared.Tests/" --nologo

echo "== Self-contained Windows server (embedded host-and-play) =="
dotnet publish "$ROOT/src/Server/SlopArena.Server.csproj" -c Release -r win-x64 --self-contained true -o "$SA/Server"
# The csproj copies server.json (dev defaults, localhost:5000) into publish
# output; both release flows (bundled host-and-play, dedicated compose) pass an
# explicit config path as arg[0], so the shipped file is dead weight AND leaks
# localhost:5000 into the zip (Task 7.2: must appear NOWHERE). Drop it.
rm -f "$SA/Server/server.json"
verify_roster_payloads "$SA/Server/content-cooked"

echo "== linux-x64 server for the mini PC =="
dotnet publish "$ROOT/src/Server/SlopArena.Server.csproj" -c Release -r linux-x64 --self-contained false -o "$ROOT/build/minipc"
# Same rationale: rsync'ing this onto alfred must not clobber the live
# server.json (real masterServerUrl + publicIp).
rm -f "$ROOT/build/minipc/server.json"
verify_roster_payloads "$ROOT/build/minipc/content-cooked"
echo "== Stage canonical cooked roster =="
mkdir -p "$SA/arenas" "$SA/content-cooked" "$SA/Server/content-cooked"
cp "$ROOT"/data/arenas/*.arena "$SA/arenas/"
cp -R "$ROOT/content-cooked/." "$SA/content-cooked/"
cp -R "$ROOT/content-cooked/." "$SA/Server/content-cooked/"
verify_roster_payloads "$SA/content-cooked"
verify_roster_payloads "$SA/Server/content-cooked"
test ! -e "$SA/content/characters/fightguy/character.json"
test ! -e "$SA/Server/content/characters/fightguy/character.json"
test ! -e "$SA/data/fightguy_skeleton.bin"
test ! -e "$SA/Server/data/fightguy_skeleton.bin"

echo "== Version stamp =="
# The stamp is reverted below with a hard checkout of the committed file; refuse
# to run if ProjectSettings.asset has uncommitted edits (they would be lost).
git -C "$ROOT" diff --quiet -- client/Unity/ProjectSettings/ProjectSettings.asset \
  || { echo "error: ProjectSettings.asset has uncommitted changes -- commit or stash them first (the version stamp is reverted via git checkout)" >&2; exit 1; }
sed -i "s/^  bundleVersion: .*/  bundleVersion: $VERSION/" "$PROJ/ProjectSettings/ProjectSettings.asset"

echo "== Unity Windows player build =="
mkdir -p "$REL"
"$UNITY" -batchmode -quit -projectPath "$PROJ" -buildWindows64Player "$REL/SlopArena.exe"

echo "== Restore committed bundleVersion =="
git -C "$ROOT" checkout -- client/Unity/ProjectSettings/ProjectSettings.asset

echo "== Unstage build-only artifacts =="
rm -rf "$SA/Server" "$SA/arenas" "$SA/data" "$SA/content" "$SA/content-cooked"

echo "== Ship docs + zip =="
cp "$ROOT/docs/release/PLAY_GUIDE.md" "$REL/README.txt"
cp "$ROOT/docs/release/HOST_GUIDE.md" "$REL/HOSTING.txt"
(cd "$REL/.." && zip -r "SlopArena-$VERSION.zip" "SlopArena-$VERSION")
echo "DONE: build/release/SlopArena-$VERSION.zip"
