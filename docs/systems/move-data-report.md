# Move Data Report

Telemetry tool for SlopArena move data: runs the **real shared sim** (ADR-0019 knockback + flight law)
for a character's Custom-knockback normal hitboxes at several victim damage percents, and prints authored
frame data + simulated per-hit trajectories (with frame advantage) + a pipeline-parity safety check.

It is the "record the data we want" answer to the workflow where a frame-data question used to spawn a
throwaway xUnit test. The sim is the same `ServerSimulation` + `Simulation.ApplyKnockback` the game server
runs, so the numbers are what the game produces — not a reimplementation.

Primary use: attack duration, active frames, frame advantage, and knockback shape/range at a glance. The
combo matrix/probes are experimental diagnostics, deliberately separated from the core report.

## Usage

```bash
scripts/move-data.sh <char> [--pcts 0,30,60,90,120,150] [--out docs/generated/<char>-move-data.md]
```

- `<char>`: `fightguy` (default) | `kistu`. `manki`/`nilus` resolve but produce empty reports until their
  kits are Melee-converted to Custom-knockback normals.
- Default markdown output: `docs/generated/<char>-move-data.md`.
- Collection: every `HitboxEvent` from slots 1–4 + air 1–4 (first stage only), **regardless of knockback
  profile**. Launch resolves as: Custom/Adaptive use their authored base/growth (Adaptive's authored angle
  is used as a representative), named profiles resolve from the `KnockbackProfile` table. Frame data is
  always exact.

## Visual report (`--json` / `--html`)

The tool also emits a **lossless JSON** report and a **self-contained HTML** visual report (no external deps,
commit under `docs/generated/`). One richer collection (per-tick arcs + KO analysis) feeds both; the markdown
path is unchanged.

```bash
scripts/move-data.sh kistu --json docs/generated/kistu-move-data.json --html docs/generated/kistu-move-data.html
```

- **`--json`** — structured report: per-move frame data, per-tick trajectory arcs, adv per move×%, kill% +
  blast clearance. The lossless source for any future renderer.
- **`--html`** — three human-readable sections:
  1. **Frame-advantage heatmap** — rows = moves, columns = victim %, cells colored green(+)→red(−) by adv
     (saturating at ±40 ticks); click a column header to sort. The g2 ramp (−15 → +24) reads at a glance.
  2. **Knockback shape gallery** — small-multiple SVG arcs (height vs horizontal travel), one per move × %,
     red apex dot, KV / apex / stun caption. The "shape" the trajectory table hides in numbers.
  3. **KO & blast clearance** — kill % (lowest victim % crossing a blast line, binary search 0→250%) plus
     progress bars for how close each move gets to the top / side blast line at the highest simulated %.

### Kill-% geometry

Kill % is arena-relative. The report's own arena (flat 200×200, no bounds) auto-resolves to sides ±∞, so
nothing ever kills on it. The tool therefore computes KO on a **Crossroads-style proxy**: flat 60×60,
`KillHeight −10`, bounds ±30 → auto top +20, sides ±40. Center launch, victim passive (no DI/SDI). The proxy
is labelled in the JSON/HTML so the assumption is explicit.

**Finding (2026-08-17):** no normal KO ≤ 250% on the proxy — the game's knockback magnitudes (apex ≤ ~9 m,
side travel ≤ ~19 m at 250%) are small vs the blast-zone distances. That is a design signal (these are combo
moves, not finishers, and/or blast zones are deep). Use **blast clearance** to read how close each move gets:
e.g. Kistu g3 Up Slash apex = 43% of the way to top blast.

## Report sections

### 1. Frame data (authored)

Pure constants from the ability spec, zero simulation:

`move | hit | trigger | active window | dmg | angle | base | growth | stun | IASA | landing lag | AC before | AC after | total duration`

Instant answer to "how long is this attack / when does it hit?" — no sim needed.

### 2. Per-hit trajectories (simulated)

One row per hitbox × per victim %, the knockback shape:

`KV m/s | hitstop | stun | adv | advL | rise@stun | drift@stun | apex | actionable tick | landed tick`

Method: launch a fresh grounded victim at the given % via the real `Simulation.ApplyKnockback` with the
hitbox's authored values (angle, base, growth, damage, stun, weight), then step the sim until landing
(cap 2400 ticks). No DI/SDI input. Hitstop is reported (`ComputeHitstopTicks`, ADR-0019:
`min(12, dmg/3 + 6)`) but not simulated — flight starts at launch. The launch is computed at
`pct + damage` (the game applies damage before the queued launch).

**`adv`** — on-hit frame advantage = `stun − (IASA − trigger)`. Positive = the attacker acts before the
victim leaves hitstun. This is the headline number for follow-up pressure.
**`advL`** (aerials) — the landed follow-up pays `LandingLagTicks` on top (SHFFL-style); land inside an AC
window (`≤ AC bef` / `≥ AC aft`) or chase with an aerial at IASA and the lag is skipped, so the true landed
number sits between `advL` and `adv`.

### 3. Pipeline parity (safety)

The report's rows launch through a direct `ApplyKnockback` call; the game launches through the real path
(input → baked-bone hitbox resolution → `ResolveHits` → hitstop queue → queued launch). This section runs
the real path for each slot's first hitbox at 0% and the last requested %, and compares applied KV/stun/apex.
Any `DIVERGE` means the game behaves differently from the report — investigate before trusting feel tuning.

This exists because the 2026-08-14 x87 float comparison bug silently scaled nothing in the game while .NET
tests were green.

## Opt-in / experimental

- `--combos` — combo matrix (no-travel bound `TA = (IASA − trigger) [+ landing lag] [+ jump squat] +
  follow-up trigger`; `TA < stun` ⇒ frame-true) + movement probes (greedy AI chase policy, real sim,
  verdicts T/C/L/-). This encodes **scripted route strings**, which contradict the freeform-combo design
  goal. Diagnostic only — do not build balance decisions on it.
- `--traj <slot>` — raw per-tick CSV launch trace for one grounded hit
  (`tick,height(m),travel(m),vY,vX,phase`).
- `--shape [step]` — knockback feel surface: the real sampled arc every `step` ticks (default 12 ≈ 0.2s)
  per hit per %, phase markers (H=hitstun / F=flight / A=apex / G=landed), plus a one-line KV + stun
  summary per block.
- `--pipe`, `--dll <path>`, `--parity` — internal diagnostics. `--dll` loads a `SlopArena.Shared.dll` in an
  isolated load context and runs its `ApplyKnockback`, proving what the file on disk does regardless of what
  the Unity editor loaded (stale-DLL detection).

## Caveats

- **Adaptive moves are approximated.** Melee-361 auto-angle (Kistu's Quick Slash / Air Slash / Reverse
  Slash) has no fixed launch angle — the real one varies with hit position. The report simulates them with
  the authored angle as a representative and tags them `adaptive`; frame data is exact. This is why a
  tagged move's direct trajectory may not match its real in-game launch.
- **Multi-hit parity false positive** — a 2-hit starter (e.g. Kistu g2 Double Slash) re-launches the victim
  with hit 2 in the pipeline run, inflating pipeline apex vs the single-hit direct row → `DIVERGE`. Matching
  KV/stun is the signal it's an artifact.
- **Manki / Nilus → empty reports** until Melee-converted. Correct, not broken.
- **Per-character combo routes** — only FightGuy has authored routes (`DefaultRoutes` in `Program.cs`). Add
  a character's designed links to `RoutesFor` to get its combo matrix.
- Hitstop reported but not simulated (flight starts at launch).
- Apex/rise/drift assume the hit connects on the first active frame.

## Flow

1. Rebuild Shared after a change: `dotnet build src/Shared/ --nologo` (DLL auto-copies to Unity Plugins).
2. `dotnet build tools/MoveDataReport/ --nologo`.
3. Run, read the markdown, sanity-check against `docs/characters/<char>.md` / the ability spec.
4. Commit regenerated `docs/generated/*.md` alongside a behavior change when it should be a changelog
   artifact. `--parity` divergence with in-game feel → check the launch-contract sentinel (see the
   `unity-mcp-gamedev` skill).

## Key files

| File | Role |
|---|---|
| `tools/MoveDataReport/Program.cs` | the tool (CLI, hit collection, sim runs, markdown) |
| `tools/MoveDataReport/MoveDataReport.csproj` | console project, `net8.0`, refs `SlopArena.Shared` |
| `scripts/move-data.sh` | wrapper (`dotnet run --project tools/MoveDataReport -- "$@"`) |
| `src/Shared/Simulation.cs` | `ApplyKnockback` (launch) |
| `src/Shared/ServerSimulation.cs` | the tick loop + `ComputeHitstopTicks` |
| `src/Shared/Characters/*Data.cs` | the authored ability specs the report reads |
| `docs/generated/*-move-data.md` | committed report artifacts |
