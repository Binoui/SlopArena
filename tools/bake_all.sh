#!/bin/bash
# Re-bake skeleton data for all characters.
# Usage: ./tools/bake_all.sh
# Requires Unity CLI on PATH or UNITY_HOME.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
UNITY="${UNITY_HOME:+$UNITY_HOME/}Unity"
# Auto-detect Unity Hub installations
if ! command -v "$UNITY" &>/dev/null; then
    for dir in "$HOME/Unity/Hub/Editor"/*/Editor; do
        if [ -x "$dir/Unity" ]; then
            UNITY="$dir/Unity"
            break
        fi
    done
fi

echo "=== Baking all characters via Unity batch mode ==="
echo "Project: $PROJECT_DIR/client/Unity"

$UNITY -batchmode -quit -projectPath "$PROJECT_DIR/client/Unity" \
    -executeMethod SlopArenaBaker.BakeAllCharacters \
    -logFile - 2>&1 | grep -E '\[BakeAll\]|BakeSkeleton|Error|error'

echo ""
echo "=== Done ==="
ls -la "$PROJECT_DIR"/data/*.skeleton.bin 2>/dev/null || echo "(no .skeleton.bin files found)"
