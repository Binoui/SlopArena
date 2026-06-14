#!/bin/bash
# Re-bake skeleton data for all characters.
# Usage: ./tools/bake_all.sh
# Requires Godot 4 headless.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_DIR" || exit 1

echo "=== Baking Manki ==="
/usr/bin/godot --headless --script tools/headless_bake.gd --path . 2>&1 | grep -E "Anim|Done|Error"

echo ""
echo "=== Baking Bunny ==="
/usr/bin/godot --headless --script tools/headless_bake_bunny.gd --path . 2>&1 | grep -E "Anim|Done|Error"

echo ""
echo "=== Done ==="
ls -la data/*.bin
