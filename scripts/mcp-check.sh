#!/usr/bin/env bash
# Probe gamedev-mcp-server liveness.
# Exit 0 = server is alive and responded.
# Exit 1 = server is down or Unity is not running.
# Usage: scripts/mcp-check.sh
#        scripts/mcp-check.sh --quiet   (no output, just exit code)

QUIET=false
[ "${1:-}" = "--quiet" ] && QUIET=true

response=$(curl -s --max-time 2 http://localhost:26356/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"omp","version":"1.0"}}}' 2>/dev/null)

if echo "$response" | grep -q '"result"'; then
  $QUIET || echo "gamedev-mcp-server: OK (Unity is running)"
  exit 0
else
  $QUIET || echo "gamedev-mcp-server: DOWN — start Unity and wait for the MCP server to bind on :26356"
  exit 1
fi
