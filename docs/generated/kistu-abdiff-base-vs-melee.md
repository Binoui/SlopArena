# Kistu — tuning A/B diff: base vs melee

Tool 1.0.0 · commit `73d1fe9fcd7cb41326e31d84f2b5ff21413fd089` · seed 20260817 (same both sides) · 20 matches · percents 0, 30, 60, 90, 120, 150.
Baseline **base**: base — shipped (melee-soft, #149) — stun 0.45×mag, KV×0.17. Candidate **melee**: melee — Melee shape — stun 0.4×mag, KV×0.19.

- **Move data**: 9/9 moves changed.
- **Combo links**: **+0 gained, -0 lost** (true-combo edges across starters × hit states × %).
- **Telemetry**: 12/15 stats moved (same seed → tuning effect only).

## Move-data diff

| move | % | KV b→c | stun b→c | adv b→c | apex b→c | kill% b→c |
|---|---|---|---|---|---|---|
| g1 Quick Slash | 0% | 4.28→4.79 | 11→10 | -1→-2 | 0.5→0.54 |  |
| g1 Quick Slash | 30% | 5.3→5.93 | 14→12 | 2→0 | 0.8→0.83 |  |
| g1 Quick Slash | 60% | 6.32→7.07 | 16→14 | 4→2 | 1.12→1.18 |  |
| g1 Quick Slash | 90% | 7.34→8.21 | 19→17 | 7→5 | 1.55→1.66 |  |
| g1 Quick Slash | 120% | 8.36→9.35 | 22→19 | 10→7 | 2.05→2.14 |  |
| g1 Quick Slash | 150% | 9.38→10.49 | 24→22 | 12→10 | 2.55→2.77 |  |
| g2 Double Slash | 0% | 3.36→3.76 | 8→7 | -16→-17 | 0.23→0.24 |  |
| g2 Double Slash | 30% | 4.18→4.67 | 11→9 | -13→-15 | 0.39 |  |
| g2 Double Slash | 60% | 4.99→5.58 | 13→11 | -11→-13 | 0.56→0.57 |  |
| g2 Double Slash | 90% | 5.81→6.49 | 15→13 | -9→-11 | 0.77→0.8 |  |
| g2 Double Slash | 120% | 6.63→7.41 | 17→15 | -7→-9 | 1→1.05 |  |
| g2 Double Slash | 150% | 7.44→8.32 | 19→17 | -5→-7 | 1.27→1.35 |  |
| g2 Double Slash (hit 2) | 0% | 5.7→6.37 | 15→13 | 3→1 | 0.93→0.97 |  |
| g2 Double Slash (hit 2) | 30% | 7.02→7.85 | 18→16 | 6→4 | 1.41→1.5 |  |
| g2 Double Slash (hit 2) | 60% | 8.35→9.33 | 22→19 | 10→7 | 2.05→2.14 |  |
| g2 Double Slash (hit 2) | 90% | 9.68→10.81 | 25→22 | 13→10 | 2.73→2.89 |  |
| g2 Double Slash (hit 2) | 120% | 11→12.3 | 29→25 | 17→13 | 3.6→3.76 |  |
| g2 Double Slash (hit 2) | 150% | 12.33→13.78 | 32→29 | 20→17 | 4.49→4.85 |  |
| g3 Up Slash | 0% | 5.56→6.22 | 14→13 | -8→-9 | 2.21→2.51 | 234→234 |
| g3 Up Slash | 30% | 6.79→7.58 | 17→15 | -5→-7 | 3.32→3.67 | 234→234 |
| g3 Up Slash | 60% | 8.01→8.95 | 21→18 | -1→-4 | 4.79→5.2 | 234→234 |
| g3 Up Slash | 90% | 9.23→10.32 | 24→21 | 2→-1 | 6.36→6.99 | 234→234 |
| g3 Up Slash | 120% | 10.46→11.69 | 27→24 | 5→2 | 8.17→9.05 | 234→234 |
| g3 Up Slash | 150% | 11.68→13.06 | 30→27 | 8→5 | 10.19→11.37 | 234→234 |
| g4 Heavy Down Slash | 0% | 7.66→8.56 | 20→18 | -5→-7 | 0.37→0.38 |  |
| g4 Heavy Down Slash | 30% | 9.29→10.38 | 24→21 | -1→-4 | 0.54 |  |
| g4 Heavy Down Slash | 60% | 10.92→12.21 | 28→25 | 3→0 | 0.75→0.77 |  |
| g4 Heavy Down Slash | 90% | 12.55→14.03 | 33→29 | 8→4 | 1.03 |  |
| g4 Heavy Down Slash | 120% | 14.18→15.85 | 37→33 | 12→8 | 1.31→1.33 |  |
| g4 Heavy Down Slash | 150% | 15.82→17.68 | 41→37 | 16→12 | 1.62→1.67 |  |
| a1 Air Slash | 0% | 4.34→4.85 | 11→10 | -5→-6 | 0.51→0.55 |  |
| a1 Air Slash | 30% | 5.36→5.99 | 14→12 | -2→-4 | 0.81→0.84 |  |
| a1 Air Slash | 60% | 6.38→7.12 | 16→15 | 0→-1 | 1.13→1.26 |  |
| a1 Air Slash | 90% | 7.4→8.26 | 19→17 | 3→1 | 1.57→1.68 |  |
| a1 Air Slash | 120% | 8.42→9.4 | 22→19 | 6→3 | 2.07→2.16 |  |
| a1 Air Slash | 150% | 9.43→10.54 | 24→22 | 8→6 | 2.56→2.79 |  |
| a2 Reverse Slash | 0% | 5.33→5.96 | 14→12 | -2→-4 | 1.13→1.2 |  |
| a2 Reverse Slash | 30% | 6.56→7.33 | 17→15 | 1→-1 | 1.72→1.85 |  |
| a2 Reverse Slash | 60% | 7.78→8.7 | 20→18 | 4→2 | 2.44→2.65 |  |
| a2 Reverse Slash | 90% | 9.01→10.07 | 23→21 | 7→5 | 3.27→3.6 |  |
| a2 Reverse Slash | 120% | 10.23→11.43 | 27→24 | 11→8 | 4.34→4.69 |  |
| a2 Reverse Slash | 150% | 11.45→12.8 | 30→26 | 14→10 | 5.43→5.78 |  |
| a3 Air Up Slash | 0% | 4.97→5.56 | 13→11 | -6→-8 | 1.78→1.92 |  |
| a3 Air Up Slash | 30% | 6.09→6.81 | 16→14 | -3→-5 | 2.71→2.97 |  |
| a3 Air Up Slash | 60% | 7.21→8.06 | 19→16 | 0→-3 | 3.84→4.13 |  |
| a3 Air Up Slash | 90% | 8.34→9.32 | 22→19 | 3→0 | 5.16→5.62 |  |
| a3 Air Up Slash | 120% | 9.46→10.57 | 25→22 | 6→3 | 6.68→7.35 |  |
| a3 Air Up Slash | 150% | 10.58→11.83 | 28→24 | 9→5 | 8.4→9.12 |  |
| a4 Air Heavy Down Slash | 0% | 6.05→6.76 | 16→14 | -5→-7 | 3 |  |
| a4 Air Heavy Down Slash | 30% | 7.38→8.25 | 19→17 | -2→-4 | 3 |  |
| a4 Air Heavy Down Slash | 60% | 8.7→9.73 | 23→20 | 2→-1 | 3 |  |
| a4 Air Heavy Down Slash | 90% | 10.03→11.21 | 26→23 | 5→2 | 3 |  |
| a4 Air Heavy Down Slash | 120% | 11.36→12.69 | 30→26 | 9→5 | 3 |  |
| a4 Air Heavy Down Slash | 150% | 12.68→14.17 | 33→29 | 12→8 | 3 |  |

## True-combo graph diff

Verdict per starter × follow-up × %: `-` never connected, `F` landed after stun, `T` true combo. `a→b` = transition.

### g1 Quick Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | -→F | -→F |
| g2 Double Slash | F→F | F→F | F→F | F→F | -→F | -→F |
| g3 Up Slash | F→F | F→F | F→F | F→F | -→F | -→F |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | -→F | -→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### g1 Quick Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→F | -→- |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→F | -→- |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→F | -→- |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | -→- |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### g2 Double Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### g2 Double Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Up Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Up Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### g4 Heavy Down Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### g4 Heavy Down Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | -→F | -→F | F→F | F→F | F→F |
| g2 Double Slash | F→F | -→F | -→F | F→F | F→F | F→F |
| g3 Up Slash | F→F | -→F | -→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | F→F | -→F | -→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a1 Air Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | -→- | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | -→- | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | -→- | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | -→- | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a1 Air Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | -→- | -→- |
| g2 Double Slash | F→F | F→F | F→F | F→F | -→- | -→- |
| g3 Up Slash | F→F | F→F | F→F | F→F | -→- | -→- |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | -→- | -→- |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a2 Reverse Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a2 Reverse Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | -→- | -→- | F→F | F→F | F→F | F→F |
| g2 Double Slash | -→- | -→- | F→F | F→F | F→F | F→F |
| g3 Up Slash | -→- | -→- | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | -→- | -→- | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→- | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→- | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→- | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→- | F→F | F→F |

### a3 Air Up Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | -→- | -→- | -→- | F→F |
| g2 Double Slash | F→F | F→F | -→- | -→- | -→- | F→F |
| g3 Up Slash | F→F | F→F | -→- | -→- | -→- | F→F |
| g4 Heavy Down Slash | F→F | F→F | -→- | -→- | -→- | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a3 Air Up Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | -→- | -→- | -→- | -→- |
| g2 Double Slash | F→F | F→F | -→- | -→- | -→- | -→- |
| g3 Up Slash | F→F | F→F | -→- | -→- | -→- | -→- |
| g4 Heavy Down Slash | F→F | F→F | -→- | -→- | -→- | -→- |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a4 Air Heavy Down Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a4 Air Heavy Down Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | -→- | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | -→- | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | -→- | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | -→- | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

## Self-play telemetry diff

| stat | base | candidate | Δ |
|---|---|---|---|
| hit rate | 42.3% | 42.4% | +0.08pp |
| whiff rate | 57.7% | 57.6% | -0.08pp |
| avg combo length | 2.08 | 2.1 | +0.02 |
| max combo length | 3 | 3 | 0 |
| damage / match | 187.2 | 159.15 | -28.05 |
| damage / stock | 31.2 | 26.53 | -4.68 |
| wins (bot A) | 4 | 10 | +6 |
| wins (bot B) | 3 | 3 | 0 |
| draws | 13 | 7 | -6 |
| avg match duration (s) | 9175 | 8665 | -510 |
| max match duration (s) | 10800 | 10800 | 0 |
| total swings | 3629 | 3464 | -165 |
| total hits | 1536 | 1469 | -67 |
| total whiffs | 2093 | 1995 | -98 |
| total damage | 10737 | 10123 | -614 |

| move | swings b→c | hits b→c | whiffs b→c | hit% b→c |
|---|---|---|---|---|
| a1 Air Slash | 368 | 8→14 | 360→354 | 2.17→3.8 |
| a2 Reverse Slash | 397→371 | 76→62 | 321→309 | 19.14→16.71 |
| a3 Air Up Slash | 378→362 | 59→58 | 319→304 | 15.61→16.02 |
| a4 Air Heavy Down Slash | 404→379 | 15→8 | 389→371 | 3.71→2.11 |
| g1 Quick Slash | 536→483 | 294→259 | 242→224 | 54.85→53.62 |
| g2 Double Slash | 456→484 | 430→452 | 26→32 | 94.3→93.39 |
| g3 Up Slash | 534→519 | 517→500 | 17→19 | 96.82→96.34 |
| g4 Heavy Down Slash | 556→498 | 137→116 | 419→382 | 24.64→23.29 |

