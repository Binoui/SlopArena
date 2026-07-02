#!/usr/bin/env bash
# Call a gamedev-mcp-server tool
# Usage: MCP_SID=<sid> scripts/mcp-call.sh <tool_name> '<json_arguments>'
#   or  SID=<sid> scripts/mcp-call.sh <tool_name> '<json_arguments>'

TOOL="${1:-}"
ARGS="${2:-{}}"
SID="${MCP_SID:-$SID}"

if [ -z "$SID" ] || [ -z "$TOOL" ]; then
    echo "Usage: SID=<sid> $0 <tool_name> '<json_args>'"
    echo "  e.g.: SID=\$SID $0 assets-refresh '{}'"
    echo "  e.g.: SID=\$SID $0 script-execute '{\"csharpCode\":\"...\"}'"
    exit 1
fi

curl -s http://localhost:26356/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: $SID" \
  -d "$(jq -n \
    --arg tool "$TOOL" \
    --argjson args "$ARGS" \
    '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":$tool,"arguments":$args}}')" 2>/dev/null
