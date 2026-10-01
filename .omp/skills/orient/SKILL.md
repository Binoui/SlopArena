---
name: orient
description: Minimal read-only SlopArena session orientation: current demo goal, working-tree status, and relevant next action. Load technical references only for the current task.
---

# orient — bounded session orientation

Run at the start of a session. Keep the read set bounded to the current work.

## Execution root

1. If the task/handoff supplies a game checkout or worktree, resolve that path first. Do not replace an invalid explicit target with the main checkout.
2. Otherwise inspect the current directory's Git root. Use it only if it contains both `src/Shared/SlopArena.Shared.csproj` and `tests/Shared.Tests/Shared.Tests.csproj`. From the workspace, resolve its `SlopArena/` link instead; the workspace's Git root is not the game.
3. Canonicalize the selected checkout with `readlink -f` and require both project files before any game command. If no valid target exists, report the unresolved path and stop.
4. Set the shell tool's `cwd` to that game root for all commands below, and prefix file-tool paths with it. Do not change BMAD's separately supplied planning root.

For MasterServer or website work, orient in that repository using its own instructions; do not apply game commands there.

## Default inputs

1. Read `docs/plans/2026-09-05-playable-demo-reset.md` product target and execution-order
   sections.
2. Run `git status --short --branch` and `git log --oneline -5`.
3. Read relevant technical references, ADRs, plans, and `TESTING-UNITY.md` only when the
   actual task needs them.

## Output

Report at most ten lines covering the current goal, uncommitted work, next action, and
known blocker. Do not run builds or perform branch/PR inventory.

Historical plans remain records, not current checklists; preserve their status labels and
follow the reset plan for current product work.
