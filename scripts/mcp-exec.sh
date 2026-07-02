#!/usr/bin/env bash
# Run a C# script in Unity via gamedev-mcp-server
# Usage: SID=<sid> scripts/mcp-exec.sh <code> [isMethodBody]
#   isMethodBody defaults to false (full class mode)
#   For body mode: SID=$SID scripts/mcp-exec.sh 'Debug.Log("hi");' true

SID="${MCP_SID:-$SID}"
CODE="$1"
BODY="${2:-false}"

if [ -z "$SID" ] || [ -z "$CODE" ]; then
    echo "Usage: SID=\$SID $0 '<csharp_code>' [isMethodBody]"
    exit 1
fi

# Escape the code for JSON
ESCAPED=$(printf '%s' "$CODE" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')

curl -s http://localhost:26356/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: $SID" \
  -d "$(jq -n \
    --arg tool "script-execute" \
    --arg code "$CODE" \
    --argjson body "$BODY" \
    '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":$tool,"arguments":{"csharpCode":$code,"isMethodBody":$body}}}')"
