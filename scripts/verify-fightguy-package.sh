#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_CLI="${UNITY_CLI:-$HOME/.local/bin/unity}"
PROJECT="$ROOT/client/Unity"

[[ -d "$PROJECT" ]] || { echo "error: Unity project missing: $PROJECT" >&2; exit 1; }
[[ -x "$UNITY_CLI" ]] || { echo "error: Unity CLI is not executable: $UNITY_CLI" >&2; exit 1; }

echo "== Verify cooked FightGuy package through Pipeline =="
response="$("$UNITY_CLI" command --project-path "$PROJECT" \
  sloparena.character.inspect --target fightguy --format json)"
printf '%s\n' "$response"
jq -e '.data.result.success and .data.result.status == "valid" and .data.result.dirtyOrStale == false' <<<"$response" >/dev/null
