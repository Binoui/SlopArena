---
name: sloparena-ability-lab
description: "Use when an agent needs native-first Ability Lab runs or scenarios against idle/shield opponents, Shared hit/block/miss/grab outcomes, recorded-frame preview or capture, or workspace diagnostics."
category: game-dev
---
Before gateway examples, set `ORCA_TERMINAL_HANDLE` to your own runtime-issued terminal handle; the gateway verifies its incarnation and fails closed if it is missing or mismatched. Use the canonical gateway in [`docs/contributing/unity-cli.md`](../../../docs/contributing/unity-cli.md) and the [shared Editor coordination protocol](file:///home/binoui/Documents/projects/sloparena-workspace/docs/unity-editor-coordination.md). Wait for an independent lease; never inject work into an owner's batch. Runtime ownership status replaces historical Markdown ownership; `blocked` or unknown is not free.

# SlopArena Ability Lab

Use native typed Pipeline commands for workspace inspection and real Shared
two-fighter runs. Do not use `eval` or temporary C# to construct gameplay.
Lab capture renders only Ability Lab preview/scenario frames; it is not the runtime
game-view screenshot route. For runtime menus, settings, or HUD, use
`sloparena.capture.game-view --source screen` and the live
`sloparena.ui.status`/navigation/viewport commands instead (see the Unity CLI
reference, *Screenshots and visual evidence*).

## Steps

1. Observe gateway status. If `held` or `releasing`, invoke the next gateway command and let it wait for an independent lease; if `blocked` or unknown, stop. `free`, settled, and zero active operations are availability observations only. Then discover the focused command family:
   ```bash
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts status --project-path /home/binoui/Documents/projects/SlopArena/client/Unity
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- --query sloparena.lab --detail full
   ```
2. Open the package and run an actual scenario. Choose distance/opponent
   deliberately; `run` does not force a hit:
   ```bash
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.open --target fightguy
# Hit / block / miss / shared grab
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.run --action ground.1 --ticks 60 --distance 1.2 --opponent idle
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.run --action ground.1 --ticks 60 --distance 1.2 --opponent shield
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.run --action ground.1 --ticks 60 --distance 12 --opponent idle
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.run --action grab --ticks 60 --distance 0.7 --opponent idle
   ```
3. Check `.data.result.success` and diagnostics, then assess observations:
   `scenario.frames[]` contains both fighters' state, movement/knockback
   velocity, facingYaw (radians), damage, hitstop/hitstun/blockStun and
   interaction phase/timing. `contacts[]` contains accepted Shared contacts;
   zero-damage blocked contact is still a contact. Grab capture/release are
   in `interactions[]`, not fabricated hit results. Also inspect
   `presentationEvents[]` and `deaths[]`. A successful run can legitimately
   have no contacts; judge semantic outcomes, not just command success.
4. `preview --action <id|grab> --tick <frame>` seeks the same action's recorded
   run. `capture --action <id|grab> --ticks <frames>` captures that matching
   recorded run/options; first run the desired scenario. Without a matching
   run, grab uses default scenario options, never another action's options;
   canonical actions retain ordinary authoring preview. Example:
   ```bash
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.preview --action grab --tick 7
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.capture --action grab --ticks 7,19 --output .ability-lab-cache/fightguy/grab
   ```
5. Use `inspect` for current workspace/run state (the gateway already forces JSON). Capture writes
   PNGs only under `.ability-lab-cache/`, rejects existing files and unsafe
   paths, cleans partial outputs on failure, and restores prior cursor,
   playback, visibility, camera and render target. Mutating commands require
   Edit Mode and report structured failures for invalid mode, missing/invalid
   preview/action/options/frame, or capture input/path issues. Require semantic
   result success; inspect diagnostics.

`--ticks` on `run` is the last frame (0–3600); defaults are last frame 60,
distance 2.5 m, idle opponent, damage 0, facing 180°. `--opponent` is `idle`
or `shield`; damage range is 0–999. UI displays both fighters and scenario
controls.

The workspace prepares current source in memory using existing compiler,
verified poses, catalog and rig; it does not save/cook, alter Undo history,
or write source/cooked data. Persisted authoritative preview still requires
valid cooked runtime/pose/catalog/rig; missing/invalid pose or rig blocks
scenarios. `dirty` reports workspace edits and may be false while
`authoritativePreview` is false. Never imply isolated scenarios prove online
play or game feel. For persistence and accepted content, use
[Character Workflow](../sloparena-character-workflow/SKILL.md). Full contracts
and CLI routing: [Unity CLI reference](../../../docs/contributing/unity-cli.md)
and [Ability Lab guide](../../../docs/systems/ability-lab.md).
