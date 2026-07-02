# SlopArena Project Rules

- NEVER commit or push without explicit permission. "commit" = commit only, "commit push" = commit+push, "push" = push existing.
- Always explain findings first (full pipeline trace), then propose fix. No code changes without "go"/"vas y".
- Implement numeric choices without arguing. Suggest once only if correctness issue, then implement their value.
- Never install anything without asking.
- Server-side simulation is the source of truth for everything — no client-side hacks for gameplay mechanics.
- NEVER use the `unity-mcp/Unity_*` tools (IvanMurzak package AI tools). Only use the custom `gamedev-mcp-server` at `localhost:26356/mcp` via its scripts or direct SSE calls.
- MCP: `gamedev-mcp-server` at `localhost:26356/mcp`. Tools: `script-execute`, `assets-refresh`, `assets-find`, `console-get-logs`, `editor-application-*`, `animation-create`. See `.omp/skills/unity-mcp-gamedev/SKILL.md` for full reference.
