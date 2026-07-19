#!/usr/bin/env bash
# Run a C# script in Unity via gamedev-mcp-server
# Usage: scripts/mcp-exec.sh <code> [isMethodBody]
#   isMethodBody defaults to false (full class mode)
#   For body mode: scripts/mcp-exec.sh 'Debug.Log("hi");' true
# Auto-initializes session — no SID management needed.

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

CODE="$1"
BODY="${2:-false}"

if [ -z "$CODE" ]; then
    echo "Usage: $0 '<csharp_code>' [isMethodBody]" >&2
    exit 1
fi

curl -s http://localhost:26356/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: $SID" \
  -d "$(jq -n \
    --arg tool "script-execute" \
    --arg code "$CODE" \
    --argjson body "$BODY" \
    '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":$tool,"arguments":{"csharpCode":$code,"isMethodBody":$body}}}')"
