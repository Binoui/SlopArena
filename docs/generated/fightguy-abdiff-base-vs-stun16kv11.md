# FightGuy — tuning A/B diff: base vs stun16kv11

Tool 1.0.0 · commit `56383b77fabad7093fccd005991dbd20af6afd97` · seed 20260817 (same both sides) · 20 matches · percents 0, 30, 60, 90, 120, 150.
Baseline **base**: base — shipped — stun 0.7×(mag+20), KV×0.11. Candidate **stun16kv11**: stun16kv11 — Melee-ish ratio — stun 0.8×mag, KV×0.11.

- **Move data**: 11/11 moves changed.
- **Combo links**: **+0 gained, -0 lost** (true-combo edges across starters × hit states × %).
- **Telemetry**: 14/15 stats moved (same seed → tuning effect only).

## Move-data diff

| move | % | KV b→c | stun b→c | adv b→c | apex b→c | kill% b→c |
|---|---|---|---|---|---|---|
| g1 Low Kick | 0% | 2.77 | 31→20 | 22→11 | 0.2→0.12 |  |
| g1 Low Kick | 30% | 3.43 | 35→24 | 26→15 | 0.27→0.19 |  |
| g1 Low Kick | 60% | 4.09 | 40→29 | 31→20 | 0.38→0.27 |  |
| g1 Low Kick | 90% | 4.75 | 44→34 | 35→25 | 0.48→0.37 |  |
| g1 Low Kick | 120% | 5.41 | 48→39 | 39→30 | 0.6→0.49 |  |
| g1 Low Kick | 150% | 6.07 | 52→44 | 43→35 | 0.74→0.62 |  |
| g2 Roundhouse | 0% | 4.55 | 42→33 | 22→13 | 1.25→1 |  |
| g2 Roundhouse | 30% | 5.61 | 49→40 | 29→20 | 1.82→1.5 |  |
| g2 Roundhouse | 60% | 6.66 | 56→48 | 36→28 | 2.49→2.16 |  |
| g2 Roundhouse | 90% | 7.72 | 63→56 | 43→36 | 3.26→2.93 |  |
| g2 Roundhouse | 120% | 8.77 | 69→63 | 49→43 | 4.08→3.75 |  |
| g2 Roundhouse | 150% | 9.83 | 76→71 | 56→51 | 5.06→4.75 |  |
| g3 Roundhouse | 0% | 4.55 | 42→33 | 22→13 | 1.25→1 |  |
| g3 Roundhouse | 30% | 5.61 | 49→40 | 29→20 | 1.82→1.5 |  |
| g3 Roundhouse | 60% | 6.66 | 56→48 | 36→28 | 2.49→2.16 |  |
| g3 Roundhouse | 90% | 7.72 | 63→56 | 43→36 | 3.26→2.93 |  |
| g3 Roundhouse | 120% | 8.77 | 69→63 | 49→43 | 4.08→3.75 |  |
| g3 Roundhouse | 150% | 9.83 | 76→71 | 56→51 | 5.06→4.75 |  |
| g4 Tornado Kick | 0% | 3.14 | 33→22 | -5→-16 | 1.21→0.84 |  |
| g4 Tornado Kick | 30% | 3.87 | 38→28 | 0→-10 | 1.73→1.32 |  |
| g4 Tornado Kick | 60% | 4.59 | 43→33 | 5→-5 | 2.35→1.86 |  |
| g4 Tornado Kick | 90% | 5.32 | 47→38 | 9→0 | 3.01→2.5 |  |
| g4 Tornado Kick | 120% | 6.05 | 52→43 | 14→5 | 3.81→3.23 |  |
| g4 Tornado Kick | 150% | 6.77 | 57→49 | 19→11 | 4.7→4.12 |  |
| a1 Double Punch | 0% | 3.3 | 35→24 | 12→1 | 1.77→1.28 |  |
| a1 Double Punch | 30% | 4.09 | 40→29 | 17→6 | 2.55→1.94 |  |
| a1 Double Punch | 60% | 4.89 | 45→35 | 22→12 | 3.47→2.81 |  |
| a1 Double Punch | 90% | 5.68 | 50→41 | 27→18 | 4.53→3.83 |  |
| a1 Double Punch | 120% | 6.47 | 55→47 | 32→24 | 5.73→5.02 |  |
| a1 Double Punch | 150% | 7.26 | 60→52 | 37→29 | 7.06→6.27 |  |
| a1 Double Punch (hit 2) | 0% | 4.29 | 41→31 | 28→18 | 2.33→1.82 |  |
| a1 Double Punch (hit 2) | 30% | 5.28 | 47→38 | 34→25 | 3.33→2.77 |  |
| a1 Double Punch (hit 2) | 60% | 6.27 | 53→45 | 40→32 | 4.51→3.92 |  |
| a1 Double Punch (hit 2) | 90% | 7.26 | 60→52 | 47→39 | 5.95→5.26 |  |
| a1 Double Punch (hit 2) | 120% | 8.25 | 66→60 | 53→47 | 7.49→6.9 |  |
| a1 Double Punch (hit 2) | 150% | 9.24 | 72→67 | 59→54 | 9.2→8.66 |  |
| a2 Floating Kick | 0% | 5.19 | 46→37 | 17→8 | 1.06→0.86 |  |
| a2 Floating Kick | 30% | 6.37 | 54→46 | 25→17 | 1.54→1.32 |  |
| a2 Floating Kick | 60% | 7.56 | 62→54 | 33→25 | 2.11→1.85 |  |
| a2 Floating Kick | 90% | 8.75 | 69→63 | 40→34 | 2.73→2.5 |  |
| a2 Floating Kick | 120% | 9.94 | 77→72 | 48→43 | 3.47→3.26 |  |
| a2 Floating Kick | 150% | 11.13 | 84→80 | 55→51 | 4.26→4.06 |  |
| a2 Floating Kick (hit 2) | 0% | 2.85 | 32→20 | 8→-4 | 0.39→0.25 |  |
| a2 Floating Kick (hit 2) | 30% | 3.58 | 36→26 | 12→2 | 0.56→0.41 |  |
| a2 Floating Kick (hit 2) | 60% | 4.3 | 41→31 | 17→7 | 0.78→0.59 |  |
| a2 Floating Kick (hit 2) | 90% | 5.03 | 46→36 | 22→12 | 1.03→0.81 |  |
| a2 Floating Kick (hit 2) | 120% | 5.76 | 50→41 | 26→17 | 1.28→1.06 |  |
| a2 Floating Kick (hit 2) | 150% | 6.48 | 55→47 | 31→23 | 1.6→1.37 |  |
| a2 Floating Kick (hit 3) | 0% | 2.85 | 32→20 | 2→-10 | 0.39→0.25 |  |
| a2 Floating Kick (hit 3) | 30% | 3.58 | 36→26 | 6→-4 | 0.56→0.41 |  |
| a2 Floating Kick (hit 3) | 60% | 4.3 | 41→31 | 11→1 | 0.78→0.59 |  |
| a2 Floating Kick (hit 3) | 90% | 5.03 | 46→36 | 16→6 | 1.03→0.81 |  |
| a2 Floating Kick (hit 3) | 120% | 5.76 | 50→41 | 20→11 | 1.28→1.06 |  |
| a2 Floating Kick (hit 3) | 150% | 6.48 | 55→47 | 25→17 | 1.6→1.37 |  |
| a3 High Kick | 0% | 5.34 | 47→38 | 19→10 | 0.89→0.72 |  |
| a3 High Kick | 30% | 6.53 | 55→47 | 27→19 | 1.28→1.09 |  |
| a3 High Kick | 60% | 7.71 | 63→56 | 35→28 | 1.74→1.55 |  |
| a3 High Kick | 90% | 8.9 | 70→64 | 42→36 | 2.24→2.05 |  |
| a3 High Kick | 120% | 10.09 | 78→73 | 50→45 | 2.83→2.66 |  |
| a3 High Kick | 150% | 11.28 | 85→82 | 57→54 | 3.46→3.34 |  |
| a4 Air Tornado | 0% | 3.38 | 35→24 | 7→-4 | 1.22→0.86 |  |
| a4 Air Tornado | 30% | 4.17 | 40→30 | 12→2 | 1.74→1.34 |  |
| a4 Air Tornado | 60% | 4.96 | 45→36 | 17→8 | 2.35→1.93 |  |
| a4 Air Tornado | 90% | 5.76 | 50→41 | 22→13 | 3.06→2.56 |  |
| a4 Air Tornado | 120% | 6.55 | 55→47 | 27→19 | 3.85→3.35 |  |
| a4 Air Tornado | 150% | 7.34 | 60→53 | 32→25 | 4.74→4.25 |  |

## True-combo graph diff

Verdict per starter × follow-up × %: `-` never connected, `F` landed after stun, `T` true combo. `a→b` = transition.

### g1 Low Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | T→T (-11) | T→T (-11) | F→F | F→F | F→F | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Double Punch | T→T (-11) | T→T (-11) | T→T (-11) | T→T (-10) | F→F | F→F |
| a2 Floating Kick | F→F | F→F | F→F | -→- | -→- | F→F |
| a3 High Kick | T→T (-11) | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Roundhouse — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→- | F→F | F→F | F→F | -→- | F→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g3 Roundhouse — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→- | -→- | -→- | -→- | F→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g4 Tornado Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→- |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | -→- | -→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→- | -→- | F→F | F→F | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### g4 Tornado Kick — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→- |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | -→- | -→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→- | -→- | F→F | F→F | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a1 Double Punch — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | -→- | F→F | F→F | -→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | -→- | F→F | F→F | -→- |
| g4 Tornado Kick | F→F | F→F | -→- | F→F | F→F | -→- |
| a1 Double Punch | F→F | -→- | F→F | F→F | -→- | F→F |
| a2 Floating Kick | -→- | -→- | -→- | -→- | -→- | F→F |
| a3 High Kick | F→F | -→- | F→F | F→F | -→- | F→F |
| a4 Air Tornado | F→F | -→- | F→F | F→F | -→- | F→F |

### a1 Double Punch — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | -→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | -→- |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | F→F | -→- |
| a1 Double Punch | F→F | -→- | F→F | -→- | -→- | F→F |
| a2 Floating Kick | F→F | -→- | -→- | -→- | -→- | F→F |
| a3 High Kick | F→F | -→- | F→F | -→- | -→- | F→F |
| a4 Air Tornado | F→F | -→- | F→F | -→- | -→- | F→F |

### a2 Floating Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | -→- | -→- | -→- |
| g2 Roundhouse | -→- | -→- | F→F | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | -→- | -→- | -→- |
| g4 Tornado Kick | F→F | F→F | -→- | -→- | -→- | -→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→- | F→F | -→- | -→- | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a2 Floating Kick — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | F→F | -→- |
| g3 Roundhouse | F→F | F→F | F→F | F→F | F→F | F→F |
| g4 Tornado Kick | F→F | F→F | F→F | F→F | -→- | -→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→- | -→- | -→- | F→F | F→F | F→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a3 High Kick — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | -→- | F→F | F→F | F→F | F→F | -→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | -→- | F→F | F→F | F→F | F→F | -→- |
| g4 Tornado Kick | -→- | F→F | F→F | F→F | F→F | -→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | F→F | -→- | -→- | F→F | F→F | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | -→- | F→F | F→F | F→F | F→F | F→F |

### a3 High Kick — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | -→- | -→- | -→- | -→- | -→- | -→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g4 Tornado Kick | -→- | -→- | -→- | -→- | -→- | -→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→- | F→F | -→- | F→F | F→F | F→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a4 Air Tornado — grounded hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | F→F | -→- | F→F | F→F |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | F→F | -→- | F→F | F→F |
| g4 Tornado Kick | F→F | F→F | F→F | -→- | F→F | F→F |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→- | -→- | -→- | F→F | F→F | -→- |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

### a4 Air Tornado — airborne hit (+0/-0)

| follow-up | 0 | 30 | 60 | 90 | 120 | 150 |
|---|---|---|---|---|---|---|
| g1 Low Kick | F→F | F→F | -→- | -→- | -→- | -→- |
| g2 Roundhouse | -→- | -→- | -→- | -→- | -→- | -→- |
| g3 Roundhouse | F→F | F→F | -→- | -→- | -→- | -→- |
| g4 Tornado Kick | F→F | F→F | -→- | -→- | -→- | -→- |
| a1 Double Punch | F→F | F→F | F→F | F→F | F→F | F→F |
| a2 Floating Kick | -→- | -→- | -→- | -→- | F→F | F→F |
| a3 High Kick | F→F | F→F | F→F | F→F | F→F | F→F |
| a4 Air Tornado | F→F | F→F | F→F | F→F | F→F | F→F |

## Self-play telemetry diff

| stat | base | candidate | Δ |
|---|---|---|---|
| hit rate | 52.1% | 52.8% | +0.67pp |
| whiff rate | 47.9% | 47.2% | -0.67pp |
| avg combo length | 2.14 | 2.18 | +0.05 |
| max combo length | 3 | 4 | +1 |
| damage / match | 248.2 | 258.4 | +10.2 |
| damage / stock | 41.37 | 43.07 | +1.7 |
| wins (bot A) | 2 | 1 | -1 |
| wins (bot B) | 2 | 1 | -1 |
| draws | 16 | 18 | +2 |
| avg match duration (s) | 10335 | 10546 | +211 |
| max match duration (s) | 10800 | 10800 | 0 |
| total swings | 3679 | 4009 | +330 |
| total hits | 1917 | 2116 | +199 |
| total whiffs | 1762 | 1893 | +131 |
| total damage | 13507 | 14811 | +1304 |

| move | swings b→c | hits b→c | whiffs b→c | hit% b→c |
|---|---|---|---|---|
| a1 Double Punch | 401→381 | 4 | 397→377 | 1→1.05 |
| a2 Floating Kick | 410→407 | 115→95 | 295→312 | 28.05→23.34 |
| a3 High Kick | 117→104 | 0 | 117→104 | 0 |
| a4 Air Tornado | 339→387 | 1→0 | 338→387 | 0.29→0 |
| g1 Low Kick | 404→507 | 391→493 | 13→14 | 96.78→97.24 |
| g2 Roundhouse | 507→593 | 259→252 | 248→341 | 51.08→42.5 |
| g3 Roundhouse | 509→576 | 371→429 | 138→147 | 72.89→74.48 |
| g4 Tornado Kick | 992→1054 | 776→843 | 216→211 | 78.23→79.98 |

