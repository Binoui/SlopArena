# SlopArena Project Rules

- NEVER commit or push without explicit permission. "commit" = commit only, "commit push" = commit+push, "push" = push existing.
- Always explain findings first (full pipeline trace), then propose fix. No code changes without "go"/"vas y".
- Implement numeric choices without arguing. Suggest once only if correctness issue, then implement their value.
- Never install anything without asking.
- Server-side simulation is the source of truth for everything — no client-side hacks for gameplay mechanics.
- Unity interaction MUST use the installed Unity CLI and `com.unity.pipeline`:
  `unity pipeline list --format json`, `unity command --project-path client/Unity ...`.
  NEVER use the removed Unity MCP tools, `gamedev-mcp-server`, `localhost:26356`,
  or deleted `scripts/mcp-*.sh` wrappers. Follow `docs/contributing/unity-cli.md`.
