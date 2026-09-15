---
name: implement
description: "Implement SlopArena work from an approved spec or ticket."
disable-model-invocation: true
---

Implement the approved work. The project rules below override generic workflow text.

## Plan and scope

Read the approved spec and relevant repository guidance before editing. Keep the bounded plan visible: affected files, authoritative seam, verification commands, and risks. Architecture changes require the repository's approval gate before implementation. Preserve unrelated working-tree changes.

Trace gameplay changes end to end: input → package/catalog → Shared simulation → state/event → Unity presentation. Identify the authoritative legality, event, and geometry seam before changing callers. Reuse existing Shared, catalog, collision, and telemetry infrastructure. Load only the relevant combat/entity/netcode/stage guidance for the task.

## Implement and verify

Prefer focused behavioral coverage at the changed seam. Run typechecks/builds and focused tests during implementation, then the applicable full suite once. Follow `docs/testing.md`, Unity CLI rules in `docs/contributing/unity-cli.md`, and the repository's main-checkout Unity restriction. Human gameplay acceptance is separate from compile, headless, and spectator evidence; record it as pending when unavailable.

Do not hand-edit cooked runtime artifacts when authored package input is the source. Do not add client-only gameplay corrections. Numeric values explicitly requested by the user are binding unless they create a correctness issue.

## Review and delivery

Review the frozen commit/tree or captured diff and the matching source snapshot, including intentional unstaged changes. Never stage, commit, or amend the review snapshot before both requested reviewers finish. Any change after review receives a delta review. Do not demand premature “no findings” output; report findings with evidence and severity.

Commit and push are separate operations and require explicit user authorization. The generic implementation skill's automatic commit instruction does not apply here. Do not commit or push by default.
