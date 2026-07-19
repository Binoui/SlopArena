#!/usr/bin/env bash
# Run a C# script in Unity via gamedev-mcp-server
# Usage:
#   scripts/mcp-exec.sh '<code>' [isMethodBody]    # raw SSE output
#   scripts/mcp-exec.sh --json '<code>' [isMethodBody]  # parsed result
# Auto-initializes session — no SID management needed.

JSON_MODE=false
[ "${1:-}" = "--json" ] && JSON_MODE=true && shift

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
    echo "Usage: $0 '[--json]' '<csharp_code>' [isMethodBody]" >&2
    exit 1
fi

RESULT=$(curl -s http://localhost:26356/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: $SID" \
  -d "$(jq -n \
    --arg tool "script-execute" \
    --arg code "$CODE" \
    --argjson body "$BODY" \
    '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":$tool,"arguments":{"csharpCode":$code,"isMethodBody":$body}}}')")

if $JSON_MODE; then
  echo "$RESULT" | python3 -c "
import json,sys
for line in sys.stdin:
    line = line.strip()
    if line.startswith('data:'):
        d = json.loads(line[5:])
        for c in d.get('result',{}).get('content',[]):
            try:
                t = json.loads(c['text'])
                v = t.get('result', {})
                if isinstance(v, dict):
                    print(v.get('value', json.dumps(v, indent=2)))
                else:
                    print(v)
            except json.JSONDecodeError:
                print(c['text'][:2000])
"
else
  echo "$RESULT"
fi