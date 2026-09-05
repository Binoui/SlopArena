# CLAUDE.md — SlopArena Project

Use the canonical project guidance before acting:

- `.omp/AGENTS.md` defines architecture, authority, content boundaries, and project context.
- `.omp/RULES.md` defines operating restrictions and the approval boundary.
- [`docs/plans/2026-09-05-playable-demo-reset.md`](docs/plans/2026-09-05-playable-demo-reset.md)
  is the current product goal: a playable four-character friends demo and first remote feedback.
- [`docs/testing.md`](docs/testing.md) owns local iteration, integrated change, and
  distributable demo verification.

Preserve Shared/server authority and deterministic equivalence. Do not implement gameplay
mechanics as client-only rules. Do not commit, push, or install anything without explicit
user permission.

## Agent skills

### Issue tracker

Issues live in GitHub Issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical labels: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout — one CONTEXT.md + docs/adr/ at the repo root. See `docs/agents/domain.md`.
