#!/usr/bin/env bash
# Call any tool on gamedev-mcp-server
# Usage: scripts/mcp-call.sh <tool-name> '<json-args>'
#   args can be omitted for tools needing no arguments
# Auto-initializes session — no SID management needed.
#
# Example:
#   scripts/mcp-call.sh gameobject-find '{"gameObjectRef":{"name":"Player"}}'
#   scripts/mcp-call.sh particle-system-get '{"gameObjectRef":{"name":"BoneTrail"}}'

SID="${MCP_SID:-$SID}"

# ── Auto-init if no session ──
if [ -z "$SID" ]; then
  sid_file="/tmp/.mcp_sid"
  [ -f "$sid_file" ] && SID=$(cat "$sid_file")
fi

if [ -z "$SID" ]; then
  headers=$(mktemp /tmp/mcp_headers.XXXXXX)
  curl -s -D "$headers" http://localhost:26356/mcp \
    -H "Content-Type: application/json" \
    -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"omp","version":"1.0"}}}' > /dev/null
  SID=$(grep -i 'mcp-session' "$headers" | awk '{print $2}' | tr -d '\r\n')
  rm "$headers"
  if [ -z "$SID" ]; then
    echo "ERROR: Failed to initialize MCP session" >&2
    exit 1
  fi
  echo "$SID" > /tmp/.mcp_sid
fi

TOOL="$1"
[ $# -ge 2 ] && ARGS="$2" || ARGS="{}"

if [ -z "$TOOL" ]; then
    echo "Usage: $0 <tool-name> '<json-args>'" >&2
    echo "Examples:" >&2
    echo "  $0 gameobject-find '{\"gameObjectRef\":{\"name\":\"Player\"}}'" >&2
    echo "  $0 particle-system-modify '{\"gameObjectRef\":{\"name\":\"BoneTrail\"},\"main\":{\"startSpeed\":0}}'" >&2
    exit 1
fi

curl -s http://localhost:26356/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: $SID" \
  -d "$(jq -n \
    --arg tool "$TOOL" \
    --argjson args "$ARGS" \
    '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":$tool,"arguments":$args}}')"
