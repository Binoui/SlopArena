#!/usr/bin/env python3
"""Unwrap gamedev-mcp-server SSE tool results.

Reads `data: {...}` lines from stdin (as produced by scripts/mcp-call.sh), extracts
the tool result (the JSON text payload), and prints:
  - the `result.value` string when present (the common case for script-execute)
  - pretty JSON otherwise
Exits 1 on MCP-level errors (tool not found, server error).

Usage: scripts/mcp-call.sh <tool> '<args>' | python3 scripts/mcp-unwrap.py
"""
import json
import sys


def main() -> int:
    saw_data = False
    for line in sys.stdin:
        line = line.strip()
        if not line.startswith("data:"):
            continue
        saw_data = True
        try:
            d = json.loads(line[5:])
        except json.JSONDecodeError as e:
            print(f"ERROR: bad SSE payload: {e}", file=sys.stderr)
            return 1
        if d.get("error"):
            err = d["error"]
            print(f"ERROR: {err.get('message', err)}", file=sys.stderr)
            return 1
        result = d.get("result") or {}
        for c in result.get("content", []):
            try:
                t = json.loads(c.get("text", ""))
            except (json.JSONDecodeError, TypeError):
                t = c.get("text", "")
            if isinstance(t, dict) and "result" in t:
                t = t["result"]
            if isinstance(t, dict) and "value" in t:
                print(t["value"], end="\n" if not t["value"].endswith("\n") else "")
            else:
                print(json.dumps(t, indent=2, ensure_ascii=False))
    if not saw_data:
        print("ERROR: no SSE data received (editor busy/offline?)", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
