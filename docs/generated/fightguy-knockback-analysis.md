# FightGuy knockback baseline analysis

Source command: `scripts/move-data.sh fightguy --pcts 0,30,60,90,120 --json /tmp/fightguy-move-data.json`.
The committed HTML/Markdown artifacts use the same requested buckets. This is an observation record, not a
tuning proposal.

## Objective observations

- The authored launch angles span low sends (g1 `8°`, g2 `25°`, g4 `28°`), rising sends (g3 `55°`, a1 hit 1 `55°`, a3 `65°`), and aerial follow-up angles (a1 hit 2 `45°`, a2 hit 2 `20°`, a4 `25°`).
- g2 Straight Punch rises from `0.7 m` apex and `1.29 m` travel at 0% to `2.9 m` and `4.82 m` at 120%; its hitstun expiry moves from tick `15` to `29`, and landing from tick `43` to `86`.
- g4 Double Kick has the largest grounded arc in this bucket range: apex `2.7 m` → `9.4 m`, expiry distance `3.79 m` → `13.05 m`, and landing tick `81` → `153` from 0% → 120%.
- a3 High Kick has the highest authored angle and reaches apex `2.1 m` → `8.2 m`; landing changes from tick `69` to `135`.
- g1 Low Kick remains a short, low arc: apex `0.1 m` → `0.4 m`, expiry distance `0.77 m` → `3.03 m`, and landing tick `15` → `39`.
- Grounded on-hit frame advantage rises with the simulated hitstun window. g2 changes from `-2` ticks at 0% to `+12` at 120%; g4 changes from `-20` to `+2`.
- All generated FightGuy normal parity rows are `OK` at the first and last requested buckets. The trajectories use the same `Simulation.ApplyKnockback` and `ServerSimulation` tick path as the report parity check.

## Subjective design interpretation

These values make g4 the most visibly escalating grounded send and g1 the least displacing pressure tool. The rising g3/a3 family reads as vertical conversion or recovery pressure, while the aerial rows trade landing lag against height and travel. Whether those roles are desirable depends on match pacing, stage blast zones, and player escape options; this report does not change any authored values or simulation tuning.
