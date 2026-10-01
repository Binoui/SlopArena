---
name: implement
description: "Implement SlopArena work from an approved spec or ticket."
disable-model-invocation: true
---

Implement the approved work. The project rules below override generic workflow text.

## Plan and scope

Read the approved spec and relevant repository guidance before editing. Keep the bounded plan visible: affected files, authoritative seam, verification commands, and risks. Architecture changes require the repository's approval gate before implementation. Preserve unrelated working-tree changes.

Resolve execution context before editing or dispatching: absolute BMAD planning root, canonical target checkout/worktree, and absolute spec/ticket path. An explicit target wins; never substitute main for an invalid worktree. Load the target repository's instructions and run its commands, baseline/diff queries, and verification with that checkout as `cwd`. BMAD configuration and planning artifacts remain at the supplied planning root, even when the worker starts outside it.

Every implementation handoff must include those concrete paths. For Unity-facing work, also name the main Editor checkout and integration owner; a worktree worker must not operate that Editor. A workspace worktree does not isolate its linked repositories. For a multi-repository task, assign each slice an explicit target rather than letting workers infer one from the launch directory.

Map repository-prefixed spec/context paths into the supplied checkout/worktree, not the workspace's main-repository symlink. Resolve workspace-owned planning/configuration paths against the planning root.

Trace gameplay changes end to end: input → package/catalog → Shared simulation → state/event → Unity presentation. Identify the authoritative legality, event, and geometry seam before changing callers. Reuse existing Shared, catalog, collision, and telemetry infrastructure. Load only the relevant combat/entity/netcode/stage guidance for the task.

## Implement and verify

Prefer focused behavioral coverage at the changed seam. Run typechecks/builds and focused tests during implementation, then the applicable full suite once. Follow `docs/testing.md`, Unity CLI rules in `docs/contributing/unity-cli.md`, and the repository's main-checkout Unity restriction. Human gameplay acceptance is separate from compile, headless, and spectator evidence; record it as pending when unavailable.

Do not hand-edit cooked runtime artifacts when authored package input is the source. Do not add client-only gameplay corrections. Numeric values explicitly requested by the user are binding unless they create a correctness issue.

## Review and delivery

Review the frozen commit/tree or captured diff and the matching source snapshot, including intentional unstaged changes. Never stage, commit, or amend the review snapshot before both requested reviewers finish. Any change after review receives a delta review. Do not demand premature “no findings” output; report findings with evidence and severity.

Commit and push are separate operations and require explicit user authorization. The generic implementation skill's automatic commit instruction does not apply here. Do not commit or push by default.
