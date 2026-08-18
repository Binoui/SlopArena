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
scripts/move-data.sh <char> --json report.json --html report.html   # visual report
scripts/move-data.sh <char> --truecombos --di --html report.html    # + true-combo graph + DI escape-space
scripts/move-data.sh <char> --reach --html report.html              # + authored hitbox reach chart
```

- `<char>`: `fightguy` (default) | `kistu`. `manki`/`nilus` resolve but produce empty reports until their
  kits are Melee-converted to Custom-knockback normals.
- Default markdown output: `docs/generated/<char>-move-data.md`.
- Collection: every `HitboxEvent` from slots 1–4 + air 1–4 (first stage only), **regardless of knockback
  profile**. Launch resolves as: Custom/Adaptive use their authored base/growth (Adaptive's authored angle
  is used as a representative), named profiles resolve from the `KnockbackProfile` table. Frame data is
  always exact.

## Visual report (`--json` / `--html`)

The tool also emits a **lossless JSON** report and a **self-contained HTML** visual report (no external
deps). The JSON is a per-tick dump (100s of KB–low MB) — **gitignored**, regenerate on demand rather than
committing; the `.html` and `.md` renderings of the same data are small enough to commit under
`docs/generated/`.

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

## True-combo reachability (`--truecombos`)

Freeform true-combo graph — no scripted strings, pure reachability. For every normal × hit state
(grounded / airborne victim) × victim %, the tool runs the **real sim**: the starter connects through the
actual hitbox path (auto-calibrated placement), then a greedy chase presses each follow-up at the earliest
legal frame (the sim's IASA early-out, plus in-reach prediction). An edge is **true** iff the follow-up's
damage lands while the victim is still in hitstun; `false` = it landed after stun expired (opponent
actionable); `-` = never connected within 2400 ticks.

- **Window tightness** per edge per %: `sim stun − (recovery + landing lag + jump squat + follow-up
  trigger)`. Positive = frame-true on paper; travel + hitstop make reality ≤ paper.
- **Combo density**: total true links per character per % (all starters × follow-ups × hit states) — the
  tuning target for "too many true combos" vs "too few".
- **Hitstun reality (important):** the sim derives hitstun from launch speed — `0.5 ×` the unscaled KB
  magnitude (`Simulation.ApplyKnockback`); the authored `StunTicks` is a zero/nonzero gate only. With
  `KbScaleFactor = 0.14`, any launch that stuns past the attacker's recovery+startup carries the victim
  beyond hitbox reach (~1.3 m) before a follow-up can activate. **Finding (2026-08-17): both FightGuy and
  Kistu currently have zero true combos at 0–150%** — the only paper-true edge (g1 → g1, +1 at 0%) is
  killed by travel. This is the current tuning's answer to "are there too many true combos?": none are
  structurally possible; combos are reads/movement, by construction.
- The greedy chase is a heuristic — `false` vs `-` granularity can miss a human's chase, but a `true`
  verdict is solid (it requires a real in-stun connect).

Output: JSON/HTML sections (per-starter reachability tables + density summary), plus a markdown section on
the default path. Flags compose with `--json`/`--html`.

## DI escape-space (`--di`)

How much a victim can bend each send with DI. Each trajectory is re-run with the victim holding each of
the four stick directions during hitstun — `in` (MoveY −1, toward the attacker / opposite the launch
axis), `away` (MoveY +1), `up` (MoveX +1), `down` (MoveX −1), the sim's DIX/DIY convention. The launch is
rotated by the sim's real `Simulation.ApplyDirectionalInfluence` (18° cap, Melee sin² curve — perpendicular
holds bend most; along-axis holds only give the expiry ASDI push) and the stick stays held through stun.

- **Escape magnitude** = max launch-vector deviation across the four holds (degrees). Low = DI-resistant
  (reliable combo/kill tool); high = DI-bendable (escapable). The deviation follows the sim curve: for a
  horizontal launch it is `18° × cos(elevation)` (e.g. 16.7° at a 22° launch).
- Output: the four variant arcs overlaid on the knockback-shape gallery (thin colored arcs, baseline stays
  solid blue), per-figure max deviation, and a markdown table on the default path.

## Authored hitbox reach (`--reach`)

Deterministic, per-move answer to "what does this move cover, and where are the gaps in the kit?" — the
authored complement to the empirical heat spots. For every collected normal (slots g1–g4, a1–a4, first
stage, every `HitboxEvent`), the tool resolves the **real sim hitbox volumes** over the active frames and
renders a side-view overlay per move, a range ladder, coverage gaps (whiff zones between moves), and
uncovered height bands.

- **Geometry**: `HitboxGeometry.ResolvePositions` per active tick (`AttackElapsedTicks = trigger + t`,
  `t ∈ [0, duration)`) at the grounded origin frame — character at `(0, CapsuleHeight/2, 0)`, yaw 0, so
  feet are at y=0 and forward is +Z. Same function `ServerAbility.SpawnHitbox` uses; baked skeleton used
  when present, entity-relative fallback otherwise. Authored geometry only — no buff bonuses.
- **Bands**: thirds of `CapsuleHeight` from the feet — low `[0, H/3)`, mid `[H/3, 2H/3)`, high
  `[2H/3, H + 0.5]` (0.5 m headroom so above-head coverage counts as high). **Reach** = max forward (Z)
  extent of the side-view envelope (X flattened) over the bands.
- **Gaps**: per band, per 0.1 m height row, the covered intervals across moves clamp to `[0, maxReachAtY]`
  (the kit's max reach at that height); holes are whiff zones. Consecutive rows whose extent agrees merge
  into one gap. **Uncovered bands** = height bands no normal reaches at all.
- Output: lossless JSON (`reach` node: per-hit capsules with per-tick endpoints/radius, band extents,
  gaps, uncovered bands) + a self-contained HTML section (side-view SVGs, range ladder, gap list), plus a
  markdown section on the default path. Flags compose with `--json`/`--html`.

```bash
scripts/move-data.sh kistu --reach --html docs/generated/kistu-move-data.html
```

**Reading the ladder**: reach sorts the kit's normals by how far forward they extend — the answer to
"my move whiffs at this spacing" and "what do I use to poke at 1.2 m vs 1.5 m". Multi-hit moves get one
row per hit (their capsules differ). A `—` band cell = the move never covers that height; a gap line
like `- high @ 2.1–2.3 m: nothing covers 0.0–0.8 m` means the kit's high-band coverage starts late
(above-head hits only connect close-in) — reads as an anti-air weakness at range.

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
