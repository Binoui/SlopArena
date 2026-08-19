# FightGuy — tuning A/B diff: base vs melee-soft

Tool 1.0.0 · commit `73d1fe9fcd7cb41326e31d84f2b5ff21413fd089` · seed 20260817 (same both sides) · 20 matches · percents 0, 30, 60, 90, 120, 150.
Baseline **base**: base — shipped — stun 0.7×(mag+20), KV×0.11. Candidate **melee-soft**: melee-soft — Melee shape, softer — stun 0.45×mag, KV×0.17.

- **Move data**: 11/11 moves changed.
- **Combo links**: **+0 gained, -8 lost** (true-combo edges across starters × hit states × %).
- **Telemetry**: 12/15 stats moved (same seed → tuning effect only).

## Move-data diff

| move | % | KV b→c | stun b→c | adv b→c | apex b→c | kill% b→c |
|---|---|---|---|---|---|---|
| g1 Low Kick | 0% | 2.77→4.28 | 31→11 | 22→2 | 0.2→0.11 |  |
| g1 Low Kick | 30% | 3.43→5.3 | 35→14 | 26→5 | 0.27→0.17 |  |
| g1 Low Kick | 60% | 4.09→6.32 | 40→16 | 31→7 | 0.38→0.24 |  |
| g1 Low Kick | 90% | 4.75→7.34 | 44→19 | 35→10 | 0.48→0.34 |  |
| g1 Low Kick | 120% | 5.41→8.36 | 48→22 | 39→13 | 0.6→0.45 |  |
| g1 Low Kick | 150% | 6.07→9.38 | 52→24 | 43→15 | 0.74→0.55 |  |
| g2 Roundhouse | 0% | 4.55→7.03 | 42→18 | 22→-2 | 1.25→0.97 |  |
| g2 Roundhouse | 30% | 5.61→8.66 | 49→22 | 29→2 | 1.82→1.49 |  |
| g2 Roundhouse | 60% | 6.66→10.3 | 56→27 | 36→7 | 2.49→2.17 |  |
| g2 Roundhouse | 90% | 7.72→11.93 | 63→31 | 43→11 | 3.26→2.91 |  |
| g2 Roundhouse | 120% | 8.77→13.56 | 69→35 | 49→15 | 4.08→3.76 |  |
| g2 Roundhouse | 150% | 9.83→15.19 | 76→40 | 56→20 | 5.06→4.81 |  |
| g3 Roundhouse | 0% | 4.55→7.03 | 42→18 | 22→-2 | 1.25→0.97 |  |
| g3 Roundhouse | 30% | 5.61→8.66 | 49→22 | 29→2 | 1.82→1.49 |  |
| g3 Roundhouse | 60% | 6.66→10.3 | 56→27 | 36→7 | 2.49→2.17 |  |
| g3 Roundhouse | 90% | 7.72→11.93 | 63→31 | 43→11 | 3.26→2.91 |  |
| g3 Roundhouse | 120% | 8.77→13.56 | 69→35 | 49→15 | 4.08→3.76 |  |
| g3 Roundhouse | 150% | 9.83→15.19 | 76→40 | 56→20 | 5.06→4.81 |  |
| g4 Tornado Kick | 0% | 3.14→4.86 | 33→12 | -5→-26 | 1.21→0.89 |  |
| g4 Tornado Kick | 30% | 3.87→5.98 | 38→15 | 0→-23 | 1.73→1.39 |  |
| g4 Tornado Kick | 60% | 4.59→7.1 | 43→18 | 5→-20 | 2.35→2 |  |
| g4 Tornado Kick | 90% | 5.32→8.22 | 47→21 | 9→-17 | 3.01→2.72 |  |
| g4 Tornado Kick | 120% | 6.05→9.34 | 52→24 | 14→-14 | 3.81→3.54 |  |
| g4 Tornado Kick | 150% | 6.77→10.47 | 57→27 | 19→-11 | 4.7→4.48 |  |
| a1 Double Punch | 0% | 3.3→5.1 | 35→13 | 12→-10 | 1.77→1.43 |  |
| a1 Double Punch | 30% | 4.09→6.33 | 40→16 | 17→-7 | 2.55→2.21 |  |
| a1 Double Punch | 60% | 4.89→7.55 | 45→19 | 22→-4 | 3.47→3.17 |  |
| a1 Double Punch | 90% | 5.68→8.78 | 50→23 | 27→0 | 4.53→4.42 |  |
| a1 Double Punch | 120% | 6.47→10 | 55→26 | 32→3 | 5.73→5.74 |  |
| a1 Double Punch | 150% | 7.26→11.22 | 60→29 | 37→6 | 7.06→7.23 |  |
| a1 Double Punch (hit 2) | 0% | 4.29→6.63 | 41→17 | 28→4 | 2.33→2 |  |
| a1 Double Punch (hit 2) | 30% | 5.28→8.16 | 47→21 | 34→8 | 3.33→3.06 |  |
| a1 Double Punch (hit 2) | 60% | 6.27→9.69 | 53→25 | 40→12 | 4.51→4.36 |  |
| a1 Double Punch (hit 2) | 90% | 7.26→11.22 | 60→29 | 47→16 | 5.95→5.88 |  |
| a1 Double Punch (hit 2) | 120% | 8.25→12.75 | 66→33 | 53→20 | 7.49→7.64 |  |
| a1 Double Punch (hit 2) | 150% | 9.24→14.28 | 72→37 | 59→24 | 9.2→9.62 |  |
| a2 Floating Kick | 0% | 5.19→8.01 | 46→21 | 17→-8 | 1.06→0.83 | 231→231 |
| a2 Floating Kick | 30% | 6.37→9.85 | 54→26 | 25→-3 | 1.54→1.27 | 231→231 |
| a2 Floating Kick | 60% | 7.56→11.69 | 62→30 | 33→1 | 2.11→1.76 | 231→231 |
| a2 Floating Kick | 90% | 8.75→13.52 | 69→35 | 40→6 | 2.73→2.39 | 231→231 |
| a2 Floating Kick | 120% | 9.94→15.36 | 77→40 | 48→11 | 3.47→3.11 | 231→231 |
| a2 Floating Kick | 150% | 11.13→17.19 | 84→45 | 55→16 | 4.26→3.93 | 231→231 |
| a2 Floating Kick (hit 2) | 0% | 2.85→4.41 | 32→11 | 8→-13 | 0.39→0.23 |  |
| a2 Floating Kick (hit 2) | 30% | 3.58→5.53 | 36→14 | 12→-10 | 0.56→0.37 |  |
| a2 Floating Kick (hit 2) | 60% | 4.3→6.65 | 41→17 | 17→-7 | 0.78→0.55 |  |
| a2 Floating Kick (hit 2) | 90% | 5.03→7.77 | 46→20 | 22→-4 | 1.03→0.77 |  |
| a2 Floating Kick (hit 2) | 120% | 5.76→8.89 | 50→23 | 26→-1 | 1.28→1.01 |  |
| a2 Floating Kick (hit 2) | 150% | 6.48→10.02 | 55→26 | 31→2 | 1.6→1.3 |  |
| a2 Floating Kick (hit 3) | 0% | 2.85→4.41 | 32→11 | 2→-19 | 0.39→0.23 |  |
| a2 Floating Kick (hit 3) | 30% | 3.58→5.53 | 36→14 | 6→-16 | 0.56→0.37 |  |
| a2 Floating Kick (hit 3) | 60% | 4.3→6.65 | 41→17 | 11→-13 | 0.78→0.55 |  |
| a2 Floating Kick (hit 3) | 90% | 5.03→7.77 | 46→20 | 16→-10 | 1.03→0.77 |  |
| a2 Floating Kick (hit 3) | 120% | 5.76→8.89 | 50→23 | 20→-7 | 1.28→1.01 |  |
| a2 Floating Kick (hit 3) | 150% | 6.48→10.02 | 55→26 | 25→-4 | 1.6→1.3 |  |
| a3 High Kick | 0% | 5.34→8.25 | 47→21 | 19→-7 | 0.89→0.66 | 232→232 |
| a3 High Kick | 30% | 6.53→10.08 | 55→26 | 27→-2 | 1.28→1.01 | 232→232 |
| a3 High Kick | 60% | 7.71→11.92 | 63→31 | 35→3 | 1.74→1.44 | 232→232 |
| a3 High Kick | 90% | 8.9→13.76 | 70→36 | 42→8 | 2.24→1.94 | 232→232 |
| a3 High Kick | 120% | 10.09→15.59 | 78→41 | 50→13 | 2.83→2.51 | 232→232 |
| a3 High Kick | 150% | 11.28→17.43 | 85→46 | 57→18 | 3.46→3.16 | 232→232 |
| a4 Air Tornado | 0% | 3.38→5.22 | 35→13 | 7→-15 | 1.22→0.89 |  |
| a4 Air Tornado | 30% | 4.17→6.45 | 40→17 | 12→-11 | 1.74→1.44 |  |
| a4 Air Tornado | 60% | 4.96→7.67 | 45→20 | 17→-8 | 2.35→2.05 |  |
| a4 Air Tornado | 90% | 5.76→8.89 | 50→23 | 22→-5 | 3.06→2.76 |  |
| a4 Air Tornado | 120% | 6.55→10.12 | 55→26 | 27→-2 | 3.85→3.57 |  |
| a4 Air Tornado | 150% | 7.34→11.34 | 60→30 | 32→2 | 4.74→4.6 |  |

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
| a2 Floating Kick | F→- | F→- | F→- | -→F | -→F | -→F |
| a3 High Kick | T→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Roundhouse — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→- | F→- | F→- | F→- | F→- |
| g2 Roundhouse | -→F | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→- | F→- | F→- | F→- | F→- |
| g4 Tornado Kick | F→- | F→- | F→- | F→- | F→- | F→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | F→F | F→F | F→- | -→- | -→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→- | F→F | F→F | F→F | F→F |

### g3 Roundhouse — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→- | F→- | F→- | F→- | F→- |
| g2 Roundhouse | -→F | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→- | F→- | F→- | F→- | F→- |
| g4 Tornado Kick | F→- | F→- | F→- | F→- | F→- | F→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→- | -→- | F→- | -→- | -→- | -→- |
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
| a2 Floating Kick | F→- | -→F | -→- | F→F | F→F | -→F |
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
| a2 Floating Kick | F→- | -→F | -→- | F→F | F→F | -→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→- | F→F | F→F | F→F | F→F |

### a1 Double Punch — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | -→F | F→F |
| a2 Floating Kick | F→F | -→F | F→F | -→- | -→- | F→- |
| a3 High Kick | F→F | F→F | F→F | F→F | -→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | -→F | F→F |

### a1 Double Punch — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→- | F→F | F→F | F→F | F→F | -→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→- | F→F | F→F | F→F | F→F | -→F |
| g4 Tornado Kick | F→- | F→F | F→F | F→F | F→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | -→F | -→- | F→F |
| a2 Floating Kick | F→- | -→- | -→F | -→F | -→- | F→F |
| a3 High Kick | F→F | F→F | F→F | -→F | -→- | F→F |
| a4 Air Tornado | F→F | F→F | F→F | -→F | -→- | F→F |

### a2 Floating Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | -→F | -→F | -→F |
| g2 Roundhouse | -→- | -→- | F→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | -→F | -→F | -→F |
| g4 Tornado Kick | F→F | F→F | -→F | -→F | -→F | -→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→- | F→- | F→F | -→- | F→F | F→F |
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
| a2 Floating Kick | -→- | -→F | -→- | F→- | F→F | F→F |
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
| a2 Floating Kick | F→- | -→- | -→- | F→- | -→- | -→F |
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
| a2 Floating Kick | -→- | F→F | -→- | -→F | F→- | F→- |
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
| a2 Floating Kick | -→- | -→F | -→- | F→F | F→F | -→F |
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
| a2 Floating Kick | -→F | -→- | -→F | -→- | F→F | F→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

## Self-play telemetry diff

| stat | base | candidate | Δ |
|---|---|---|---|
| hit rate | 48.7% | 49.5% | +0.82pp |
| whiff rate | 51.3% | 50.5% | -0.82pp |
| avg combo length | 2.14 | 2.16 | +0.01 |
| max combo length | 4 | 4 | 0 |
| damage / match | 363.85 | 270.5 | -93.35 |
| damage / stock | 60.64 | 45.08 | -15.56 |
| wins (bot A) | 1 | 1 | 0 |
| wins (bot B) | 0 | 2 | +2 |
| draws | 19 | 17 | -2 |
| avg match duration (s) | 10775 | 10258 | -517 |
| max match duration (s) | 10800 | 10800 | 0 |
| total swings | 3877 | 4021 | +144 |
| total hits | 1888 | 1991 | +103 |
| total whiffs | 1989 | 2030 | +41 |
| total damage | 13026 | 13890 | +864 |

| move | swings b→c | hits b→c | whiffs b→c | hit% b→c |
|---|---|---|---|---|
| a1 Double Punch | 370→375 | 5 | 365→370 | 1.35→1.33 |
| a2 Floating Kick | 372→332 | 106→71 | 266→261 | 28.49→21.39 |
| a3 High Kick | 115→124 | 0 | 115→124 | 0 |
| a4 Air Tornado | 402→332 | 0 | 402→332 | 0 |
| g1 Low Kick | 495→498 | 481 | 14→17 | 97.17→96.59 |
| g2 Roundhouse | 562→618 | 79→71 | 483→547 | 14.06→11.49 |
| g3 Roundhouse | 567→598 | 423→440 | 144→158 | 74.6→73.58 |
| g4 Tornado Kick | 994→1144 | 794→923 | 200→221 | 79.88→80.68 |

