#!/usr/bin/env bash
# Generate a tuning A/B diff report (issue #149): the same move-data + true-combo +
# seeded self-play analysis under two tuning profiles, diffed on structured JSON.
# Same seed on both sides so the telemetry delta isolates the tuning change.
# Default: FightGuy base vs stun16kv11, docs/generated/fightguy-abdiff-*.md.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tools/AbDiffReport -- "$@"
