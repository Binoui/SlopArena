# FightGuy — tuning A/B diff: base vs melee-hot

Tool 1.0.0 · commit `fd927fc8b16e41030ecbc8eb5d4da01ccdfe099c` · seed 20260817 (same both sides) · 10 matches · percents 0, 30, 60, 90, 120, 150.
Baseline **base**: base — shipped — stun 0.7×(mag+20), KV×0.11. Candidate **melee-hot**: melee-hot — Melee shape, hotter — stun 0.4×mag, KV×0.22.

- **Move data**: 11/11 moves changed.
- **Combo links**: **+0 gained, -8 lost** (true-combo edges across starters × hit states × %).
- **Telemetry**: 13/15 stats moved (same seed → tuning effect only).

## Move-data diff

| move | % | KV b→c | stun b→c | adv b→c | apex b→c | kill% b→c |
|---|---|---|---|---|---|---|
| g1 Low Kick | 0% | 2.77→5.54 | 31→10 | 22→1 | 0.2→0.13 |  |
| g1 Low Kick | 30% | 3.43→6.86 | 35→12 | 26→3 | 0.27→0.2 |  |
| g1 Low Kick | 60% | 4.09→8.18 | 40→14 | 31→5 | 0.38→0.28 |  |
| g1 Low Kick | 90% | 4.75→9.5 | 44→17 | 35→8 | 0.48→0.4 |  |
| g1 Low Kick | 120% | 5.41→10.82 | 48→19 | 39→10 | 0.6→0.52 |  |
| g1 Low Kick | 150% | 6.07→12.14 | 52→22 | 43→13 | 0.74→0.68 |  |
| g2 Roundhouse | 0% | 4.55→9.1 | 42→16 | 22→-4 | 1.25→1.24 | 215→215 |
| g2 Roundhouse | 30% | 5.61→11.21 | 49→20 | 29→0 | 1.82→1.92 | 215→215 |
| g2 Roundhouse | 60% | 6.66→13.32 | 56→24 | 36→4 | 2.49→2.76 | 215→215 |
| g2 Roundhouse | 90% | 7.72→15.44 | 63→28 | 43→8 | 3.26→3.75 | 215→215 |
| g2 Roundhouse | 120% | 8.77→17.55 | 69→31 | 49→11 | 4.08→4.78 | 215→215 |
| g2 Roundhouse | 150% | 9.83→19.66 | 76→35 | 56→15 | 5.06→6.05 | 215→215 |
| g3 Roundhouse | 0% | 4.55→9.1 | 42→16 | 22→-4 | 1.25→1.24 | 215→215 |
| g3 Roundhouse | 30% | 5.61→11.21 | 49→20 | 29→0 | 1.82→1.92 | 215→215 |
| g3 Roundhouse | 60% | 6.66→13.32 | 56→24 | 36→4 | 2.49→2.76 | 215→215 |
| g3 Roundhouse | 90% | 7.72→15.44 | 63→28 | 43→8 | 3.26→3.75 | 215→215 |
| g3 Roundhouse | 120% | 8.77→17.55 | 69→31 | 49→11 | 4.08→4.78 | 215→215 |
| g3 Roundhouse | 150% | 9.83→19.66 | 76→35 | 56→15 | 5.06→6.05 | 215→215 |
| g4 Tornado Kick | 0% | 3.14→6.28 | 33→11 | -5→-27 | 1.21→1.22 |  |
| g4 Tornado Kick | 30% | 3.87→7.74 | 38→14 | 0→-24 | 1.73→1.92 |  |
| g4 Tornado Kick | 60% | 4.59→9.19 | 43→16 | 5→-22 | 2.35→2.67 |  |
| g4 Tornado Kick | 90% | 5.32→10.64 | 47→19 | 9→-19 | 3.01→3.67 |  |
| g4 Tornado Kick | 120% | 6.05→12.09 | 52→21 | 14→-17 | 3.81→4.68 |  |
| g4 Tornado Kick | 150% | 6.77→13.54 | 57→24 | 19→-14 | 4.7→5.97 |  |
| a1 Double Punch | 0% | 3.3→6.6 | 35→12 | 12→-11 | 1.77→1.99 |  |
| a1 Double Punch | 30% | 4.09→8.19 | 40→14 | 17→-9 | 2.55→3 |  |
| a1 Double Punch | 60% | 4.89→9.77 | 45→17 | 22→-6 | 3.47→4.36 |  |
| a1 Double Punch | 90% | 5.68→11.36 | 50→20 | 27→-3 | 4.53→5.96 |  |
| a1 Double Punch | 120% | 6.47→12.94 | 55→23 | 32→0 | 5.73→7.81 |  |
| a1 Double Punch | 150% | 7.26→14.52 | 60→26 | 37→3 | 7.06→9.91 |  |
| a1 Double Punch (hit 2) | 0% | 4.29→8.58 | 41→15 | 28→2 | 2.33→2.68 | 210→210 |
| a1 Double Punch (hit 2) | 30% | 5.28→10.56 | 47→19 | 34→6 | 3.33→4.17 | 210→210 |
| a1 Double Punch (hit 2) | 60% | 6.27→12.54 | 53→22 | 40→9 | 4.51→5.84 | 210→210 |
| a1 Double Punch (hit 2) | 90% | 7.26→14.52 | 60→26 | 47→13 | 5.95→7.96 | 210→210 |
| a1 Double Punch (hit 2) | 120% | 8.25→16.5 | 66→30 | 53→17 | 7.49→10.4 | 210→210 |
| a1 Double Punch (hit 2) | 150% | 9.24→18.48 | 72→33 | 59→20 | 9.2→12.96 | 210→210 |
| a2 Floating Kick | 0% | 5.19→10.37 | 46→18 | 17→-11 | 1.06→1 | 180→180 |
| a2 Floating Kick | 30% | 6.37→12.75 | 54→23 | 25→-6 | 1.54→1.57 | 180→180 |
| a2 Floating Kick | 60% | 7.56→15.12 | 62→27 | 33→-2 | 2.11→2.21 | 180→180 |
| a2 Floating Kick | 90% | 8.75→17.5 | 69→31 | 40→2 | 2.73→2.96 | 180→180 |
| a2 Floating Kick | 120% | 9.94→19.87 | 77→36 | 48→7 | 3.47→3.9 | 180→180 |
| a2 Floating Kick | 150% | 11.13→22.25 | 84→40 | 55→11 | 4.26→4.88 | 180→180 |
| a2 Floating Kick (hit 2) | 0% | 2.85→5.7 | 32→10 | 8→-14 | 0.39→0.29 |  |
| a2 Floating Kick (hit 2) | 30% | 3.58→7.15 | 36→13 | 12→-11 | 0.56→0.48 |  |
| a2 Floating Kick (hit 2) | 60% | 4.3→8.61 | 41→15 | 17→-9 | 0.78→0.68 |  |
| a2 Floating Kick (hit 2) | 90% | 5.03→10.06 | 46→18 | 22→-6 | 1.03→0.96 |  |
| a2 Floating Kick (hit 2) | 120% | 5.76→11.51 | 50→20 | 26→-4 | 1.28→1.24 |  |
| a2 Floating Kick (hit 2) | 150% | 6.48→12.96 | 55→23 | 31→-1 | 1.6 |  |
| a2 Floating Kick (hit 3) | 0% | 2.85→5.7 | 32→10 | 2→-20 | 0.39→0.29 |  |
| a2 Floating Kick (hit 3) | 30% | 3.58→7.15 | 36→13 | 6→-17 | 0.56→0.48 |  |
| a2 Floating Kick (hit 3) | 60% | 4.3→8.61 | 41→15 | 11→-15 | 0.78→0.68 |  |
| a2 Floating Kick (hit 3) | 90% | 5.03→10.06 | 46→18 | 16→-12 | 1.03→0.96 |  |
| a2 Floating Kick (hit 3) | 120% | 5.76→11.51 | 50→20 | 20→-10 | 1.28→1.24 |  |
| a2 Floating Kick (hit 3) | 150% | 6.48→12.96 | 55→23 | 25→-7 | 1.6 |  |
| a3 High Kick | 0% | 5.34→10.67 | 47→19 | 19→-9 | 0.89→0.82 | 183→183 |
| a3 High Kick | 30% | 6.53→13.05 | 55→23 | 27→-5 | 1.28→1.24 | 183→183 |
| a3 High Kick | 60% | 7.71→15.43 | 63→28 | 35→0 | 1.74→1.78 | 183→183 |
| a3 High Kick | 90% | 8.9→17.8 | 70→32 | 42→4 | 2.24→2.37 | 183→183 |
| a3 High Kick | 120% | 10.09→20.18 | 78→36 | 50→8 | 2.83→3.04 | 183→183 |
| a3 High Kick | 150% | 11.28→22.55 | 85→41 | 57→13 | 3.46→3.87 | 183→183 |
| a4 Air Tornado | 0% | 3.38→6.76 | 35→12 | 7→-16 | 1.22 |  |
| a4 Air Tornado | 30% | 4.17→8.34 | 40→15 | 12→-13 | 1.74→1.89 |  |
| a4 Air Tornado | 60% | 4.96→9.93 | 45→18 | 17→-10 | 2.35→2.72 |  |
| a4 Air Tornado | 90% | 5.76→11.51 | 50→20 | 22→-8 | 3.06→3.59 |  |
| a4 Air Tornado | 120% | 6.55→13.09 | 55→23 | 27→-5 | 3.85→4.71 |  |
| a4 Air Tornado | 150% | 7.34→14.68 | 60→26 | 32→-2 | 4.74→5.97 |  |

## True-combo graph diff

Verdict per starter × follow-up × %: `-` never connected, `F` landed after stun, `T` true combo. `a→b` = transition.

### g1 Low Kick — grounded hit (+0/-8)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | T→F | T→F | F→F | F→- | F→- | F→F |
| g2 Roundhouse | -→- | -→- | -→F | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→- | F→- | F→F |
| g4 Tornado Kick | F→F | F→F | F→- | F→- | F→- | F→- |
| a1 Double Punch | T→F | T→F | T→F | T→F | F→F | T→F |
| a2 Floating Kick | F→- | F→- | F→F | -→- | -→- | -→- |
| a3 High Kick | T→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Roundhouse — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→- | F→- | F→- | F→F | F→F | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→F | -→- | -→- |
| g3 Roundhouse | F→- | F→- | F→- | F→F | F→F | F→F |
| g4 Tornado Kick | F→- | F→- | F→- | F→- | F→F | F→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | F→F | F→- | F→- | -→- | -→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Roundhouse — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→- | F→- | F→- | F→- | F→F | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→- | F→- | F→- | F→- | F→F | F→F |
| g4 Tornado Kick | F→- | F→- | F→- | F→- | F→F | F→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→F | F→- | -→- | -→- | -→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g4 Tornado Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→- | -→- | F→- | F→F | -→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g4 Tornado Kick — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→- | -→- | F→- | F→F | -→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→- | F→F | F→F | F→F | F→F | F→F |

### a1 Double Punch — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | -→F | F→F |
| a2 Floating Kick | F→- | -→- | F→F | -→F | -→- | F→F |
| a3 High Kick | F→F | F→F | F→F | F→F | -→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | -→F | F→F |

### a1 Double Punch — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | -→F | -→F | F→F |
| a2 Floating Kick | F→- | -→- | -→F | -→- | -→F | F→F |
| a3 High Kick | F→F | F→F | F→F | -→F | -→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | -→F | -→F | F→F |

### a2 Floating Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | -→F | -→F | -→F |
| g2 Roundhouse | -→- | -→- | F→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | -→F | -→F | -→F |
| g4 Tornado Kick | F→F | F→F | -→F | -→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | F→- | F→F | -→- | F→F | F→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a2 Floating Kick — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | F→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→F | -→F | -→- | F→F | F→- | F→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a3 High Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | -→F | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | -→F | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | -→F | F→F | F→F | F→F | F→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→- | -→F | -→- | F→F | -→F | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a3 High Kick — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | -→F | -→F | -→F | -→F | -→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | -→F | -→F | -→F | -→F | -→F | -→F |
| g4 Tornado Kick | -→F | -→F | -→F | -→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→F | F→- | -→F | -→F | F→F | F→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a4 Air Tornado — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | -→- | F→- | F→- |
| g2 Roundhouse | -→- | -→- | -→F | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | -→- | F→- | F→- |
| g4 Tornado Kick | F→F | F→F | F→- | -→- | F→- | F→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→- | -→F | -→- | F→- | F→- | -→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a4 Air Tornado — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | -→F | -→F | -→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | -→F | -→F | -→F | -→F |
| g4 Tornado Kick | F→F | F→F | -→F | -→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→- | -→- | -→- | -→- | F→F | F→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

## Self-play telemetry diff

| stat | base | candidate | Δ |
|---|---|---|---|
| hit rate | 49.4% | 50.1% | +0.7pp |
| whiff rate | 50.6% | 49.9% | -0.7pp |
| avg combo length | 2.13 | 2.2 | +0.07 |
| max combo length | 4 | 4 | 0 |
| damage / match | 369.8 | 207.6 | -162.2 |
| damage / stock | 61.63 | 34.6 | -27.03 |
| wins (bot A) | 1 | 0 | -1 |
| wins (bot B) | 0 | 3 | +3 |
| draws | 9 | 7 | -2 |
| avg match duration (s) | 10750 | 10406 | -344 |
| max match duration (s) | 10800 | 10800 | 0 |
| total swings | 1999 | 1987 | -12 |
| total hits | 987 | 995 | +8 |
| total whiffs | 1012 | 992 | -20 |
| total damage | 6709 | 6976 | +267 |

| move | swings b→c | hits b→c | whiffs b→c | hit% b→c |
|---|---|---|---|---|
| a1 Double Punch | 164→186 | 4→2 | 160→184 | 2.44→1.08 |
| a2 Floating Kick | 190→157 | 47→31 | 143→126 | 24.74→19.75 |
| a3 High Kick | 59→54 | 0 | 59→54 | 0 |
| a4 Air Tornado | 212→161 | 0 | 212→161 | 0 |
| g1 Low Kick | 286→238 | 277→230 | 9→8 | 96.85→96.64 |
| g2 Roundhouse | 289→302 | 42→30 | 247→272 | 14.53→9.93 |
| g3 Roundhouse | 287→343 | 211→267 | 76 | 73.52→77.84 |
| g4 Tornado Kick | 512→546 | 406→435 | 106→111 | 79.3→79.67 |

