# ADR-0013: Combo Influence — Additive Launch Drift

**Status:** Superseded — 2026-08-13 by [ADR-0019 §4](0019-melee-based-hit-response.md) (DI): Combo Influence is dropped. The Melee angle rotation at hitstop exit (up to `18°·sin²` toward the committed 8-way input, magnitude preserved) replaces the additive drift — both would stack to double-strength. `LaunchMagnitude` removed (server-local, never on the wire). The `DIX/DIY` capture fields survive (ADR-0019 §4 reuses them).
**Deciders:** @Binoui

> Note (ADR-0015): this decision's context cited warp + tracking erasing the drift. Warp is removed — nothing erases Combo Influence now; it becomes the primary combo-escape tool. Decision itself unchanged.
**Deciders:** @Binoui

## Context

The trajectory-influence mechanism already exists — `DIX/DIY` captured during hitstun, `+3.5 m/s` flat added to remaining knockback at expiry (`Simulation.cs:506`) — but it has no actual use. +3.5 is a fraction of launch speeds (8–38 m/s), is applied once against a remnant that is already decaying (λ = 1.8/s), is horizontal-only, and chase abilities (warp + 0.8–0.9 tracking) erase any shift it does produce. It is a formality. We need a defender tool that works in 3D and actually moves the drift.

## Decision

**Keep the additive (Smash-4 vectoring) model — it is the 3D-native one — but scale it to the launch instead of a flat constant.**

- **Direction captured during Hitstop + Hitstun** (existing `DIX/DIY`; Hitstop window added by ADR-0012).
- **Applied once at lock expiry to remaining horizontal knockback, scaled as a fraction of the original launch magnitude.** Start at ~0.30 (30%), tune from playtest.
- **Horizontal only (X/Z)** — drift within the arena plane; vertical is untouched so kill angles keep their shape.
- **Why one-shot works with exponential decay:** the influence is applied at expiry, early in the visible flight, while the remnant is still fast — most of the shift lands where it matters. Decay then kills the tail as usual.

## Considered Options

- **Rotational DI (Melee/Rivals)** — redirect the launch angle toward the stick. Rejected: needs relative-angle math in 3D; the perpendicular mental model is what makes DI feel unusable outside 2D.
- **SDI mashing** — position micro-shifts per input during hitlag. Rejected: mashing-driven, not a deliberate read.
- **Drift DI (continuous additive during flight, Rivals 2)** — same fields, applied per-tick instead of once. Noted as fallback if one-shot underdelivers; small change, not a redesign.

## Consequences

- `DIStrength` becomes a launch-relative multiplier, not a flat constant — one tuning axis with a clear effect (combo escape = shift enough that tracking can't follow; survival = drift toward stage).
- Canonical term is **Combo Influence** (see CONTEXT.md); code fields stay `DIX/DIY` for wire stability.
- If one-shot influence proves too weak in playtest, drift application is a constant-behavior swap, not a redesign.
