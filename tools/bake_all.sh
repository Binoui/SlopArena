#!/bin/bash
# Re-bake skeleton data for all characters.
# Usage: ./tools/bake_all.sh
# Requires Godot 4 headless.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_DIR" || exit 1

echo "=== Baking Manki + Bunny ==="
godot --headless --script tools/headless_bake.gd --path . -- manki bunny

echo ""
echo "=== Done ==="
ls -la data/*.bin
