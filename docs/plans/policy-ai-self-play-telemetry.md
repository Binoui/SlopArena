# Policy AI (bots) + self-play telemetry — issue #148

## Scope

Deliver a deterministic heuristic bot that plays the real `ServerSimulation`, a headless
self-play match generator, and two analysis outputs: **match statistics** (hit/whiff rate,
combo length, damage per stock, per-move usage) and **spatial threat data**. In-game bot
matchmaking/training-mode wiring is out of scope (follow-up) — the Shared policy module is
the reuse seam.

**Design correction vs the issue (decided 2026-08-17):** the issue's "hitbox heat spots"
(world-space density map of where hitboxes were active across matches) is dropped. Hitboxes
are entity-relative (`OffX/Y/Z` rotated by facing, spawned at `entityPos + rotate(offset)`,
static for `DurationTicks`), so a world-space map conflates two independent things: the
**threat zone** (a deterministic kit property) and **where the fighters happened to be**
(a property of the bot policy + stage + spacing). A cold cell means "no hitbox there" *or*
"the opponent never stood there" — indistinguishable. Replaced by:

1. **Reach envelope** — deterministic per-move threat zone, entity-relative side-view SVG,
   computed from the ability spec + sim. Zero matches needed; the kit answers "where is my
   threat zone" exactly.
2. **Whiff spots** — the genuinely empirical half. During self-play, record the opponent's
   position *relative to the attacker* (normalized into the attacker's facing frame) at
   swing windows that did not connect. "Where do I whiff" is a spacing-behavior question
   that only matches can answer.

Bots + match stats stay as the issue specs them.

## Facts the design rests on (verified)

- Input injection: `ServerSimulation.Tick(Dictionary<ulong, InputState>)`; an entity missing
  from the dict gets `default` (zero input). A bot = fill the dict per tick per entity ID.
- `InputState.MoveX/MoveY` are **world-space** X/Z. The client rotates the camera-relative
  stick into world space before sending (`InputController.BuildInputState`); the sim consumes
  them directly (`GetInputDirection`). Bots must emit world-space movement.
- Facing convention: yaw 0 = facing +Z; world dir = `(sin(yaw), cos(yaw))`; hitbox offsets
  rotate `hx = PX + OffX·cos − OffZ·sin`, `hz = PZ + OffX·sin + OffZ·cos`. To face a target
  at delta `(dx, dz)`: `yaw = atan2(dx, dz)`. `FacingYaw`/`AimYaw` are degrees × 100 (short).
- `ActiveSlot`: 1 = LMB, 2 = RMB, 3 = Q, 4 = E, 5 = R, 6 = F. Ground normals are slots 1–4;
  air normals are slots 1–4 pressed while airborne (`GetSlotAbility(slot, air)`).
- The sim auto-computes `state.TargetEntityId` each tick (nearest enemy within 20 m,
  `ComputeSoftLockTargets`) — bots can read it or brute-force.
- Connect events: `sim.LastTickHits` — `List<HitResult>` with `OwnerEntityId`,
  `TargetEntityId`, `Damage`.
- Match end: `StockMatchRule` (`Deaths >= MaxStocks` → eliminated, `Evaluate` → winner); sim
  owns respawns/spawn points. Self-play uses the kill-% geometry proxy arena (flat 60×60,
  top +20, sides ±40, bottom −10) so KOs actually end matches; it is deterministic and
  self-contained (no stage data dependency).
- `MoveDataReport`'s greedy-chase policy (`RunProbe`/`RunFollowUpSim`) is the prior art the
  issue cites for the policy approach. Test harness: `TestHelpers` (`MakeSim`, `PlayerState`,
  `NpcState`, `RegisterEntity`, `Tick(dict)`) — the `ScriptedFollowUp` pattern.
- Client already has an input-injection hook (`InputController.SetAiInput`, used by NPCs) —
  unused by v1, noted for the in-game follow-up.

## Module layout

```
src/Shared/AI/
  IBotPolicy.cs          — Decide(own state, target state, def, rng, memory) → InputState
  HeuristicBotPolicy.cs  — seeded deterministic policy v1
  BotMemory.cs           — per-entity bot state (swing tracking, decision cooldown)
  SelfPlayMatch.cs       — runs one bot-vs-bot match on a real ServerSimulation
  MatchRecorder.cs       — accumulates telemetry DTOs during a match
  TelemetryDto.cs        — per-tick samples, hit events, combo links, swing/whiff records
tools/SelfPlayReport/    — console tool: N-match generator + JSON/HTML/markdown report
scripts/selfplay.sh      — wrapper (mirrors move-data.sh)
tests/Shared.Tests/
  BotPolicyTests.cs      — decision correctness + invariants
  SelfPlayTests.cs       — determinism + match invariants
  ReachEnvelopeTests.cs  — envelope math regression
```

Shared stays pure C# (netstandard2.1, no Unity); it already references `System.Text.Json`
(the codecs), so telemetry DTOs serialize without new dependencies. The bot does **not**
touch `CharacterState` — no wire changes, prediction unaffected.

## Bot policy v1 (`HeuristicBotPolicy`)

Deterministic heuristic, per tick per entity:

1. **Target** — `state.TargetEntityId` if it names a live enemy, else brute-force nearest
   enemy (≤ 20 m, same rule as the sim).
2. **Face** — `FacingYaw = atan2(dx, dz) × 100` toward the target (clamped to short range).
3. **Tiers** (only when not in hitstun/hitstop/anim-lock, else emit no action input):
   - *In attack reach* → press the highest-priority slot whose hitbox reach covers the
     target. Reach = max over first-stage hitboxes of `(forward offset + radius)` in the
     facing frame, plus `LungeForce` travel over the active window. Priority order:
     grounded slots 1→4; if airborne, air slots 1→4 (jump toward target first when
     grounded and the target is above).
   - *In dash range, not attack reach* → `Dash` toward target.
   - *Far* → `MoveX/MoveY` = normalized world-space delta to target.
   - Target airborne & self grounded → `Jump` (+ `JumpHeld` while closing).
4. **Determinism** — a seeded `Random` per match; the policy consumes it only for
   tie-breaks/decision jitter (e.g. slot choice when multiple connect, retreat option on a
   failed swing). Same seed → same match, bit-for-bit.

Invariants the policy must never violate: `MoveX/MoveY` magnitude ≤ 1; no `ActiveSlot`
while `HitstunTicks > 0 || HitstopTicks > 0 || AnimLockTicks > 0 || State == Attacking`;
no `Dash` while `BurstRecoveryTicks > 0`; no duplicate one-shot flags across ticks
(one-shot semantics are the sim's job — the bot emits a fresh `InputState` per tick, same
contract as a client).

## Self-play runner (`SelfPlayMatch`)

- Register two entities (player id 1 / NPC id 100, mirrored spawns), `StockMatchRule`
  (3 stocks), the proxy arena, per-entity `BotMemory`.
- Loop `sim.Tick(inputs)` where inputs = both policies' decisions; cap at 10 800 ticks
  (3 min) as a deterministic safety net (a draw is reported, not a crash).
- Feed `MatchRecorder` per tick: entity states (sampled every tick — the JSON is
  gitignored, size is fine), `LastTickHits`, swing windows, respawn/death events.
- Terminate on `rule.Evaluate(...)` having a winner or the cap.

## Telemetry (`MatchRecorder`)

- **Swing record** — one per slot press: attacker, slot, window `[trigger,
  trigger+duration]` of the pressed move, whether any `HitResult` from that attacker landed
  in the window (connect) or not (whiff), and — on whiff — the target's position relative
  to the attacker at the window start, normalized into the attacker's facing frame
  (`Δ` rotated by `−FacingYaw`): `relX` (side), `relY` (height), `relZ` (forward).
- **Hit events** — attacker, target, damage, tick, attacker state at hit (grounded/air).
- **Combo links** — consecutive hits by the same attacker on the same target with the
  target in hitstun throughout the gap (derived from hit events + stun windows).
- **Per-tick samples** — positions only (for the stats provenance; the spatial maps use the
  swing records, not raw positions — that is the whole correction).

Derived stats: hit rate, whiff rate, damage per stock, avg/max combo length, per-move
usage + per-move hit/whiff split, match duration, deaths.

## Reach envelope (deterministic threat zone)

Per move (slots 1–4 + air 1–4, first stage, all hitboxes — same collection as
`MoveDataReport.CollectHits`):

- For each active tick `t ∈ [trigger, trigger+duration)`, the hitbox disc center in the
  facing frame = `(OffX, OffY, OffZ) + lunge drift(t)` where lunge drift is the attacker's
  `LungeForce` displacement over the active window (integrate the sim's own lunge so the
  envelope matches the game, not an approximation). Facing = +Z by convention.
- Union of discs (+ radius) → a per-move side-view arc list: `(relZ, relY)` outline.
- Rendered as small-multiple SVG per move (like the knockback-shape gallery), tagged with
  reach distance and active window. Pure function of the def — regenerated on kit changes,
  diffable, no matches involved.

## Whiff spots (character-relative heatmap)

- Accumulate every whiff swing's `(relX→ignored, relZ, relY)` normalized frame point onto a
  side-view grid (e.g. 0.25 m cells, forward axis vs height, facing = +Z).
- Density = whiffs per cell; overlay the move's reach envelope outline so "whiffed because
  the opponent stood past my reach" vs "whiffed inside my reach (timing/placement)" are
  visually distinct. The inside-reach whiffs are the skill-vs-game signal.
- Per character, across all N matches.

## Tooling (`tools/SelfPlayReport`)

New console project (net8.0, refs Shared) — deliberately separate from `MoveDataReport`
(already 1700+ lines, single-purpose). CLI:

```bash
scripts/selfplay.sh [--matches N] [--seed S] [--char fightguy|kistu] \
    [--json report.json] [--html report.html] [--out report.md]
```

- Default: N=20, seed from a constant unless overridden (same seed → same matches → same
  report; diffable across tuning changes).
- Outputs: lossless JSON (gitignored), committed HTML + markdown. HTML sections:
  1. **Match stats** — hit/whiff/combo/damage/per-move tables.
  2. **Reach-envelope gallery** — per-move side-view SVG threat zones.
  3. **Whiff-spot heatmap** — per-character side-view density grid with envelope overlay.
- Reuses the HTML/JSON report pattern from `MoveDataReport`.

## Tests

- `BotPolicyTests` — decision correctness against crafted sim states: approaches when far
  (world-space `MoveX/MoveY` normalized toward target), attacks when in reach (right
  `ActiveSlot` from priority), faces the opponent (`FacingYaw ≈ atan2(dx,dz)`), emits no
  action inputs in hitstun/hitstop/anim-lock, magnitudes ≤ 1.
- `SelfPlayTests` — determinism (same seed → identical per-tick state trace across two
  runs), termination (winner or cap, no exception), both entities dealt and took damage,
  swing accounting consistent (hits + whiffs = swings), no NaN/out-of-bounds positions.
- `ReachEnvelopeTests` — the envelope math: a known move's reach extents pin (regression),
  facing-rotation correctness (reach rotates with yaw — envelope is computed in the facing
  frame so it must be rotation-invariant in that frame by construction; the test pins the
  rotation helper used by both envelope and whiff normalization).
- Heatmap rendering verified by smoke (run the generator, inspect output) — per the issue's
  Testing Decisions; not unit-tested.

## Implementation notes (build 2026-08-17)

Corrections discovered while building, worth recording against the plan:

- **Proxy arena floor origin must be −30, not 0.** The 60×60 flat heightmap with `OriginX=0`
  covers `x∈[0,60]`, but self-play spawns at `x=±12` — OFF the floor, so both bots fell through
  and self-eliminated. The move-data tool never hit this (single entity at the origin). The
  self-play arena uses `OriginX=-30, OriginZ=-30` so the floor covers `[-30,30]`, matching the
  bounds. This is the one arena-geometry gotcha that only self-play exposes.
- **`AttackRange` is the auto-dash engage distance, NOT the hitbox reach.** Using it as the
  bot's attack trigger made the bot swing from ~2 m away while the g1/g2 hitboxes connect at
  ~0.7 m → 100% whiff. The bot uses `ForwardReach` = the ACTUAL hitbox extent (OffX/OffZ +
  radius + lunge), empirically verified (g1/g2/g4 connect at 0.7 m, not 0.9).
- **`CharacterState.AttackSlot` persists — it is the "last used slot", not a per-attack flag.**
  It never returns to 0, so swing detection via 0→nonzero transitions fires once per entity.
  Swings are detected from the bot's PRE-TICK press (`MatchRecorder.RecordPresses`, called
  before `sim.Tick` consumes the input).
- **Two identical bots trading at guaranteed-connect range yield degenerate telemetry**
  (100% hit rate, no whiffs, no KOs). Seeded jitter is required: the bot attacks when the
  opponent is within `maxReach × (1.0..1.6)` (seeded) and picks a random viable slot, so it
  sometimes commits out of reach (whiffs) and uses a mix of moves. This produces ~40% hit
  rate, real combos, and matches that can resolve.
- **Dash is omitted in v1.** Both bots dashing at each other overshoot massively and never
  settle at connect range. Approach is run-only (the sim normalizes `MoveX/Y`, no analog
  easing). Jump (anti-air) is exercised; dash is a follow-up.
- The disengage (back off after each swing and after being hit) is essential — without it the
  bots IASA-chain into a permanent point-blank mash that never resolves.

## Out of scope / follow-ups

- In-game bot integration: the **training-mode** bot is DONE — `TrainingMatch` gained an
  `NpcAiMode.Heuristic` that drives the NPC with `HeuristicBotPolicy.Decide` (random seed per
  match, `_npcClass` must be a kitted character like FightGuy). Still out of scope:
  matchmaking a bot into a **PvP match** (game-server-spawned bot entity). The policy's
  `Decide` seam is the handoff point; the client's `SetAiInput`/`InjectAI` hook exists.
- Dash usage in the policy (overshoot problem noted above).
- Recovery/ledge/tech behaviors — v1 policy is approach/attack only; a match can be lost
  to self-elimination, which is fine for telemetry.
- ML / learned agents.
- Any combat tuning change.
- Per-character policy personalities (single policy, parameterized by def only).
