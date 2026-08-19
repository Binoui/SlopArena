# FightGuy — self-play telemetry
Generated 2026-08-19 07:49Z · 20 seeded bot-vs-bot matches on the real ServerSimulation (issue #148). 
Seed 42 · avg 179.5s, max 180.0s · wins 0–1, draws 19.

- **Hit rate** 46.0% (1816/3950 swings) — **whiff rate** 54.0% (2134/3950).
- **Combos**: avg length 2.16, max 3 (gap ≤ 1.5 s between same-pair hits).
- **Damage**: 300 per match, 50 per stock (6 stocks/match).

| move | swings | hits | whiffs | hit% |
|---|---|---|---|---|
| a1 Double Punch | 377 | 5 | 372 | 1% |
| a2 Floating Kick | 387 | 89 | 298 | 23% |
| a3 High Kick | 122 | 0 | 122 | 0% |
| a4 Air Tornado | 398 | 0 | 398 | 0% |
| g1 Low Kick | 458 | 446 | 12 | 97% |
| g2 Roundhouse | 601 | 60 | 541 | 10% |
| g3 Roundhouse | 583 | 415 | 168 | 71% |
| g4 Tornado Kick | 1024 | 801 | 223 | 78% |

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
Side-view (forward × height) grid, opponent position relative to the attacker at whiffed swings. Character's max reach is 1.43 m; the silhouette (per-height max reach) overlays the heatmap in HTML. 2134 total whiffs. Whiffs inside the silhouette = timing/placement (skill); beyond it = spacing (out of reach).
