---
name: code-review
description: "Review SlopArena changes against project standards and the approved specification."
---

Run a two-axis review: Standards and Spec. Use independent reviewers when delegation is requested or when the review workflow supports it; keep their findings separate. The project bindings below override generic review text.

## Fixed snapshot

Review the requested fixed commit/tree or captured diff. Inspect the matching source snapshot as well, including intentional unstaged changes. Confirm the snapshot and spec before reviewing. If the fixed point or spec is unavailable, state the exact missing input instead of inventing one.

Review Shared authority, package/catalog admission, deterministic simulation, telemetry provenance, and Unity presentation boundaries. Treat authoritative legality, event provenance, and collision geometry as load-bearing seams. Load relevant repository guidance only; do not apply unrelated standards or redesign selected UI/content.

## Required gates

1. Implementation complete: requested behavior and all named acceptance criteria are present.
2. Automated checks passed: report exact commands, result, known baseline failures, and artifacts.
3. Human acceptance complete: report the requested Unity/solo/training checks separately. Compile, spectator, and headless output do not substitute for human gameplay acceptance.

For every matrix row record: requested identity, resolved identity, difficulty/tier, seed, exact command, exit status, and artifact/output path. Exit 0 is insufficient when identity or tier mismatches; reject the row and report it. A failed skill launcher is recoverable: retry with the documented fallback, disclose the failure, and do not silently count it as evidence.

## Review constraints

Do not stage, commit, amend, or push during review. The repository rule requiring explicit user authorization for commits/pushes overrides generic skill instructions. Do not require an artificial “no findings” result; report concrete findings, uncertainty, and remaining gates. Changes made after review require a delta review against the captured snapshot.

User-selected frontend design choices remain binding; do not autonomously invoke design or polish guidance. Do not claim skill-load quotas, reviewer counts, or other workflow metrics that were not observed.

Report Standards and Spec separately, with file/line evidence, severity, and a concrete fix where needed. End with remaining implementation, automated-verification, and human-acceptance status.
