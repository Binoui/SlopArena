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
unity command --project-path client/Unity --query sloparena --detail compact --format json
```

The project exposes self-describing commands. Discover the relevant command family
with `--query` or `--tag`; use `--detail full` only when its argument schema is needed.



## Ability Lab agent commands

For gameplay scenarios, run and inspect the native Shared two-fighter scenario
first. Do not generate temporary C# or use `eval` to create opponents or arrange
gameplay. `eval` is reserved for novel diagnostics not covered by a typed command.

```bash
unity command --project-path client/Unity \
  sloparena.lab.open --target fightguy --format json
# Normal hit
unity command --project-path client/Unity \
  sloparena.lab.run --action ground.1 --ticks 60 --distance 1.2 \
  --opponent idle --format json
# Block, miss, shared grab capture/throw
unity command --project-path client/Unity \
  sloparena.lab.run --action ground.1 --ticks 60 --distance 1.2 \
  --opponent shield --format json
unity command --project-path client/Unity \
  sloparena.lab.run --action ground.1 --ticks 60 --distance 12 \
  --opponent idle --format json
unity command --project-path client/Unity \
  sloparena.lab.run --action grab --ticks 60 --distance 0.7 \
  --opponent idle --format json
unity command --project-path client/Unity \
  sloparena.lab.inspect --format json
```

The Ability Lab UI shows both fighters and scenario controls. The `run`
contract is `--action <canonical-id|grab> --ticks <last-frame>` (0–3600),
`--distance <metres>`, `--opponent idle|shield`, `--damage 0..999`, and
`--facing <relative-degrees>`. Defaults are 60, 2.5, idle, 0, and 180.
Outcomes are genuine Shared results: success may mean hit, blocked contact,
miss, grab whiff, or paired capture/release, not guaranteed success.

Use `preview --action <id|grab> --tick <frame>` to seek recorded scenario
frames; frame 0 is the first input at MatchTick 1. `capture --action` uses the
matching recorded run and its options, not replacement defaults.
Without a matching run, `grab` preview/capture creates a default grab scenario;
it does not inherit another action's distance, opponent, damage, facing or horizon.
Canonical actions without a matching run retain ordinary authoring preview.
For example, run a grab with the desired distance/opponent/horizon, then:

```bash
unity command --project-path client/Unity \
  sloparena.lab.preview --action grab --tick 7 --format json
unity command --project-path client/Unity \
  sloparena.lab.capture --action grab --ticks 7,19 \
  --output .ability-lab-cache/fightguy/grab --format json
```

This scenario frame numbering differs from canonical authoring timeline
preview: authoring cumulative ticks include the requested duration endpoint,
which applies the final authored stage tick (`durationTicks - 1`).

Check semantic `data.result.success` and diagnostics, then inspect
`data.result.scenario`: `frames[]` includes both fighters' state, movement
velocity, knockback velocity, facingYaw (radians), damage, hitstop/hitstun/
blockStun, and interaction phase/IDs/timing. `contacts[]` are accepted Shared
hit/block records with damage, blocked, force, knockback, stun/hitstop and
position. Grab capture/release are `interactions[]`, not fabricated hits;
`presentationEvents[]` and `deaths[]` are observed outcomes. Distinguish a
valid miss from command failure. `inspect` reports current selection, preview,
workspace, scenario, and diagnostics.

`open`, `run`, `preview`, and `capture` are Edit Mode only. Structured
diagnostics cover unsupported mode, missing package/rig, invalid/unavailable
action/draft/options/frame and capture dimension/path/tick/existing-file
errors. Capture rejects unsafe paths/symlinks and removes partial PNGs on
failure; it restores prior scenario/cursor/playback/visibility/camera/render
target. Capture accepts up to 64 distinct ticks, 64–4096 pixel dimensions,
and writes only below repository-relative `.ability-lab-cache/`; no overwrite.

The workspace prepares current source in memory using existing compilation,
verified poses, catalog and rig without saving, cooking, changing Undo history,
or writing source/cooked files. Persisted authoritative preview still requires
valid cooked runtime/pose/catalog/rig prerequisites; missing/invalid pose or rig
blocks scenarios. A clean prepared source may report `dirty: false` and
`authoritativePreview: false`. Dirty status is workspace-edit state, not a hit
verdict. Isolated scenarios do not establish online behavior or game feel.

For ordinary authoring preview, `open --target` accepts a package ID or
project-relative root below `Assets/CharacterPackages`; preview/capture actions
use canonical IDs (`ground.1`, `air.R`, etc.). Reopening the same target
preserves a dirty draft; switching away is rejected. `inspect` is read-only.
Capture's `captures[]` gives action, requested tick, PNG path, and either
scenario frame/MatchTick or authoring applied stage/local/cumulative tick.
Root result is the restored workspace state. See the
[Ability Lab guide](../systems/ability-lab.md) for the complete contract.

Pipeline wraps typed results under `data.result`; outer command success is
transport status. For automation, require semantic success explicitly:

```bash
unity command --project-path client/Unity \
  sloparena.lab.run --action ground.1 --distance 1.2 --opponent idle \
  --format json | jq -e '.data.result.success'
```

Discover this family narrowly:

```bash
unity command --project-path client/Unity \
  --query sloparena.lab --detail full --format json
```

Routing: use native Pipeline commands for generic Unity tasks and
`sloparena.lab.*` for these domain workflows. Avoid `eval` where a typed
command covers the operation; never invent aliases or obsolete `--slot`
arguments.


## Skill sources

- SlopArena operations use repository `.omp/skills/`, `docs/testing.md`, and this
  document's installed Unity CLI/Pipeline commands.
- Project skills require explicit `name` and nonempty `description` frontmatter.
- `.agents/skills/unity-skills` is an ignored local installation; `.claude/skills/unity-skills`
  points to it. The operational umbrella is excluded for this project because it requires
  a conflicting REST route. Generic advisory skills remain available.
- Do not modify `~/.omp/agent/skills`, ignored skill installations, or `skills-lock.json`
  to repair a project override. Discovery is refreshed by a new OMP session, not by
  re-reading a stale skill URI in the old session.
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

## Agent-facing character authoring

Character source files are the agent-facing authoring representation. SlopArena CLI
commands validate, cook, inspect, and operate on Unity-owned concerns; they are not
intended to replace source editing with a command per property.

Character inspection and cooking both accept a package ID or a project-relative
package root under `Assets/CharacterPackages`:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target fightguy --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target fightguy --format json
```

The authoring boundary also exposes read-only planning and typed catalog operations:

```bash
unity command --project-path client/Unity \
  sloparena.character.cook --target bonk --dry-run --format json
unity command --project-path client/Unity \
  sloparena.character.verify --target bonk --format json
unity command --project-path client/Unity \
  sloparena.character.bind --target bonk --semantic-id anim.run \
  --asset-path Assets/CharacterPackages/bonk/Animations/bonk_run.anim --format json
unity command --project-path client/Unity \
  sloparena.character.unbind --target bonk --semantic-id anim.run --format json
unity command --project-path client/Unity \
  sloparena.character.roster.refresh --package-id bonk --format json
unity command --project-path client/Unity \
  sloparena.character.assets --target bonk --semantic-id anim.run --format json
```

`cook --dry-run` returns the predicted hashes and output paths without writing
canonical cooked artifacts, generated catalogs, or cook status. Roster admission is
explicit through `sloparena.character.roster.admit` and requires a passing verification.
After an admitted package is recooked, `sloparena.character.roster.refresh` explicitly
repins its existing roster entry to the newly verified version and hashes. It does not
admit a new package or change the selector.

Pipeline wraps the typed command result under `data.result`. Inspect returns the
canonical 16-slot summary, source/cooked hashes, status, stale reasons, and existing
compiler/cooker diagnostics. Cook returns source, cooked-content, and package hashes
when successful. Semantic cook failures return `data.result.success: false` with
structured diagnostics; the outer Pipeline transport command remains successful.
Shell automation can propagate that semantic result explicitly:

```bash
unity command --project-path client/Unity \
  sloparena.character.cook --target fightguy --format json \
  | jq -e '.data.result.success'
```

Choose the verification mode from [`docs/testing.md`](../testing.md):

- **Local iteration:** edit source or assets, exercise the affected Ability Lab or
  Training path, and do not cook a publishing package for every tuning change.
- **Accepted or distributable content:** inspect, cook, verify, and refresh the
  admitted roster entry when the change is intended to ship.

The commands above remain the concrete Unity CLI reference. A failed cook does not
replace the last valid cooked package, generated assets, or persisted cook status.

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

Use `eval_file` only when a genuinely novel probe needs more code than an inline
expression. First check the relevant native and SlopArena command schemas. Keep
task-specific assertions temporary; promote repeated setup/control operations into
the existing typed command seam rather than rewriting them in each session.

## Migration notes

Removed from the active project:

- `com.ivanmurzak.unity.mcp`
- IvanMurzak ProBuilder, Animation, InputSystem, Navigation, and ParticleSystem extensions
- MCP-only Roslyn, ReflectorNet, McpPlugin, and R3 binaries
- `scripts/mcp-*.sh`, `scripts/mcp-unwrap.py`, and `.omp/mcp.json`
- The `com.ivanmurzak` OpenUPM scope

Application dependencies remain, including SignalR and `System.Text.Json` used by the
client lobby code.

## Verification modes

Choose the applicable mode in [`docs/testing.md`](../testing.md):

- **Local iteration:** use the existing Editor development content and exercise
  the affected Ability Lab or Training path. Recompile when code or the Shared
  plugin requires it; do not force a recompile for prose or every numeric JSON edit.
- **Integrated change:** for Unity-facing behavior, confirm Pipeline reachability,
  recompile when required, read current error logs, and exercise the affected runtime.
- **Distributable demo:** inspect and cook accepted packages, refresh admitted roster
  pins, and verify the packaged client/server path.

The standalone CLI and Pipeline package are experimental. If a future Pipeline update
reintroduces dependency conflicts, keep the project on the last verified version rather
than restoring the removed MCP integration.
