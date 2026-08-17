# FightGuy — self-play telemetry
Generated 2026-08-17 18:55Z · 20 seeded bot-vs-bot matches on the real ServerSimulation (issue #148). 
Seed 42 · avg 178.4s, max 180.0s · wins 0–1, draws 19.

- **Hit rate** 43.5% (1765/4059 swings) — **whiff rate** 56.5% (2294/4059).
- **Combos**: avg length 2.27, max 5 (gap ≤ 1.5 s between same-pair hits).
- **Damage**: 276 per match, 46 per stock (6 stocks/match).

| move | swings | hits | whiffs | hit% |
|---|---|---|---|---|
| a1 Double Punch | 311 | 17 | 294 | 5% |
| a2 Floating Kick | 338 | 41 | 297 | 12% |
| a3 High Kick | 103 | 0 | 103 | 0% |
| a4 Air Tornado | 367 | 102 | 265 | 28% |
| g1 Low Kick | 914 | 716 | 198 | 78% |
| g2 Roundhouse | 1304 | 594 | 710 | 46% |
| g3 Uppercut | 272 | 165 | 107 | 61% |
| g4 Tornado Kick | 450 | 130 | 320 | 29% |

### Reach envelope (deterministic threat zone)
Entity-relative forward reach sampled from real sim hitboxes (includes lunge):

| move | reach (m) |
|---|---|
| g1 Low Kick | 0.56 |
| g2 Roundhouse | 0.61 |
| g3 Uppercut | 0.35 |
| g4 Tornado Kick | 0.40 |
| a1 Double Punch | 0.40 |
| a2 Floating Kick | 0.40 |
| a3 High Kick | 0.24 |
| a4 Air Tornado | 0.40 |

### Whiff spots
Side-view (forward × height) grid, opponent position relative to the attacker at whiffed swings. Character's max reach is 0.61 m; the silhouette (per-height max reach) overlays the heatmap in HTML. 2294 total whiffs. Whiffs inside the silhouette = timing/placement (skill); beyond it = spacing (out of reach).
