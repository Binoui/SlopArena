---
name: orient
description: Session-start context restore for SlopArena — read the canonical documentation index, current roadmap(s), active plans, ADRs, recent git log/status, and TESTING-UNITY.md, then print where we are, what's in-flight, next step, and open risk in under 30 lines. Read-only. Use at the start of every session before any work.
---

# orient — session-start context restore

Run at the START of every session, before any work. Kills the "where are we" tax.

## Behavior
1. Read `docs/README.md` first. Treat its Plans table as the canonical roadmap index; do not assume `docs/roadmap.md` exists.
2. Read every roadmap listed there, starting with the one marked current. Also inspect other plans with an explicit `Status: Active` or `Active execution roadmap` header when they cover the current work. Do not select by filesystem mtime alone.
3. Skip plans marked `Superseded`, `Executed`, `Archived`, or historical unless needed only to understand a replacement chain. Never present one of those as the current next step.
4. Read the newest accepted ADRs relevant to the current roadmap. ADRs constrain the work; they are not substitutes for an active execution roadmap.
5. Read `git log --oneline -10` and `git status --short` to find the in-flight thread.
6. Read `TESTING-UNITY.md` if it exists (handoff checklist).
7. Print, in under 30 lines:
   - **Sources** — the roadmap/active plans and ADRs actually read; mention missing expected files instead of failing silently.
   - **Where we are** — 1-2 sentences from the current roadmap, active plans, and commits.
   - **What's in-flight** — uncommitted work: files + intent.
   - **Next step** — the single next action from the current roadmap, not from a superseded plan.
   - **Open risk** — anything the current roadmap or latest relevant plan flags as blocking.
8. Do NOT edit anything. This is read-only orientation.
