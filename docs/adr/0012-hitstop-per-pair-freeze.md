# ADR-0012: Hitstop — Per-Pair Freeze on Hit

**Status:** Proposed — 2026-08-07
**Deciders:** @Binoui

## Context

Hits currently launch instantly — `ApplyKnockback` runs the same tick the hitbox connects, so there is no beat between "hit lands" and "flight begins." The frontloaded knockback feel (exponential decay, `Simulation.cs:35`) wants a readable beat: hit → freeze → launch. Hitstop also creates the decision window — the defender picks their Combo Influence direction (ADR-0013) and whether to Burst (ADR-0014) while both characters are frozen.

## Decision

**Every hit that connects freezes attacker and victim for a short, damage-scaled duration; knockback launches when the freeze ends.**

- **Per-pair, not global.** Only the two entities in the hit freeze; everyone else keeps moving. A frozen victim is an open target to third parties in 2–4 player matches — deliberate, it is a team/FFA lever (can a teammate interrupt my string?).
- **Projectile/zone hits freeze the receiver only** — the thrower is not frozen (they may be across the map; freezing them would read as wrong).
- **Both freeze symmetrically** — the attacker's ability ticks pause too, so hitstop extends the victim's lock *and* the attacker's recovery equally. No free combo extension.
- **Formula (start values, tune from playtest):** `freeze = 2 + 2·damage` ticks, capped 24. Hits under 3 damage get ×2 so tickle hits still pop. 4-dmg jab → 10 ticks (0.17 s), 14-dmg finisher → 24 (0.4 s).
- **Multihit discount:** hits beyond the first within the same ability get 50% freeze — without it, multihits become stunlock soup.
- **Match clock is NOT paused** — hitstop is a state, not a pause, or stalling becomes a tactic.
- **Combo Influence capture extends across freeze + hitstun** (DIX/DIY fields, ADR-0013).
- **Burst is pressable during the freeze** (ADR-0014) — the freeze is the escape decision window.

Implementation shape: per-entity `HitstopTicks` in `CharacterState`; `SimulateTick` gates at the top (skip state machine + movement, decrement); hit resolution sets both sides on connect; `ApplyKnockback` deferred to freeze expiry.

## Considered Options

- **No hitstop (status quo)** — rejected: no decision beat, the DKO feel is missing, and Burst has no natural decision window.
- **Global pause** — rejected: stalls players not in the hit, opens stalling, muddies the match clock.

## Consequences

- `HitstopTicks` must ride the state packet for prediction → wire format change + codec tests.
- All golden snapshots shift (positions land later and shorter) — regenerate with inspection.
- Tick-exact tests retune.
- Multihit discount must be designed in from day one.
