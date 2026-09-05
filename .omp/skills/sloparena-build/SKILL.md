# SlopArena Unity CLI Build Gate

Use after changes under `client/Unity/Assets/` or when Unity reports a compiler/runtime error.
SlopArena no longer uses Unity MCP. All live Editor interaction uses the Unity CLI and
`com.unity.pipeline`.

## Required gate

1. Build Shared when Shared code or the Unity Shared plugin may be stale:

   ```bash
   dotnet build src/Shared/ --nologo
   ```

2. Confirm the live Editor/Pipeline connection:

   ```bash
   unity pipeline list --format json
   ```

   Require `isReachable: true`.

3. Request and monitor a real project recompile:

   ```bash
   unity command --project-path client/Unity recompile --format json
   unity command --project-path client/Unity recompile_status --format json
   ```

4. Read current Unity errors after the recompile:

   ```bash
   unity command --project-path client/Unity \
     get_console_logs --severity error --limit 20 --format json
   ```

   Require zero current compiler errors, exceptions, or asserts. Warnings are non-blocking
   unless introduced by the change or indicative of a runtime defect.

5. Exercise the changed path with typed CLI commands when applicable:

   ```bash
   unity command --project-path client/Unity editor_status --format json
   unity command --project-path client/Unity editor_play --format json
   unity command --project-path client/Unity editor_stop --format json
   ```

   Use `eval` or `eval_file` for targeted live C# probes. Prefer typed project commands
   over ad-hoc probes.

## Package and asset verification

Use the typed Pipeline commands for package work:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

Inspect is read-only. Cook is the persistence boundary. Require valid inspect status,
`dirtyOrStale: false`, and matching hashes where those fields apply.

## Prohibited workflow

Do not use or recreate:

- `scripts/mcp-*.sh`
- `scripts/mcp-unwrap.py`
- `gamedev-mcp-server`
- `localhost:26356/mcp`
- `xd://mcp__unity_mcp_*`
- the removed IvanMurzak Unity MCP package or MCP-only extensions

If the Unity CLI or Pipeline endpoint is unavailable, report that exact failure. Do not
claim Unity verification from a shell-only build or fall back to MCP.

Canonical reference: `docs/contributing/unity-cli.md`.
