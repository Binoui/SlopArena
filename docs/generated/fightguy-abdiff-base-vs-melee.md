# FightGuy — tuning A/B diff: base vs melee

Tool 1.0.0 · commit `73d1fe9fcd7cb41326e31d84f2b5ff21413fd089` · seed 20260817 (same both sides) · 20 matches · percents 0, 30, 60, 90, 120, 150.
Baseline **base**: base — shipped — stun 0.7×(mag+20), KV×0.11. Candidate **melee**: melee — Melee shape — stun 0.4×mag, KV×0.19.

- **Move data**: 11/11 moves changed.
- **Combo links**: **+0 gained, -8 lost** (true-combo edges across starters × hit states × %).
- **Telemetry**: 14/15 stats moved (same seed → tuning effect only).

## Move-data diff

| move | % | KV b→c | stun b→c | adv b→c | apex b→c | kill% b→c |
|---|---|---|---|---|---|---|
| g1 Low Kick | 0% | 2.77→4.79 | 31→10 | 22→1 | 0.2→0.11 |  |
| g1 Low Kick | 30% | 3.43→5.93 | 35→12 | 26→3 | 0.27→0.17 |  |
| g1 Low Kick | 60% | 4.09→7.07 | 40→14 | 31→5 | 0.38→0.24 |  |
| g1 Low Kick | 90% | 4.75→8.21 | 44→17 | 35→8 | 0.48→0.34 |  |
| g1 Low Kick | 120% | 5.41→9.35 | 48→19 | 39→10 | 0.6→0.44 |  |
| g1 Low Kick | 150% | 6.07→10.49 | 52→22 | 43→13 | 0.74→0.58 |  |
| g2 Roundhouse | 0% | 4.55→7.86 | 42→16 | 22→-4 | 1.25→1.02 |  |
| g2 Roundhouse | 30% | 5.61→9.68 | 49→20 | 29→0 | 1.82→1.59 |  |
| g2 Roundhouse | 60% | 6.66→11.51 | 56→24 | 36→4 | 2.49→2.28 |  |
| g2 Roundhouse | 90% | 7.72→13.33 | 63→28 | 43→8 | 3.26→3.1 |  |
| g2 Roundhouse | 120% | 8.77→15.15 | 69→31 | 49→11 | 4.08→3.94 |  |
| g2 Roundhouse | 150% | 9.83→16.98 | 76→35 | 56→15 | 5.06→5 |  |
| g3 Roundhouse | 0% | 4.55→7.86 | 42→16 | 22→-4 | 1.25→1.02 |  |
| g3 Roundhouse | 30% | 5.61→9.68 | 49→20 | 29→0 | 1.82→1.59 |  |
| g3 Roundhouse | 60% | 6.66→11.51 | 56→24 | 36→4 | 2.49→2.28 |  |
| g3 Roundhouse | 90% | 7.72→13.33 | 63→28 | 43→8 | 3.26→3.1 |  |
| g3 Roundhouse | 120% | 8.77→15.15 | 69→31 | 49→11 | 4.08→3.94 |  |
| g3 Roundhouse | 150% | 9.83→16.98 | 76→35 | 56→15 | 5.06→5 |  |
| g4 Tornado Kick | 0% | 3.14→5.43 | 33→11 | -5→-27 | 1.21→0.99 |  |
| g4 Tornado Kick | 30% | 3.87→6.68 | 38→14 | 0→-24 | 1.73→1.55 |  |
| g4 Tornado Kick | 60% | 4.59→7.93 | 43→16 | 5→-22 | 2.35→2.16 |  |
| g4 Tornado Kick | 90% | 5.32→9.19 | 47→19 | 9→-19 | 3.01→2.97 |  |
| g4 Tornado Kick | 120% | 6.05→10.44 | 52→21 | 14→-17 | 3.81→3.79 |  |
| g4 Tornado Kick | 150% | 6.77→11.7 | 57→24 | 19→-14 | 4.7→4.84 |  |
| a1 Double Punch | 0% | 3.3→5.7 | 35→12 | 12→-11 | 1.77→1.6 |  |
| a1 Double Punch | 30% | 4.09→7.07 | 40→14 | 17→-9 | 2.55→2.41 |  |
| a1 Double Punch | 60% | 4.89→8.44 | 45→17 | 22→-6 | 3.47→3.49 |  |
| a1 Double Punch | 90% | 5.68→9.81 | 50→20 | 27→-3 | 4.53→4.78 |  |
| a1 Double Punch | 120% | 6.47→11.18 | 55→23 | 32→0 | 5.73→6.27 |  |
| a1 Double Punch | 150% | 7.26→12.54 | 60→26 | 37→3 | 7.06→7.97 |  |
| a1 Double Punch (hit 2) | 0% | 4.29→7.41 | 41→15 | 28→2 | 2.33→2.16 | 246→246 |
| a1 Double Punch (hit 2) | 30% | 5.28→9.12 | 47→19 | 34→6 | 3.33→3.37 | 246→246 |
| a1 Double Punch (hit 2) | 60% | 6.27→10.83 | 53→22 | 40→9 | 4.51→4.71 | 246→246 |
| a1 Double Punch (hit 2) | 90% | 7.26→12.54 | 60→26 | 47→13 | 5.95→6.43 | 246→246 |
| a1 Double Punch (hit 2) | 120% | 8.25→14.25 | 66→30 | 53→17 | 7.49→8.41 | 246→246 |
| a1 Double Punch (hit 2) | 150% | 9.24→15.96 | 72→33 | 59→20 | 9.2→10.47 | 246→246 |
| a2 Floating Kick | 0% | 5.19→8.96 | 46→18 | 17→-11 | 1.06→0.83 | 215→215 |
| a2 Floating Kick | 30% | 6.37→11.01 | 54→23 | 25→-6 | 1.54→1.31 | 215→215 |
| a2 Floating Kick | 60% | 7.56→13.06 | 62→27 | 33→-2 | 2.11→1.85 | 215→215 |
| a2 Floating Kick | 90% | 8.75→15.11 | 69→31 | 40→2 | 2.73→2.47 | 215→215 |
| a2 Floating Kick | 120% | 9.94→17.16 | 77→36 | 48→7 | 3.47→3.26 | 215→215 |
| a2 Floating Kick | 150% | 11.13→19.22 | 84→40 | 55→11 | 4.26→4.08 | 215→215 |
| a2 Floating Kick (hit 2) | 0% | 2.85→4.92 | 32→10 | 8→-14 | 0.39→0.24 |  |
| a2 Floating Kick (hit 2) | 30% | 3.58→6.18 | 36→13 | 12→-11 | 0.56→0.4 |  |
| a2 Floating Kick (hit 2) | 60% | 4.3→7.43 | 41→15 | 17→-9 | 0.78→0.57 |  |
| a2 Floating Kick (hit 2) | 90% | 5.03→8.69 | 46→18 | 22→-6 | 1.03→0.8 |  |
| a2 Floating Kick (hit 2) | 120% | 5.76→9.94 | 50→20 | 26→-4 | 1.28→1.03 |  |
| a2 Floating Kick (hit 2) | 150% | 6.48→11.19 | 55→23 | 31→-1 | 1.6→1.34 |  |
| a2 Floating Kick (hit 3) | 0% | 2.85→4.92 | 32→10 | 2→-20 | 0.39→0.24 |  |
| a2 Floating Kick (hit 3) | 30% | 3.58→6.18 | 36→13 | 6→-17 | 0.56→0.4 |  |
| a2 Floating Kick (hit 3) | 60% | 4.3→7.43 | 41→15 | 11→-15 | 0.78→0.57 |  |
| a2 Floating Kick (hit 3) | 90% | 5.03→8.69 | 46→18 | 16→-12 | 1.03→0.8 |  |
| a2 Floating Kick (hit 3) | 120% | 5.76→9.94 | 50→20 | 20→-10 | 1.28→1.03 |  |
| a2 Floating Kick (hit 3) | 150% | 6.48→11.19 | 55→23 | 25→-7 | 1.6→1.34 |  |
| a3 High Kick | 0% | 5.34→9.22 | 47→19 | 19→-9 | 0.89→0.69 | 218→218 |
| a3 High Kick | 30% | 6.53→11.27 | 55→23 | 27→-5 | 1.28→1.04 | 218→218 |
| a3 High Kick | 60% | 7.71→13.32 | 63→28 | 35→0 | 1.74→1.5 | 218→218 |
| a3 High Kick | 90% | 8.9→15.37 | 70→32 | 42→4 | 2.24→1.99 | 218→218 |
| a3 High Kick | 120% | 10.09→17.43 | 78→36 | 50→8 | 2.83→2.55 | 218→218 |
| a3 High Kick | 150% | 11.28→19.48 | 85→41 | 57→13 | 3.46→3.25 | 218→218 |
| a4 Air Tornado | 0% | 3.38→5.84 | 35→12 | 7→-16 | 1.22→0.99 |  |
| a4 Air Tornado | 30% | 4.17→7.2 | 40→15 | 12→-13 | 1.74→1.54 |  |
| a4 Air Tornado | 60% | 4.96→8.57 | 45→18 | 17→-10 | 2.35→2.22 |  |
| a4 Air Tornado | 90% | 5.76→9.94 | 50→20 | 22→-8 | 3.06→2.92 |  |
| a4 Air Tornado | 120% | 6.55→11.31 | 55→23 | 27→-5 | 3.85→3.83 |  |
| a4 Air Tornado | 150% | 7.34→12.68 | 60→26 | 32→-2 | 4.74→4.86 |  |

## True-combo graph diff

Verdict per starter × follow-up × %: `-` never connected, `F` landed after stun, `T` true combo. `a→b` = transition.

### g1 Low Kick — grounded hit (+0/-8)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | T→F | T→F | F→F | F→- | F→- | F→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→- | F→- | F→- |
| g4 Tornado Kick | F→F | F→F | F→F | F→- | F→- | F→- |
| a1 Double Punch | T→F | T→F | T→F | T→F | F→F | T→F |
| a2 Floating Kick | F→F | F→F | F→F | -→- | -→- | -→F |
| a3 High Kick | T→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Roundhouse — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→- | F→- | F→- | F→- | F→F |
| g2 Roundhouse | -→F | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→- | F→- | F→- | F→- | F→F |
| g4 Tornado Kick | F→- | F→- | F→- | F→- | F→- | F→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | F→- | F→- | F→- | -→- | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Roundhouse — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→- | F→- | F→- | F→- | F→- | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→- | F→- | F→- | F→- | F→- | F→F |
| g4 Tornado Kick | F→- | F→- | F→- | F→- | F→- | F→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→- | F→- | -→- | -→- | -→F |
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
| a2 Floating Kick | F→- | -→- | -→F | F→- | F→F | -→- |
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
| a2 Floating Kick | F→F | -→- | -→F | F→- | F→F | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a1 Double Punch — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | -→F | F→F |
| a2 Floating Kick | F→F | -→- | F→F | -→- | -→F | F→- |
| a3 High Kick | F→F | F→F | F→F | F→F | -→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | -→F | F→F |

### a1 Double Punch — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | -→- | -→F | F→F |
| a2 Floating Kick | F→- | -→- | -→- | -→- | -→F | F→F |
| a3 High Kick | F→F | F→F | F→F | -→- | -→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | -→- | -→- | F→F |

### a2 Floating Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | -→F | -→F | -→F |
| g2 Roundhouse | -→- | -→- | F→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | -→F | -→F | -→F |
| g4 Tornado Kick | F→F | F→F | -→F | -→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | F→- | F→- | -→F | F→- | F→- |
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
| a2 Floating Kick | -→- | -→F | -→- | F→F | F→F | F→- |
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
| a2 Floating Kick | F→F | -→F | -→- | F→F | -→F | -→F |
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
| a2 Floating Kick | -→- | F→- | -→F | -→F | F→F | F→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a4 Air Tornado — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | -→- | F→- | F→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | -→- | F→- | F→- |
| g4 Tornado Kick | F→F | F→F | F→- | -→- | F→- | F→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→- | -→F | -→- | F→- | F→- | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→- | F→F | F→F | F→F | F→F |

### a4 Air Tornado — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | -→F | -→F | -→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | -→F | -→F | -→F | -→F |
| g4 Tornado Kick | F→F | F→F | -→F | -→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→F | -→F | -→- | -→F | F→- | F→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

## Self-play telemetry diff

| stat | base | candidate | Δ |
|---|---|---|---|
| hit rate | 48.7% | 50.4% | +1.68pp |
| whiff rate | 51.3% | 49.6% | -1.68pp |
| avg combo length | 2.14 | 2.27 | +0.13 |
| max combo length | 4 | 5 | +1 |
| damage / match | 363.85 | 183.1 | -180.75 |
| damage / stock | 60.64 | 30.52 | -30.13 |
| wins (bot A) | 1 | 2 | +1 |
| wins (bot B) | 0 | 1 | +1 |
| draws | 19 | 17 | -2 |
| avg match duration (s) | 10775 | 10291 | -484 |
| max match duration (s) | 10800 | 10800 | 0 |
| total swings | 3877 | 4026 | +149 |
| total hits | 1888 | 2028 | +140 |
| total whiffs | 1989 | 1998 | +9 |
| total damage | 13026 | 14177 | +1151 |

| move | swings b→c | hits b→c | whiffs b→c | hit% b→c |
|---|---|---|---|---|
| a1 Double Punch | 370→352 | 5→3 | 365→349 | 1.35→0.85 |
| a2 Floating Kick | 372→334 | 106→80 | 266→254 | 28.49→23.95 |
| a3 High Kick | 115→85 | 0 | 115→85 | 0 |
| a4 Air Tornado | 402→346 | 0 | 402→346 | 0 |
| g1 Low Kick | 495→499 | 481→487 | 14→12 | 97.17→97.6 |
| g2 Roundhouse | 562→659 | 79→85 | 483→574 | 14.06→12.9 |
| g3 Roundhouse | 567→604 | 423→452 | 144→152 | 74.6→74.83 |
| g4 Tornado Kick | 994→1147 | 794→921 | 200→226 | 79.88→80.3 |

