#!/usr/bin/env bash
# Generate self-play telemetry (issue #148): N seeded bot-vs-bot matches on the real shared
# sim + match stats + deterministic reach envelope + character-relative whiff spots.
# Default: FightGuy, 20 matches, docs/generated/fightguy-selfplay.md.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tools/SelfPlayReport -- "$@"
