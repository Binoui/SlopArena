---
name: sloparena-build
description: Verify SlopArena Unity compilation and affected runtime behavior through Unity CLI and com.unity.pipeline after Unity-facing changes or compiler errors. Use docs/testing.md to distinguish local iteration from package publishing.
---
# SlopArena Unity CLI Build Gate

Use this skill after changes under `client/Unity/Assets/` or when Unity reports a
compiler/runtime error. Choose the applicable verification mode in
[`docs/testing.md`](../../../docs/testing.md).

## Local iteration

Use the existing Editor development content and exercise the affected Ability Lab or
Training path. Recompile only when changed C# or the Shared plugin requires it; prose
and numerical JSON tuning do not require a forced project recompile.

## Integrated change

For Unity-facing code or plugin changes:

1. Build Shared when Shared code or the Unity Shared plugin may be stale:

   ```bash
   dotnet build src/Shared/ --nologo
   ```

2. Confirm the live Editor/Pipeline connection:

   ```bash
   unity pipeline list --format json
   ```

   Require `isReachable: true`.

3. Request and monitor a project recompile:

   ```bash
   unity command --project-path client/Unity recompile --format json
   unity command --project-path client/Unity recompile_status --format json
   ```

4. Read current Unity errors and require zero compiler errors, exceptions, or asserts:

   ```bash
   unity command --project-path client/Unity \
     get_console_logs --severity error --limit 20 --format json
   ```

5. Exercise the affected path with typed CLI commands:

   ```bash
   unity command --project-path client/Unity editor_status --format json
   unity command --project-path client/Unity editor_play --format json
   unity command --project-path client/Unity editor_stop --format json
   ```

   Use `eval` or `eval_file` for targeted live C# probes. Prefer typed project commands
   over ad-hoc probes.

## Distributable demo

Accepted package changes cross the cook/inspect/roster-refresh boundary below. Verify
the packaged client/server path; do not treat local preview as package verification.

## Accepted package and asset verification

Use the typed Pipeline commands for accepted package work:

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
