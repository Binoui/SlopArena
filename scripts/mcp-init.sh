#!/usr/bin/env bash
# Initialize MCP session with gamedev-mcp-server
# Usage: source scripts/mcp-init.sh
# Sets $SID env var for subsequent calls

headers=$(mktemp /tmp/mcp_headers.XXXXXX)
curl -s -D "$headers" http://localhost:26356/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"omp","version":"1.0"}}}' > /dev/null

SID=$(grep -i 'mcp-session' "$headers" | awk '{print $2}' | tr -d '\r\n')
rm "$headers"

if [ -z "$SID" ]; then
    echo "ERROR: Failed to initialize MCP session" >&2
    return 1
fi
echo "SID=$SID"
