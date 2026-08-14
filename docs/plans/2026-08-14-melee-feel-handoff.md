# Handoff — melee feel pass, tooling, and the launch-path bug (2026-08-14)

State after the session that made knockback feel right for the first time. Read this
before touching knockback, the move-data tool, or the hit resolution.

## What is live now (all in `src/Shared/`)

| Knob | Value | Where |
|---|---|---|
| Launch scale | `KbScaleFactor = 0.14` (velocity-only — hitstun computed from the UNSCALED magnitude, so combo math is preserved) | `Simulation.cs` |
| Post-hitstun flight | gravity `14` m/s², horizontal friction `10` (ADR-0019 law B; raised from 8 — launches dropped too slowly) | `Simulation.cs` (`FlightGravity`, `FlightFriction`, public consts) |
| Hitstop | ADR-0019: `min(12, (int)(dmg/3 + 6))` (old ADR-0012 extras removed) | `ServerSimulation.ComputeHitstopTicks` |
| Hitstun | `(int)(0.5 × raw magnitude)` — never scaled | `Simulation.ApplyKnockback` |
| Hitstun integration | single `KV·dt` per tick (the old double integration doubled travel) | `Simulation.ProcessHitstun` |
| Flight regime | `CharacterState.InPostHitstunFlight` — cleared on landing, jump, aerial, ability | `CharacterState.cs`, `ServerSimulation` |

**Critical fix (the whole saga):** `ResolveHits` decides between the formula path
(scaled) and the force path (unscaled) with
`hookSuppliedForce = kbForce != kbForceDefault` — where `kbForceDefault` is captured
ONCE before the ability hook. It must NEVER be a recomputed expression: the Unity
editor JIT evaluates float math in 80-bit x87 precision while .NET uses 32-bit SSE —
a 1-ULP difference made every hit look "overridden" and take the unscaled force path
(~16 m/s launches at 0%, `KbScaleFactor` dead, hitstun ~half). The tool and all .NET
tests were green the whole time. **Rule: never compare a stored float to a recomputed
expression.**

Fixed-strength tools deliberately exempt from the scale (`applyScale: false`):
defensive burst shove and Nilus NetherGrasp grab.

## The tuning loop (feel pass)

1. Edit the knob (scale constant, gravity, or a move's base/growth/angle in
   `FightGuyData.cs`).
2. `dotnet build src/Shared/ --nologo` (copies the DLL to Unity Plugins).
3. `./scripts/move-data.sh fightguy` — prints + writes `docs/generated/fightguy-move-data.md`
   (frame data, trajectories at 0/30/60/90/120/150%, combo matrix, movement probes,
   pipeline parity).
4. `./scripts/move-data.sh fightguy --parity` — real-path vs direct-formula drift check.
5. In-game: the `[Launch] applied KV=… hitstun=…` log (TrainingMatch, once per hit)
   must match the report row for that %. If it doesn't, the game runs different
   physics than the tool — stop tuning and find out why.

**% convention:** rows say the victim's damage BEFORE the hit; the launch is computed
at `% + damage` (the game applies damage first — verified by the parity section).

## Tool (tools/MoveDataReport)

- `scripts/move-data.sh fightguy [--pcts 0,30,...] [--out path]` — full report
- `fightguy --parity` — pipeline parity (16 cells: 8 slots × 0%/150%)
- `fightguy --traj <slot>` — per-tick CSV of a launch at 10% steps
- `fightguy --pipe` — single-slot full-pipeline launch diagnostic
- `fightguy --dll <path>` — run a SlopArena.Shared.dll file in isolation (proves what
  the FILE does regardless of what the editor loaded)
- Probes verdicts: **T** frame-true, **C** connected while victim airborne (real
  juggle/read), **L** after landing (neutral, not a combo), **-** no connect.
  Movement probe policy: stop-before-press (lunge overshoot), auto-calibrated starter
  placement (grid search — stale positions break; test comments with trigger ticks
  can drift from reality, the calibrator is the source of truth).

## Current feel numbers (0.14 / g14, post-hit %)

| move | @0% | @150% |
|---|---|---|
| g1 jab | KV 4.1, apex 0.6m, 0.62s | 7.4, 2.0m |
| g2 roundhouse | KV 6.0, apex 1.2m | 11.5, 4.5m |
| g3 uppercut | KV 4.9, apex 2.1m (launcher) | 9.5, 8.1m |
| a3 high kick | KV 6.5, apex 1.5m | 11.9, 5.3m |

Combo probes at these values: g3→a2 juggles at every %; g3→g2 at 30-120 (T at 150);
g1→g2 T from 60%; g3→a1 and g3→a3 are timing/reach windows. Known weak spot:
high-% kill drama is soft (flat %-curve) — see next steps.

## Next steps (in order)

1. **Blast zones** — engine has only void death (`PY < KillHeight`), no top/side
   kill lines. Kills can't happen; kill-% tuning impossible until added.
2. **a1 reach decision** — a1 (Double Punch) can't juggle anything (probe shows L
   everywhere): hitbox reach too short. Design call: extend reach or accept as a
   non-juggle tool.
3. **Growth-curve steepening (Melee shape)** — base:growth is ~1:2 here vs ~1:5-8 in
   Melee; the % curve is too flat: low-% pops and high-% kills can't both be right
   with one linear scale. Raise growth, lower base per move — also shortens low-%
   hitstun ("not every hit combos" gets truer).
4. **Mid-% timing windows** — g3→a3 C only at 30/60: press-window tuning.
5. Later: DI/SDI victim probes; dash-startup authoring (adds PARTIAL column to the
   matrix); flight-gravity re-check after playtesting (still ~1s float at low %).

## Test suite (expected red)

44 failures, ALL pre-existing collateral — do not chase:
- FightGuy goldens ×10 + other goldens (Manki/Kistu/Nilus LMB, FacingSnap ×2, TargetLock)
  — pin pre-overhaul numbers. **Regenerate goldens LAST**, after the kit is locked.
- Nilus drag/detonate/yank ×9, death/respawn ×7, CycloneKick ×2, IasaTests,
  Manki RMB, BurstTests — normals-overhaul collateral.
- `LandingLagTests.cs` (user's own file) — untouched.

Suite commands: `dotnet build src/Shared/ --nologo`, `dotnet test tests/Shared.Tests/`.

## MCP tooling (this session)

- `scripts/mcp-script.sh [-b] <file.cs>` — run a C# probe in the editor (class must
  be named `Script`; `-b` = method body). Write the file with the Write tool.
- `scripts/mcp-run.sh <tool> '<json-args>'` — any tool, unwrapped result.
- `scripts/mcp-unwrap.py` — SSE unwrapper.
- Editor precheck + `MCP_TIMEOUT` env (default 60s) built in. A hang = editor busy
  (compiling/dialog), not a broken tool.
- Recipes + gotchas in `.omp/skills/unity-mcp-gamedev/SKILL.md` → "Live-sim
  diagnostics": reading live sim state via `_bridge`, baked-data loading, base-class
  reflection walk, namespace shadowing (`SlopArena.Client.Simulation` shadows the
  type), the float rule.

## Git

Single commit on `main` covering the whole pass. Convention: one squash commit per
branch, `<type>(<scope>): <imperative>`. Do not push without explicit permission.
