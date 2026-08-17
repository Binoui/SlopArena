---
name: sloparena-move-data-report
description: Record and report a character's move data — authored frame data (trigger/active/duration/IASA/landing lag), simulated per-hit trajectories with frame advantage, and knockback shape — via scripts/move-data.sh → tools/MoveDataReport. Use when the user wants attack duration, active frames, frame advantage, knockback trajectory/shape, or a move-data table pinned for a character.
triggers:
  - frame data
  - move data
  - frame advantage
  - knockback shape
  - record frame data
  - true combo
  - combo graph
  - combo density
  - DI escape
  - directional influence
---

# SlopArena Move Data Report

`scripts/move-data.sh` wraps `tools/MoveDataReport` (dotnet console, `net8.0`). It runs the **real shared sim**
(`ServerSimulation` + `Simulation.ApplyKnockback`, ADR-0019 flight law) for a character's Custom-knockback
normal hitboxes at several victim damage percents, and writes a markdown report. No xUnit, no assertion, no
golden file — pure telemetry for a human to read. Reference: `docs/systems/move-data-report.md`.

## Usage

```bash
scripts/move-data.sh <char> [--pcts 0,30,60,90,120,150] [--out docs/generated/<char>-move-data.md]
scripts/move-data.sh <char> --json report.json --html report.html   # visual report
scripts/move-data.sh <char> --truecombos --di --html report.html    # + true-combo graph + DI escape-space
```

- `<char>`: `fightguy` (default) | `kistu`. `manki`/`nilus` resolve but produce empty reports — they are not
  Melee-converted to Custom-knockback normals yet (see caveats).
- Default markdown output: `docs/generated/<char>-move-data.md`.

### Analysis modes (issue #147)

- `--truecombos` — freeform true-combo reachability graph: for each normal × hit state (grounded/airborne)
  × %, the real sim runs the starter and attempts every follow-up (greedy chase, IASA early-out press).
  **T** = follow-up landed while the victim was still in hitstun, **F** = landed after stun expired,
  `-` = never. Per-edge window tightness (`sim stun − recovery − follow-up trigger`) + per-% combo density.
  **Finding (2026-08-17): both characters currently have zero true combos** — the sim derives hitstun from
  launch speed (`0.5 ×` unscaled KV; authored `StunTicks` is a zero/nonzero gate) and `KbScaleFactor 0.14`
  carries the victim out of reach before any follow-up can activate. Combos are reads/movement, by construction.
- `--di` — DI escape-space: each trajectory re-run with the victim holding each stick direction through
  hitstun (`in`/`away` = MoveY ∓/±1 along the launch axis, `up`/`down` = MoveX ±1 perpendicular), rotated by
  the sim's real `ApplyDirectionalInfluence` (18° cap, Melee sin² curve). Max launch-vector deviation =
  escape magnitude — low = DI-resistant (reliable kill tool), high = DI-bendable (escapable). Overlaid on
  the knockback-shape gallery.

## Visual report (`--json` / `--html`)

The tool also emits a **lossless JSON** report and a **self-contained HTML** visual report (no external
deps). The JSON is a per-tick dump (100s of KB-low MB) — **gitignored**, regenerate on demand; only the
`.html`/`.md` renderings commit under `docs/generated/`. One richer collection feeds both; the markdown
path is unchanged.

- `--json <path>` — structured: per-move frame data, per-tick trajectory arcs, adv per move×%, kill% + blast clearance.
- `--html <path>` — three human-readable sections:
  - **Frame-advantage heatmap** — rows = moves, columns = victim %, cells colored green(+)→red(−) by adv; click a column header to sort.
  - **Knockback shape gallery** — small-multiple SVG arcs (height vs travel) per move × %, apex dot, KV/stun/apex labels.
  - **KO & blast clearance** — kill % (lowest victim % crossing a blast line) + bars for how close each move gets to top/side blast.
- **Kill % geometry**: binary search 0→250%, lowest % at which the launch crosses a blast line on a
  **Crossroads-style 60×60 proxy** (top +20, sides ±40, bottom −10), center launch, no DI. Blast clearance
  is the fraction of the way to top/side blast at the highest simulated % — always populated.
- **Finding (2026-08-17):** no normal KO ≤ 250% on the proxy — knockback magnitudes are small vs blast-zone
  distance. Read clearance to see "up-slash apex is 43% of the way to top blast."

## What it reports (the core — frame advantage lives here)

**1. Frame data (authored)** — pure constants from the ability spec, no sim: trigger, active window,
damage, angle, base KB, growth, stun, IASA, landing lag, auto-cancel before/after, total duration.
Instant answer to "how long is this attack / when does it hit?"

**2. Per-hit trajectories (simulated)** — one row per hitbox × per victim %, the knockback shape:
`KV m/s | hitstop | stun | adv | advL | rise@stun | drift@stun | apex | actionable tick | landed tick`.
- **`adv`** = on-hit frame advantage = `stun − (IASA − trigger)`. Positive = attacker acts before the victim
  leaves hitstun. **This is the headline number.**
- `advL` (aerials) = landed follow-up pays landing lag.
- Method: fresh grounded victim at that %, launch via the real `ApplyKnockback` with the hitbox's authored
  values, step the sim until landing (cap 2400 ticks). No DI/SDI; hitstop reported but not simulated.

**3. Pipeline parity** — safety: runs the *real* hit path (input → baked-bone hitbox → `ResolveHits` →
hitstop queue → queued launch) and compares applied KV/stun/apex vs the direct-formula rows. Any `DIVERGE` =
the game behaves differently from the report. Existing because the 2026-08-14 x87 float bug silently
scaled nothing while .NET tests were green.

## Opt-in / experimental — NOT part of the design loop

- `--combos` — combo matrix (no-travel bound `TA < stun`) + movement probes (greedy AI chase, real sim).
  Encodes **scripted route strings**, which contradict the freeform-combo design goal. Keep it as a
  diagnostic only; do not build balance decisions on it.
- `--traj <slot>` — raw per-tick CSV launch trace for one grounded hit.
- `--shape [step]` — knockback feel surface: sampled arc every ~0.2s per hit per %, phase markers
  (H=hitstun/F=flight/A=apex/G=landed) + one-line KV/stun summary.
- `--pipe`, `--dll <path>`, `--parity` — internal diagnostics (see `unity-mcp-gamedev` skill for the
  `--parity` vs in-game launch-contract sentinel check).

## Caveats (read before trusting a report)

- **Every move is included regardless of knockback profile.** Frame data (active / duration / IASA / stun)
  is always exact. Trajectories use the resolved launch: Custom/Adaptive use their authored base/growth;
  named profiles (Light/Medium/…) resolve from the profile table.
- **Adaptive (Melee-361) moves** (e.g. Kistu's Quick Slash / Air Slash / Reverse Slash) are simulated with
  their **authored angle as a representative** — the real launch angle varies with hit position (a level hit
  sends flatter). Tagged `adaptive` in the HTML report with an in-report caveat; frame data is unaffected.
- **Multi-hit moves false-positive parity.** A 2-hit starter (e.g. Kistu g2 Double Slash) re-launches the
  victim with hit 2 in the pipeline run, inflating pipeline apex vs the single-hit direct row → `DIVERGE`.
  KV/stun still matching is the signal it's an artifact, not a real drift.
- **Manki / Nilus produce empty reports** until their kits are Melee-converted. That is correct, not broken.
- **Per-character combo routes:** only FightGuy has authored routes in `RoutesFor` (`DefaultRoutes`). Add a
  char's designed links there to get its combo matrix.
- Hitstop is reported (`ComputeHitstopTicks`) but not simulated — flight starts at launch.

## When to run

- User asks for attack duration, active frames, frame advantage, knockback shape/range, or a move-data table.
- After a balance/timing change to a character's normals — regenerate and read the diff as the changelog.
- If a report disagrees with in-game feel, run `--parity` and compare with the game's launch-contract
  sentinel (see `unity-mcp-gamedev` skill).

## Verify / flow

1. After a Shared change, rebuild first: `dotnet build src/Shared/ --nologo` (DLL auto-copies to Unity Plugins).
2. `dotnet build tools/MoveDataReport/ --nologo`.
3. Run, read the markdown, sanity-check numbers against `docs/characters/<char>.md` / the ability spec.
4. Commit regenerated `docs/generated/*.md` alongside the behavior change if it should be a changelog artifact.
