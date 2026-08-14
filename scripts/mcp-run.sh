#!/usr/bin/env bash
# Call any gamedev-mcp-server tool with editor precheck, timeout and result unwrapping.
#
# Usage:
#   scripts/mcp-run.sh <tool-name> '<json-args>'
#
# Same as scripts/mcp-call.sh --json but with a busy-editor precheck and a hard
# timeout (MCP_TIMEOUT env var, default 60s). Prints the unwrapped result value.

set -euo pipefail
cd "$(dirname "$0")/.."

[ $# -ge 1 ] || { echo "usage: $0 <tool-name> '<json-args>'" >&2; exit 2; }

timeout 10 scripts/mcp-check.sh > /dev/null 2>&1 \
  || { echo "ERROR: editor busy or offline (mcp-check failed)" >&2; exit 1; }

timeout "${MCP_TIMEOUT:-60}" scripts/mcp-call.sh "$1" "${2:-"{}"}" | python3 scripts/mcp-unwrap.py
