# FightGuy — self-play telemetry
Generated 2026-08-18 08:06Z · 20 seeded bot-vs-bot matches on the real ServerSimulation (issue #148). 
Seed 42 · avg 179.6s, max 180.0s · wins 0–1, draws 19.

- **Hit rate** 51.7% (2035/3933 swings) — **whiff rate** 48.3% (1898/3933).
- **Combos**: avg length 2.17, max 4 (gap ≤ 1.5 s between same-pair hits).
- **Damage**: 250 per match, 42 per stock (6 stocks/match).

| move | swings | hits | whiffs | hit% |
|---|---|---|---|---|
| a1 Double Punch | 416 | 3 | 413 | 1% |
| a2 Floating Kick | 377 | 91 | 286 | 24% |
| a3 High Kick | 110 | 0 | 110 | 0% |
| a4 Air Tornado | 379 | 1 | 378 | 0% |
| g1 Low Kick | 441 | 431 | 10 | 98% |
| g2 Roundhouse | 567 | 264 | 303 | 47% |
| g3 Roundhouse | 593 | 429 | 164 | 72% |
| g4 Tornado Kick | 1050 | 816 | 234 | 78% |

### Reach envelope (deterministic threat zone)
Entity-relative forward reach sampled from real sim hitboxes (includes lunge):

| move | reach (m) |
|---|---|
| g1 Low Kick | 1.24 |
| g2 Roundhouse | 0.16 |
| g3 Roundhouse | 1.32 |
| g4 Tornado Kick | 1.43 |
| a1 Double Punch | 0.99 |
| a2 Floating Kick | 1.18 |
| a3 High Kick | 0.92 |
| a4 Air Tornado | 1.03 |

### Whiff spots
Side-view (forward × height) grid, opponent position relative to the attacker at whiffed swings. Character's max reach is 1.43 m; the silhouette (per-height max reach) overlays the heatmap in HTML. 1898 total whiffs. Whiffs inside the silhouette = timing/placement (skill); beyond it = spacing (out of reach).
