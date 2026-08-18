#!/usr/bin/env bash
# Movement data sheet (issue #150): measured run/dash/jump/fall/stop from the real sim,
# side-by-side per character. Default: all characters, docs/generated/movement.{html,md}.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tools/MovementReport -- "$@"
