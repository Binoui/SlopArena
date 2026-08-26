# Unity CLI

## Current decision

SlopArena now uses the Unity CLI with `com.unity.pipeline`. The IvanMurzak Unity MCP
package and its extensions were removed because their Roslyn assemblies conflicted with
Pipeline's live C# evaluation.

Verified on 2026-08-26 with:

- Unity CLI `1.0.0-beta.6`
- Unity Editor `6000.0.78f1`
- `com.unity.pipeline` `0.5.0-exp.1`

Official documentation:

- <https://docs.unity.com/en-us/unity-cli/use-unity-cli>
- <https://docs.unity.com/en-us/unity-production-pipeline/local-tools-cli/unity-pipeline-package>

## Standalone CLI

The binary is installed at `~/.local/bin/unity`.

```bash
unity --version
unity upgrade
unity auth status --format json
unity editors --format json
unity open client/Unity
unity doctor
```

Use `--format json` for automation. Results are written to stdout, errors to stderr, and
command failure produces a nonzero exit code.

## Pipeline connection

Pipeline runs a localhost HTTP server inside the running Editor. The CLI discovers it
through `Library/Pipeline/.unity-pipeline-port`.

```bash
unity pipeline list --format json
unity command --project-path client/Unity --detail compact --format json
```

The current project exposes 142 self-describing commands, including scene and GameObject
inspection, serialized fields, assets, prefabs, animation, tests, builds, console logs,
screenshots, Play mode control, package management, and live C# evaluation.

## Live Editor commands

```bash
unity command --project-path client/Unity editor_status --format json
unity command --project-path client/Unity list_open_scenes --format json
unity command --project-path client/Unity get_scene_hierarchy --format json
unity command --project-path client/Unity \
  get_console_logs --severity error --limit 20 --format json
unity command --project-path client/Unity recompile --format json
unity command --project-path client/Unity recompile_status --format json
unity command --project-path client/Unity editor_play --format json
unity command --project-path client/Unity editor_stop --format json
```

## Live C# evaluation

`eval` runs C# inside the live Editor on its main thread without a project-level recompile:

```bash
unity command --project-path client/Unity eval \
  'return new { unity = UnityEngine.Application.unityVersion, playing = UnityEditor.EditorApplication.isPlaying };' \
  --format json
```

Verified result:

```json
{"unity":"6000.0.78f1","playing":false}
```

For larger probes, use `eval_file` with a source file. Treat `eval` as powerful and
local-only; avoid destructive commands unless the task explicitly requires them.

## Migration notes

Removed from the active project:

- `com.ivanmurzak.unity.mcp`
- IvanMurzak ProBuilder, Animation, InputSystem, Navigation, and ParticleSystem extensions
- MCP-only Roslyn, ReflectorNet, McpPlugin, and R3 binaries
- `scripts/mcp-*.sh`, `scripts/mcp-unwrap.py`, and `.omp/mcp.json`
- The `com.ivanmurzak` OpenUPM scope

Application dependencies remain, including SignalR and `System.Text.Json` used by the
client lobby code.

## Verification gate

After Unity-facing changes:

1. Confirm `unity pipeline list --format json` reports `isReachable: true`.
2. Run `unity command ... recompile` and poll `recompile_status`.
3. Read `get_console_logs --severity error` and require zero current errors.
4. Use `eval` for targeted live state checks.
5. Use `editor_play` / `editor_stop` for lifecycle checks.

The standalone CLI and Pipeline package are experimental. If a future Pipeline update
reintroduces dependency conflicts, keep the project on the last verified version rather
than restoring the removed MCP integration.
