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
- `fightguy --shape [step]` — knockback feel surface: sampled launch arcs (height/travel/
  V/phase, hit-indexed) for every hitbox at every %, default step 12 ≈ 0.2s
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

Post air-knockback pass (2026-08-15) and g3 70° launcher fix. Full per-%-rows live in
`docs/generated/fightguy-move-data.md` (+ `fightguy-knockback-shapes.txt`); summary:

| move | angle/base/growth | @0% | @150% |
|---|---|---|---|
| g1 jab | 30 / 4 / 20 | KV 3.5, apex 0.4m | 7.7, 2.2m |
| g2 roundhouse | 28 / 6 / 32 | KV 5.8, apex 1.1m | 12.5, 5.4m |
| g3 uppercut | **70** / 6 / 26 (was 82° dead-vertical) | KV 4.9, apex 2.0m (launcher) | 10.4, 9.2m |
| g4 tornado | 40 / 4+2 / 22+12 | KV 4.0, apex 0.8m | 8.6, 3.7m |
| a1 DP h1/h2 | **55**/5/24, **45**/7/30 (was 75°/60°) | KV 4.2/5.5, drift 0.6/1.2m | 9.2/11.8, drift 2.9/5.8m |
| a2 FK sweet/weak | **35**/7/36, **30**/2/22 | KV 6.6/3.6, drift 2.1/0.6m | 14.2/8.2, drift 9.7/3.5m |
| a3 high kick | **25**/7/36 (was 30°) | KV 6.8, drift 2.5m | 14.4, drift 11.1m |
| a4 tornado | **35**/4/24, ender **25**/4/20 (was 40°, ender 2/12) | KV 4.3/3.5, drift 0.9/0.6m | 9.3/7.7, drift 4.2/3.2m |

Air pass goal met: all aerials carry more horizontally and pop less vertically; a4's final
hit is a real launcher (was the weakest hit). Ground normals + ground Slot4 tornado untouched.

## Feel tooling added this pass

- Move-data report gains **`adv` / `advL`** columns: on-hit frame advantage (ticks) =
  `stun − (IASA − trigger)`, with a landed-follow-up variant paying `LandingLagTicks` for
  aerials (AC windows / aerial chase skip it). Hitstop freezes both players so it cancels
  out. This is the metric that tells you whether a hit lets you press again.
- **`--shape [step]`** mode (default 12 ≈ 0.2s): sampled launch arcs (height/travel/V/phase)
  for every hitbox at every %, hit-indexed — the combo-free feel surface.
- **Ability Lab** (Unity): knockback trajectory preview (victim-% slider, per-hitbox picker,
  cyan hitstun → blue flight → white apex → red landing) + editable angle/base/growth fields.

## Next steps (updated)

1. ~~Blast zones~~ — **done** (commit `053d279`).
2. ~~Growth-curve steepening~~ — **done** (g1-4 + a1-4; Melee 1:4-6 shape applied).
3. **Remaining feel tuning**: kill-% drama (high-% curve still soft vs Melee's kill moves),
   mid-% timing windows (g3→a3 connect only 30/60), a1 reach decision (double punch reach).
4. Later: DI/SDI victim probes; dash-startup authoring; Manki/Nilus feel pass with the new
   `--shape` tool once FightGuy feel is locked.

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
