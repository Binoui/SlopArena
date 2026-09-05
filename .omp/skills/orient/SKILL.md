---
name: orient
description: Minimal read-only SlopArena session orientation: current demo goal, working-tree status, and relevant next action. Load technical references only for the current task.
---

# orient — bounded session orientation

Run at the start of a session. Keep the read set bounded to the current work.

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
