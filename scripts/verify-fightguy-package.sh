#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GATEWAY="/home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts"
PROJECT="$ROOT/client/Unity"

[[ -d "$PROJECT" ]] || { echo "error: Unity project missing: $PROJECT" >&2; exit 1; }
[[ -f "$GATEWAY" ]] || { echo "error: Unity gateway missing: $GATEWAY" >&2; exit 1; }
: "${ORCA_TERMINAL_HANDLE:?Set your own runtime-issued terminal handle before verification}"

echo "== Verify cooked FightGuy package through Pipeline =="
response="$(bun "$GATEWAY" --project-path "$PROJECT" -- \
  sloparena.character.inspect --target fightguy)"
printf '%s\n' "$response"
jq -s -e 'any(.[]; .data.command == "sloparena.character.inspect" and .data.result.success and .data.result.status == "valid" and .data.result.dirtyOrStale == false)' <<<"$response" >/dev/null
