#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY_EDITOR:-/home/binoui/Unity/Hub/Editor/6000.0.78f1/Editor/Unity}"
PROJECT="$ROOT/client/Unity"

[[ -d "$PROJECT" ]] || { echo "error: Unity project missing: $PROJECT" >&2; exit 1; }
[[ -x "$UNITY" ]] || { echo "error: Unity editor is not executable: $UNITY" >&2; exit 1; }

echo "== Verify committed FightGuy package in Unity =="
"$UNITY" -batchmode -quit -nographics -projectPath "$PROJECT" \
  -executeMethod SlopArenaCharacterCook.VerifyCommittedFightGuy
