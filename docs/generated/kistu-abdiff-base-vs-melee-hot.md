# Kistu — tuning A/B diff: base vs melee-hot

Tool 1.0.0 · commit `73d1fe9fcd7cb41326e31d84f2b5ff21413fd089` · seed 20260817 (same both sides) · 20 matches · percents 0, 30, 60, 90, 120, 150.
Baseline **base**: base — shipped (melee-soft, #149) — stun 0.45×mag, KV×0.17. Candidate **melee-hot**: melee-hot — Melee shape, hotter — stun 0.4×mag, KV×0.22.

- **Move data**: 9/9 moves changed.
- **Combo links**: **+0 gained, -0 lost** (true-combo edges across starters × hit states × %).
- **Telemetry**: 14/15 stats moved (same seed → tuning effect only).

## Move-data diff

| move | % | KV b→c | stun b→c | adv b→c | apex b→c | kill% b→c |
|---|---|---|---|---|---|---|
| g1 Quick Slash | 0% | 4.28→5.54 | 11→10 | -1→-2 | 0.5→0.67 |  |
| g1 Quick Slash | 30% | 5.3→6.86 | 14→12 | 2→0 | 0.8→1.02 |  |
| g1 Quick Slash | 60% | 6.32→8.18 | 16→14 | 4→2 | 1.12→1.45 |  |
| g1 Quick Slash | 90% | 7.34→9.5 | 19→17 | 7→5 | 1.55→2.03 |  |
| g1 Quick Slash | 120% | 8.36→10.82 | 22→19 | 10→7 | 2.05→2.62 |  |
| g1 Quick Slash | 150% | 9.38→12.14 | 24→22 | 12→10 | 2.55→3.39 |  |
| g2 Double Slash | 0% | 3.36→4.35 | 8→7 | -16→-17 | 0.23→0.29 |  |
| g2 Double Slash | 30% | 4.18→5.41 | 11→9 | -13→-15 | 0.39→0.47 |  |
| g2 Double Slash | 60% | 4.99→6.46 | 13→11 | -11→-13 | 0.56→0.7 |  |
| g2 Double Slash | 90% | 5.81→7.52 | 15→13 | -9→-11 | 0.77→0.97 |  |
| g2 Double Slash | 120% | 6.63→8.58 | 17→15 | -7→-9 | 1→1.29 |  |
| g2 Double Slash | 150% | 7.44→9.63 | 19→17 | -5→-7 | 1.27→1.64 |  |
| g2 Double Slash (hit 2) | 0% | 5.7→7.37 | 15→13 | 3→1 | 0.93→1.19 |  |
| g2 Double Slash (hit 2) | 30% | 7.02→9.09 | 18→16 | 6→4 | 1.41→1.84 |  |
| g2 Double Slash (hit 2) | 60% | 8.35→10.81 | 22→19 | 10→7 | 2.05→2.62 |  |
| g2 Double Slash (hit 2) | 90% | 9.68→12.52 | 25→22 | 13→10 | 2.73→3.54 |  |
| g2 Double Slash (hit 2) | 120% | 11→14.24 | 29→25 | 17→13 | 3.6→4.6 |  |
| g2 Double Slash (hit 2) | 150% | 12.33→15.95 | 32→29 | 20→17 | 4.49→5.93 |  |
| g3 Up Slash | 0% | 5.56→7.2 | 14→13 | -8→-9 | 2.21→3.15 | 196→196 |
| g3 Up Slash | 30% | 6.79→8.78 | 17→15 | -5→-7 | 3.32→4.62 | 196→196 |
| g3 Up Slash | 60% | 8.01→10.37 | 21→18 | -1→-4 | 4.79→6.53 | 196→196 |
| g3 Up Slash | 90% | 9.23→11.95 | 24→21 | 2→-1 | 6.36→8.77 | 196→196 |
| g3 Up Slash | 120% | 10.46→13.53 | 27→24 | 5→2 | 8.17→11.34 | 196→196 |
| g3 Up Slash | 150% | 11.68→15.12 | 30→27 | 8→5 | 10.19→14.24 | 196→196 |
| g4 Heavy Down Slash | 0% | 7.66→9.91 | 20→18 | -5→-7 | 0.37→0.45 | 235→235 |
| g4 Heavy Down Slash | 30% | 9.29→12.02 | 24→21 | -1→-4 | 0.54→0.64 | 235→235 |
| g4 Heavy Down Slash | 60% | 10.92→14.13 | 28→25 | 3→0 | 0.75→0.91 | 235→235 |
| g4 Heavy Down Slash | 90% | 12.55→16.24 | 33→29 | 8→4 | 1.03→1.22 | 235→235 |
| g4 Heavy Down Slash | 120% | 14.18→18.36 | 37→33 | 12→8 | 1.31→1.57 | 235→235 |
| g4 Heavy Down Slash | 150% | 15.82→20.47 | 41→37 | 16→12 | 1.62→1.98 | 235→235 |
| a1 Air Slash | 0% | 4.34→5.61 | 11→10 | -5→-6 | 0.51→0.68 |  |
| a1 Air Slash | 30% | 5.36→6.93 | 14→12 | -2→-4 | 0.81→1.04 |  |
| a1 Air Slash | 60% | 6.38→8.25 | 16→15 | 0→-1 | 1.13→1.54 |  |
| a1 Air Slash | 90% | 7.4→9.57 | 19→17 | 3→1 | 1.57→2.05 |  |
| a1 Air Slash | 120% | 8.42→10.89 | 22→19 | 6→3 | 2.07→2.65 |  |
| a1 Air Slash | 150% | 9.43→12.21 | 24→22 | 8→6 | 2.56→3.42 |  |
| a2 Reverse Slash | 0% | 5.33→6.9 | 14→12 | -2→-4 | 1.13→1.48 |  |
| a2 Reverse Slash | 30% | 6.56→8.49 | 17→15 | 1→-1 | 1.72→2.29 |  |
| a2 Reverse Slash | 60% | 7.78→10.07 | 20→18 | 4→2 | 2.44→3.28 |  |
| a2 Reverse Slash | 90% | 9.01→11.66 | 23→21 | 7→5 | 3.27→4.44 |  |
| a2 Reverse Slash | 120% | 10.23→13.24 | 27→24 | 11→8 | 4.34→5.78 |  |
| a2 Reverse Slash | 150% | 11.45→14.82 | 30→26 | 14→10 | 5.43→7.13 |  |
| a3 Air Up Slash | 0% | 4.97→6.43 | 13→11 | -6→-8 | 1.78→2.41 | 231→231 |
| a3 Air Up Slash | 30% | 6.09→7.88 | 16→14 | -3→-5 | 2.71→3.73 | 231→231 |
| a3 Air Up Slash | 60% | 7.21→9.34 | 19→16 | 0→-3 | 3.84→5.19 | 231→231 |
| a3 Air Up Slash | 90% | 8.34→10.79 | 22→19 | 3→0 | 5.16→7.06 | 231→231 |
| a3 Air Up Slash | 120% | 9.46→12.24 | 25→22 | 6→3 | 6.68→9.21 | 231→231 |
| a3 Air Up Slash | 150% | 10.58→13.69 | 28→24 | 9→5 | 8.4→11.43 | 231→231 |
| a4 Air Heavy Down Slash | 0% | 6.05→7.83 | 16→14 | -5→-7 | 3 |  |
| a4 Air Heavy Down Slash | 30% | 7.38→9.55 | 19→17 | -2→-4 | 3 |  |
| a4 Air Heavy Down Slash | 60% | 8.7→11.26 | 23→20 | 2→-1 | 3 |  |
| a4 Air Heavy Down Slash | 90% | 10.03→12.98 | 26→23 | 5→2 | 3 |  |
| a4 Air Heavy Down Slash | 120% | 11.36→14.7 | 30→26 | 9→5 | 3 |  |
| a4 Air Heavy Down Slash | 150% | 12.68→16.41 | 33→29 | 12→8 | 3 |  |

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
| g1 Quick Slash | F→F | F→F | F→F | F→F | F→- | -→- |
| g2 Double Slash | F→F | F→F | F→F | F→F | F→- | -→- |
| g3 Up Slash | F→F | F→F | F→F | F→F | F→- | -→- |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→F | F→- | -→- |
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
| g1 Quick Slash | -→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | -→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | -→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | -→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a1 Air Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→F | F→F | F→- | -→- | -→- |
| g2 Double Slash | F→F | F→F | F→F | F→- | -→- | -→- |
| g3 Up Slash | F→F | F→F | F→F | F→- | -→- | -→- |
| g4 Heavy Down Slash | F→F | F→F | F→F | F→- | -→- | -→- |
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
| g1 Quick Slash | -→- | -→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | -→- | -→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | -→- | -→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | -→- | -→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→- | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→- | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→- | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→- | F→F | F→F | F→F |

### a3 Air Up Slash — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→- | -→- | -→- | -→F | F→F |
| g2 Double Slash | F→F | F→- | -→- | -→- | -→F | F→F |
| g3 Up Slash | F→F | F→- | -→- | -→- | -→F | F→F |
| g4 Heavy Down Slash | F→F | F→- | -→- | -→- | -→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

### a3 Air Up Slash — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Quick Slash | F→F | F→- | -→- | -→- | -→- | -→- |
| g2 Double Slash | F→F | F→- | -→- | -→- | -→- | -→- |
| g3 Up Slash | F→F | F→- | -→- | -→- | -→- | -→- |
| g4 Heavy Down Slash | F→F | F→- | -→- | -→- | -→- | -→- |
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
| g1 Quick Slash | -→F | F→F | F→F | F→F | F→F | F→F |
| g2 Double Slash | -→F | F→F | F→F | F→F | F→F | F→F |
| g3 Up Slash | -→F | F→F | F→F | F→F | F→F | F→F |
| g4 Heavy Down Slash | -→F | F→F | F→F | F→F | F→F | F→F |
| a1 Air Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Reverse Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a3 Air Up Slash | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Heavy Down Slash | F→F | F→F | F→F | F→F | F→F | F→F |

## Self-play telemetry diff

| stat | base | candidate | Δ |
|---|---|---|---|
| hit rate | 42.3% | 42.2% | -0.08pp |
| whiff rate | 57.7% | 57.8% | +0.08pp |
| avg combo length | 2.08 | 2.11 | +0.03 |
| max combo length | 3 | 4 | +1 |
| damage / match | 187.2 | 81.85 | -105.35 |
| damage / stock | 31.2 | 13.64 | -17.56 |
| wins (bot A) | 4 | 5 | +1 |
| wins (bot B) | 3 | 10 | +7 |
| draws | 13 | 5 | -8 |
| avg match duration (s) | 9175 | 7614 | -1561 |
| max match duration (s) | 10800 | 10800 | 0 |
| total swings | 3629 | 3011 | -618 |
| total hits | 1536 | 1272 | -264 |
| total whiffs | 2093 | 1739 | -354 |
| total damage | 10737 | 8674 | -2063 |

| move | swings b→c | hits b→c | whiffs b→c | hit% b→c |
|---|---|---|---|---|
| a1 Air Slash | 368→302 | 8→5 | 360→297 | 2.17→1.66 |
| a2 Reverse Slash | 397→314 | 76→42 | 321→272 | 19.14→13.38 |
| a3 Air Up Slash | 378→330 | 59→38 | 319→292 | 15.61→11.52 |
| a4 Air Heavy Down Slash | 404→309 | 15→3 | 389→306 | 3.71→0.97 |
| g1 Quick Slash | 536→455 | 294→264 | 242→191 | 54.85→58.02 |
| g2 Double Slash | 456→411 | 430→397 | 26→14 | 94.3→96.59 |
| g3 Up Slash | 534→420 | 517→406 | 17→14 | 96.82→96.67 |
| g4 Heavy Down Slash | 556→470 | 137→117 | 419→353 | 24.64→24.89 |

