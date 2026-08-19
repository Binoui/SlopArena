# Kistu — self-play telemetry
Generated 2026-08-19 08:13Z · 20 seeded bot-vs-bot matches on the real ServerSimulation (issue #148). 
Seed 42 · avg 150.0s, max 180.0s · wins 4–5, draws 11.

- **Hit rate** 42.7% (1552/3633 swings) — **whiff rate** 57.3% (2081/3633).
- **Combos**: avg length 2.10, max 4 (gap ≤ 1.5 s between same-pair hits).
- **Damage**: 186 per match, 31 per stock (6 stocks/match).

| move | swings | hits | whiffs | hit% |
|---|---|---|---|---|
| a1 Air Slash | 393 | 17 | 376 | 4% |
| a2 Reverse Slash | 393 | 57 | 336 | 15% |
| a3 Air Up Slash | 400 | 60 | 340 | 15% |
| a4 Air Heavy Down Slash | 366 | 9 | 357 | 2% |
| g1 Quick Slash | 525 | 283 | 242 | 54% |
| g2 Double Slash | 478 | 460 | 18 | 96% |
| g3 Up Slash | 546 | 523 | 23 | 96% |
| g4 Heavy Down Slash | 532 | 143 | 389 | 27% |

### Reach envelope (deterministic threat zone)
Entity-relative forward reach sampled from real sim hitboxes (includes lunge):

| move | reach (m) |
|---|---|
| g1 Quick Slash | 1.38 |
| g2 Double Slash | 0.98 |
| g3 Up Slash | 0.96 |
| g4 Heavy Down Slash | 0.95 |
| a1 Air Slash | 1.13 |
| a2 Reverse Slash | 0.91 |
| a3 Air Up Slash | 0.96 |
| a4 Air Heavy Down Slash | 1.02 |

### Whiff spots
Side-view (forward × height) grid, opponent position relative to the attacker at whiffed swings. Character's max reach is 1.38 m; the silhouette (per-height max reach) overlays the heatmap in HTML. 2081 total whiffs. Whiffs inside the silhouette = timing/placement (skill); beyond it = spacing (out of reach).
