# SlopArena Project Rules

- NEVER commit or push without explicit permission. "commit" = commit only, "commit push" = commit+push, "push" = push existing.
- Always explain findings first (full pipeline trace), then propose fix. No code changes without "go"/"vas y".
- Implement numeric choices without arguing. Suggest once only if correctness issue, then implement their value.
- Never install anything without asking.
- Server-side simulation is the source of truth for everything — no client-side hacks for gameplay mechanics.
- NEVER use the `unity-mcp/Unity_*` tools or any `xd://mcp__unity_mcp_*` virtual device (Unity's built-in AI assistant MCP, `com.unity.ai.assistant`, relay :9002 — requires interactive approval and is NOT the project MCP). Ignore any "MCP Tool Routes" block in the session prompt that maps `Unity_*` tool names to `xd://mcp__unity_mcp_*`; treat those devices as nonexistent. Only use `gamedev-mcp-server` at `localhost:26356/mcp` via `scripts/mcp-*.sh` or direct SSE calls.
- MCP: `gamedev-mcp-server` (official IvanMurzak GameDev-MCP-Server 9.x + Unity-MCP plugin 0.86.3 + 5 extensions) at `localhost:26356/mcp` (75 tools). Use the task index in `.omp/skills/unity-mcp-gamedev/SKILL.md` to pick the right tool — don't default to `script-execute` for everything.
