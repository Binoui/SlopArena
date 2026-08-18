# Movement data sheet — measured from the real sim (issue #150)

> Generated 2026-08-18 08:43 UTC · scripted inputs on the real ServerSimulation (60 Hz tick). 
> Run: hold right from standstill. Dash: one dash press. Jump: full jump (held past the short-hop 
> window). Double jump: jump edge at first apex. Drift: stick held through the full hop. 
> Fall: spawned airborne at 50 m (float window skipped) — natural drop vs hold-Down fast fall. 
> Stop: release at cruise. 
> Values are measured (effective behavior, incl. rush kick-off, float windows, caps), not authored constants.

## Comparison

| metric | Manki | FightGuy | Kistu | Nilus |
|---|---|---|---|
| Run max speed (m/s) | 12.0 (12) | 14.0 (14) | 15.0 (15) | 13.0 (13) |
| Run time-to-max | instant (rush kick-off) | instant (rush kick-off) | instant (rush kick-off) | instant (rush kick-off) |
| Dash duration (ticks) | 15 (15) | 20 (20) | 16 (16) | 15 (15) |
| Dash distance (m) | 4.67 | 6.33 | 6.00 | 4.90 |
| Dash actionable (tick) | 15 | 20 | 16 | 15 |
| Jump apex (m) | 1.35 | 1.90 | 2.24 | 2.02 |
| Jump time-to-apex (ticks) | 16 | 18 | 20 | 20 |
| Jump airtime (s) | 0.53 | 0.63 | 0.68 | 0.67 |
| Full-hop drift (m) | 2.37 | 3.47 | 4.34 | 3.63 |
| Running jump distance (m) | 4.23 | 5.69 | 6.63 | 5.41 |
| Double-jump apex (m) | 2.19 | 3.09 | 3.65 | 3.29 |
| Double-jump airtime (s) | 0.82 | 0.95 | 1.05 | 1.03 |
| Air drift cap (m/s) | 6.5 (6) | 7.5 (8) | 8.5 (8) | 7.0 (7) |
| Fall max speed (m/s) | 45 (45) | 48 (48) | 48 (48) | 46 (46) |
| Fall time-to-max (s) | 1.28 | 1.32 | 1.32 | 1.35 |
| Fall 50 m descent (s) | 1.72 | 1.67 | 1.67 | 1.73 |
| Fast fall (m/s) | 54 (54) | 58 (58) | 58 (58) | 55 (55) |
| Fast-fall 50 m descent (s) | 0.92 | 0.85 | 0.85 | 0.90 |
| Stop time (s) | 0.32 | 0.38 | 0.40 | 0.35 |
| Stop distance (m) | 1.71 | 2.38 | 2.76 | 2.03 |

## Manki

- **Run**: 12.0 m/s (authored 12) — instant cruise — Rush kick-off sets RunSpeed on the first tick (no ramp); the soft-start accel only shows after a turnaround skid
- **Dash**: 15 ticks, 4.67 m, actionable on tick 15 (hard stop; authored 20 m/s for 15 ticks)
- **Jump**: apex 1.35 m at 16 ticks, airtime 0.53 s, full-hop drift 2.37 m; running jump carries 4.23 m
- **Double jump**: second apex 2.19 m, total airtime 0.82 s
- **Air drift**: speed cap 6.5 m/s (authored 6.5)
- **Fall** (50 m drop): max 45 m/s (authored 45), reached 1.28 s into the drop, descent 1.72 s; fast fall 54 m/s (authored 54), 50 m descent 0.92 s
- **Stop** (cruise → standstill): 0.32 s, 1.71 m

## FightGuy

- **Run**: 14.0 m/s (authored 14) — instant cruise — Rush kick-off sets RunSpeed on the first tick (no ramp); the soft-start accel only shows after a turnaround skid
- **Dash**: 20 ticks, 6.33 m, actionable on tick 20 (hard stop; authored 20 m/s for 20 ticks)
- **Jump**: apex 1.90 m at 18 ticks, airtime 0.63 s, full-hop drift 3.47 m; running jump carries 5.69 m
- **Double jump**: second apex 3.09 m, total airtime 0.95 s
- **Air drift**: speed cap 7.5 m/s (authored 7.5)
- **Fall** (50 m drop): max 48 m/s (authored 48), reached 1.32 s into the drop, descent 1.67 s; fast fall 58 m/s (authored 58), 50 m descent 0.85 s
- **Stop** (cruise → standstill): 0.38 s, 2.38 m

## Kistu

- **Run**: 15.0 m/s (authored 15) — instant cruise — Rush kick-off sets RunSpeed on the first tick (no ramp); the soft-start accel only shows after a turnaround skid
- **Dash**: 16 ticks, 6.00 m, actionable on tick 16 (hard stop; authored 24 m/s for 16 ticks)
- **Jump**: apex 2.24 m at 20 ticks, airtime 0.68 s, full-hop drift 4.34 m; running jump carries 6.63 m
- **Double jump**: second apex 3.65 m, total airtime 1.05 s
- **Air drift**: speed cap 8.5 m/s (authored 8.5)
- **Fall** (50 m drop): max 48 m/s (authored 48), reached 1.32 s into the drop, descent 1.67 s; fast fall 58 m/s (authored 58), 50 m descent 0.85 s
- **Stop** (cruise → standstill): 0.40 s, 2.76 m

## Nilus

- **Run**: 13.0 m/s (authored 13) — instant cruise — Rush kick-off sets RunSpeed on the first tick (no ramp); the soft-start accel only shows after a turnaround skid
- **Dash**: 15 ticks, 4.90 m, actionable on tick 15 (hard stop; authored 21 m/s for 15 ticks)
- **Jump**: apex 2.02 m at 20 ticks, airtime 0.67 s, full-hop drift 3.63 m; running jump carries 5.41 m
- **Double jump**: second apex 3.29 m, total airtime 1.03 s
- **Air drift**: speed cap 7.0 m/s (authored 7.0)
- **Fall** (50 m drop): max 46 m/s (authored 46), reached 1.35 s into the drop, descent 1.73 s; fast fall 55 m/s (authored 55), 50 m descent 0.90 s
- **Stop** (cruise → standstill): 0.35 s, 2.03 m

