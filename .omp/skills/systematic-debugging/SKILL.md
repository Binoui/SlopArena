---
name: systematic-debugging
description: Use when the user says "diagnose"/"debug this", or reports something broken, throwing, failing, or slow. Use before proposing fixes.
---

# Diagnosing Bugs

A discipline for hard bugs. Skip phases only when explicitly justified.

When exploring the codebase, read `docs/architecture-overview.md` and any relevant `docs/systems/` files first to build a clear mental model of the module.

## Phase 1 — Build a feedback loop

**This is the skill.** Everything else is mechanical. If you have a **tight** pass/fail signal for the bug — one that goes red on *this* bug — you will find the cause. If you don't, no amount of reading code will save you.

Spend disproportionate effort here. Be aggressive. Be creative. Refuse to give up.

### Ways to construct one — try in order

1. **Failing test** at whatever seam reaches the bug.
2. **CLI invocation** with a fixture input, diffing output against known-good.
3. **Throwaway harness.** Minimal subset of the system that exercises the bug path with a single call.
4. **Replay a captured trace.** Save a real packet / input / event log to disk; replay in isolation.
5. **Property / fuzz loop.** If the bug is "sometimes wrong output", run many inputs and look for failure.
6. **Bisection.** If the bug appeared between two known states, automate "boot at state X, check, repeat".

Build the right feedback loop, and the bug is 90% fixed.

### Tighten the loop

Once you have *a* loop, tighten it:
- Can I make it faster? (Cache setup, narrow scope)
- Can I make the signal sharper? (Assert on the specific symptom)
- Can I make it deterministic? (Pin time, seed RNG, freeze network)

### Completion criterion — a tight loop that goes red

Phase 1 is done when you can name **one command** you have **already run at least once** that is:

- [ ] **Red-capable** — drives the actual bug path and asserts the user's exact symptom
- [ ] **Deterministic** — same verdict every run
- [ ] **Fast** — seconds, not minutes
- [ ] **Agent-runnable** — no human in the loop

If you catch yourself reading code to build a theory before this command exists, **stop**. No red-capable command, no Phase 2.

## Phase 2 — Reproduce + minimise

Run the loop. Watch it go red.

Confirm:
- [ ] The loop produces the failure the user described — not a nearby failure
- [ ] Reproducible across multiple runs
- [ ] Exact symptom captured (error message, wrong output, timing)

Then shrink the repro to the **smallest scenario that still goes red**. Cut inputs, callers, config one at a time. Every remaining element must be load-bearing.

## Phase 3 — Hypothesise

Generate **3–5 ranked hypotheses** before testing any of them. Each must be falsifiable:

> "If X is the cause, then changing Y will make the bug disappear / changing Z will make it worse."

Show the ranked list before testing. The user may have domain knowledge that re-ranks instantly. Don't block on it — proceed with your ranking if they're AFK.

## Phase 4 — Instrument

Each probe maps to a specific prediction from Phase 3. **Change one variable at a time.**

1. Debugger / REPL inspection if the env supports it. One breakpoint beats ten logs.
2. Targeted logs at the boundaries that distinguish hypotheses.
3. Never "log everything and grep".

Tag every debug log with a unique prefix, e.g. `[DBG-a4f2]`. Cleanup is then a single grep.

For performance regressions: establish a baseline measurement, then bisect. Measure first, fix second.

## Phase 5 — Fix + regression test

Write the regression test **before the fix** — but only if there is a correct seam for it.

A correct seam is one where the test exercises the real bug pattern as it occurs at the call site. If no correct seam exists, note it — that itself is a finding about the architecture.

If a correct seam exists:
1. Turn the minimised repro into a failing test
2. Watch it fail
3. Apply the fix
4. Watch it pass
5. Re-run the Phase 1 loop against the original scenario

## Phase 6 — Cleanup + post-mortem

Before declaring done:
- [ ] Original repro no longer reproduces
- [ ] Regression test passes (or absence of seam is documented)
- [ ] All `[DBG-...]` instrumentation removed
- [ ] Throwaway prototypes deleted
- [ ] The correct hypothesis is stated in the commit message

Then ask: what would have prevented this bug? If the answer involves architecture (no good test seam, tangled callers, hidden coupling) — note it explicitly.
