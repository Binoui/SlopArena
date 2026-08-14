#!/usr/bin/env bash
# Run a C# script file in the Unity editor via the gamedev MCP (script-execute).
#
# Usage:
#   scripts/mcp-script.sh <file.cs>              # full compile: file defines
#                                                #   public class Script { public static string Main() {...} }
#   scripts/mcp-script.sh -b <file.cs>           # method body: file is the body of Main()
#
# Writes the file's content as the tool payload (no shell-quoting hell — write the
# .cs with the Write tool, pass the path), prechecks the editor, applies a timeout,
# and unwraps the SSE result. Prints the tool's return string.
#
# Gotchas it encodes:
#   - the script-execute full-compile mode REQUIRES the class to be named `Script`
#   - multi-line code passed inline breaks jq/quoting — always use a file
#   - a hang here usually means the editor is compiling or in a dialog — not that
#     the tool is broken; MCP_TIMEOUT env var controls the wait (default 60s)

set -euo pipefail
cd "$(dirname "$0")/.."

BODY_MODE=false
if [ "${1:-}" = "-b" ]; then BODY_MODE=true; shift; fi
CS="${1:-}"
[ -f "$CS" ] || { echo "usage: $0 [-b] <file.cs>" >&2; exit 2; }

timeout 10 scripts/mcp-check.sh > /dev/null 2>&1 \
  || { echo "ERROR: editor busy or offline (mcp-check failed)" >&2; exit 1; }

timeout "${MCP_TIMEOUT:-60}" scripts/mcp-call.sh script-execute \
  "$(jq -n --rawfile c "$CS" --argjson body "$BODY_MODE" '{csharpCode: $c, isMethodBody: $body}')" \
  | python3 scripts/mcp-unwrap.py
