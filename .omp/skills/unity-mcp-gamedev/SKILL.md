# Unity CLI

> This file preserves the historical skill path. SlopArena no longer uses Unity MCP.
> Use the Unity CLI with `com.unity.pipeline` for all Unity interaction.

## Current workflow

The canonical workflow is documented in [`docs/contributing/unity-cli.md`](../../../docs/contributing/unity-cli.md).

The standalone CLI is installed at `~/.local/bin/unity`:

```bash
unity --version
unity doctor
unity pipeline list --format json
```

Pipeline connects to the running Unity Editor through its localhost endpoint. It replaces
all former `scripts/mcp-*.sh`, `gamedev-mcp-server`, and `localhost:26356` operations.

## Unity-facing verification gate

After any Unity-facing change:

```bash
# Confirm the live Editor/Pipeline connection.
unity pipeline list --format json

# Request and monitor a real project recompile.
unity command --project-path client/Unity recompile --format json
unity command --project-path client/Unity recompile_status --format json

# Read current errors after compilation.
unity command --project-path client/Unity \
  get_console_logs --severity error --limit 20 --format json
```

Require a successful recompile and zero current compiler errors. Warnings are non-blocking
unless introduced by the change or indicative of a runtime defect.

Use the typed Unity CLI commands for live checks:

```bash
unity command --project-path client/Unity editor_status --format json
unity command --project-path client/Unity list_open_scenes --format json
unity command --project-path client/Unity get_scene_hierarchy --format json
unity command --project-path client/Unity editor_play --format json
unity command --project-path client/Unity editor_stop --format json
```

Use `eval` or `eval_file` for targeted live C# probes. Do not replace source edits with
one command per property, and avoid destructive probes unless the task explicitly requires
them.

## Character package workflow

Inspect and cook through the typed Pipeline commands:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target fightguy --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target fightguy --format json
```

Inspect is read-only. Cook is the persistence boundary. A failed cook must not replace the
last valid cooked package, generated assets, or persisted cook status.

## Prohibited stale workflow

Do not use or recreate:

- `scripts/mcp-*.sh`
- `scripts/mcp-unwrap.py`
- `gamedev-mcp-server`
- `localhost:26356/mcp`
- `xd://mcp__unity_mcp_*`
- the removed IvanMurzak MCP package or MCP-only extensions

If the Unity CLI or Pipeline endpoint is unavailable, report that exact failure. Do not
fall back to MCP or claim Unity verification from a shell-only command.
