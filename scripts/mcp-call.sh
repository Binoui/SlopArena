#!/usr/bin/env bash
# Call any tool on gamedev-mcp-server
# Usage:
#   scripts/mcp-call.sh <tool-name> '<json-args>'    # raw SSE
#   scripts/mcp-call.sh --json <tool-name> '<json-args>'  # parsed result
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

TOOL="$1"
[ $# -ge 2 ] && ARGS="$2" || ARGS="{}"

if [ -z "$TOOL" ]; then
    echo "Usage: $0 '[--json]' '<tool-name>' '<json-args>'" >&2
    echo "Examples:" >&2
    echo "  $0 gameobject-find '{\"gameObjectRef\":{\"name\":\"Player\"}}'" >&2
    echo "  $0 --json particle-system-get '{\"gameObjectRef\":{\"name\":\"BoneTrail\"}}'" >&2
    exit 1
fi

RESULT=$(curl -s http://localhost:26356/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: $SID" \
  -d "$(jq -n \
    --arg tool "$TOOL" \
    --argjson args "$ARGS" \
    '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":$tool,"arguments":$args}}')")

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
                    print(json.dumps(v, indent=2)[:2000])
                else:
                    print(str(v)[:2000])
            except json.JSONDecodeError:
                print(str(c['text'])[:2000])
"
else
  echo "$RESULT"
fi