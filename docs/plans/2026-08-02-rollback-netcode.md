# Rollback Netcode — Design

**Date:** 2026-08-02
**Status:** Spec complete, implementation pending go. Output of the grill-with-docs session (triage → grilling → domain-modeling).
**Tracks:** PvP roadmap v2 Phase 7 (`docs/plans/2026-08-01-pvp-roadmap-v2.md`), `docs/systems/netcode-architecture.md` §6/§10.
**Glossary:** `CONTEXT.md` § Prediction & Rollback — ConfirmedTick, RollbackWindow, InputRelay.

## Goal

Kill input lag in online matches. The client currently renders raw server state ("one-tick display latency is intentional (Phase 1)"), which at internet RTT (20–100ms = 1–6 ticks) makes movement and hit reactions feel floaty. Rollback gives every client instant local response while the server stays authoritative: predict locally, correct when the authoritative state disproves the prediction.

Demo context: v0.2.0-demo.1 is live with an official server (SlopArena EU #1, `sloparena.barakaslurp.fr:7777`); the friends playtest is the first real internet-latency audience — exactly the regime this design exists for.

## Decisions

| # | Decision | Rationale / evidence |
|---|---|---|
| D1 | **Predict all entities** (self + opponents), hard cap 10 | Hit reactions must feel instant both ways — a platform fighter's feel is in being hit. Rivals 2 style. Cap bounds re-sim cost. |
| D2 | **InputRelay** — server appends each entity's consumed input (or no-input marker) to the broadcast | The client has no other source of opponent inputs; the protocol is state-only today. |
| D3 | **Always re-simulate** from ConfirmedTick on every state batch; input ring buffer + confirmed base only, **no predicted-state ring** | Deterministic, no tolerance tuning, one code path. Cost ≈ the server's existing 15-match workload — trivial at ≤10 entities. |
| D4 | Corrections **snap** (no blend) | Platform-fighter convention. Corrections are frontier-only and small; blend adds renderer state and rubber-band smear. |
| D5 | Frontier guess: **hold-last** — a *feel* decision | The server drops→neutral on empty queues (`MatchInstance.cs:337-341` omits the entity; `ServerSimulation.cs:161/346/467` → `default(InputState)`), so no consistency argument exists. Hold-last minimizes divergence for continuous movement; neutral stalls constantly. Loss-induced divergence is rare and corrected by the next batch. |
| D6 | Local sim runs the **same rule** (`StockMatchRule`, same stocks); **MatchState UI snaps** from the server packet | KO/respawn/elimination prediction must match the server; lifecycle UI is 1 byte @60Hz — nothing to predict. |
| D7 | Rollback core lives in **`src/Shared` as a pure C# `RollbackSimulator`** (input buffer, confirmed base, rebuild-and-replay, gap handling); **`RollbackSimulationBridge`** is a thin Unity adapter — third `ISimulationBridge` impl: poll → core → render, `NetworkClient` wiring. PvP switches to it, Training keeps `LocalSimulationBridge`. | Puts the whole algorithm at the one testable seam (`tests/Shared.Tests`), so the golden-tick determinism suite can exist; the bridge stays a thin adapter. Everything the core needs is already Shared (`ServerSimulation`, `ArenaDefinition`, `CharacterDefinition`, `InputState`, `IMatchRule`). |
| D8 | **Golden-tick determinism test** + **simulated delay/loss harness** | Determinism is the whole game — prove byte-identical re-sim or nothing works. |

### Corrections landed during review
- Relay payload is **19B** (`InputState.Size = 19`, `InputState.cs:38`), not 31B — the 31 includes entityId+tick which do not re-travel.
- Own-entity input source on own-packet loss: client replays **its own input buffer**, never the relayed (held) input — the instant-input promise is the point of this work. Divergence is ≤2 ticks and snap-corrected. (Server holds last and discards late ticks via `clientTick <= _serverTick`, `MatchInstance.cs:276`.)

## Wire format

### Downlink, per entity (was 75B)
```
[0..7]   entityId          (8)
[8..11]  tick              (4)   ← _serverTick, in client-tick space (verified: MatchInstance.cs:342, 395, 400)
[12..74] CharacterStatePacket (63)
[75]     hasInput          (1)   ← 0x01 = relayed InputState follows; 0x00 = no input this tick
[76..94] InputState        (19)  ← present iff hasInput
```
- **hasInput = 0** means the server's queue for that entity was empty that tick (or the entity is eliminated): the client must *omit* the entity from its re-sim inputs dict, reproducing the server's `default(InputState)` path exactly.
- Max 95B/entity → **~57KB/s down** @ 10 entities × 60Hz. Uplink unchanged (31B).

### The reconciliation anchor
The tick field already works — no server change needed beyond the relay. Verified: `SendState()` writes `_serverTick` to both the envelope and `CharacterStatePacket.TickNumber`; `_serverTick = max(_serverTick, input.tick)` runs in client-tick space.

## Client architecture

```mermaid
flowchart TD
    A[FixedUpdate] --> B[ownInputBuf[tick] = polled input]
    B --> C[re-sim: RollbackSimulator rebuilds<br/>from ConfirmedTick base]
    C --> D[replay: own buffer for self,<br/>relayed stream for opponents,<br/>hold-last at frontier]
    D --> E[render re-simulated states]
    E --> F[drain state batch]
    F --> G[advance ConfirmedTick]
    G -->|new base| C
```

`RollbackSimulator` (`src/Shared` — the tested core):
- owns a `ServerSimulation` — same code as the server, already client-proven via `LocalSimulationBridge`;
- `ownInputBuffer`: `InputState` per send tick;
- `confirmedBase`: full entity-state set + tick, replaced on every received batch;
- rebuild-and-replay on every state batch (D3);
- `Resolver` returns the local resolver (hitboxes/projectiles predict locally);
- gap absorption + window cap (30 ticks).

`RollbackSimulationBridge` (`client/Unity/Assets/Scripts/Runtime/Simulation/` — thin adapter):
- feeds polled input into the core's own buffer; decodes received batches (state + relay) into the core's confirmed base; renders the core's re-simulated states (snap);
- setup parity with `MatchInstance`: same registration order (roster order), same defs + baked data, same respawn positions, same rule (D6).

Server (`MatchInstance.SendState`): append the relay section per entity from the per-slot input actually consumed; empty queue → `hasInput = 0`; eliminated entities always `hasInput = 0`. Nothing else changes.

## Determinism contract

The re-sim reproduces the server's states exactly **iff** input knowledge is identical. Divergence sources, all handled:
1. **Own entity** — own buffer, never relayed: transient (≤2 ticks), snap-corrected. Golden-tick loss case covers it.
2. **Opponents inside the relayed window** — exact relay incl. no-input markers: zero divergence by construction.
3. **Opponents at the frontier** (≤1–2 ticks) — hold-last guess: the only real divergence source, snap-corrected.
4. **Gap ticks** (all queues empty → sim + broadcast stall behind `if (inputs.Count > 0)`): confirmed base stalls, re-sim window absorbs the gap; cap the window at **30 ticks** as a desync guard.

Hard requirements: Shared code only (`MathF`, no `UnityEngine`), **no RNG in Shared** (audited: none), identical entity registration order, `Simulation.OnDebugLog` stays log-only.

### Golden-tick test (`tests/Shared.Tests`)
Drive a reference sim with a scripted input stream (incl. omissions and gaps); assert the `RollbackSimulator`'s re-sim is byte-identical to the reference. Cases:
- normal stream;
- own-packet loss (server holds last, client replays own buffer) — ≤2-tick transient, converges;
- opponent-packet loss (relay no-input marker path);
- gap tick (empty batch);
- elimination tail (eliminated entity broadcasts, never in inputs).

### Delay/loss harness
Dev-only seam around `NetworkClient` (drop / duplicate / reorder / inject RTT) so rollback is exercisable before the friends playtest. (Old roadmap Phase 3's "simulated UDP delay" test.)

## Match lifecycle

- Local sim uses the same `StockMatchRule` (stocks from match config) so KO/respawn/elimination predict identically.
- `MatchState` (countdown → fight → results) renders from the server packet — no local prediction. Local rule evaluation drives sim behavior only; the packet drives UI.

## Test plan

1. `dotnet test tests/Shared.Tests/` — golden-tick suite above + existing suites unchanged.
2. Unity playtest checklist (dev): two local clients through the delay/loss harness — movement feels instant; opponent movement smooth; forced loss shows snap correction; client-side pause doesn't wedge the sim; host-and-play (localhost, 0 RTT) unaffected.
3. F3 debug overlay: correction counter + RollbackWindow display (ties into issue #56's debug-overlay cluster).

## Sequencing (open decision)

- **A — after the first friends playtest** (roadmap's original Phase 7): gameplay feedback comes back clean; rollback lands before the public demo. Risk: the playtest runs at full internet lag, which may color feedback.
- **B — before/parallel with the playtest**: the playtest measures the real target, but gameplay + netcode ship in one batch — confounded feedback if something feels off.

Recommendation: **A**. The friends playtest is the game's first external gameplay signal — keep it uncontaminated. It also produces the correction-frequency data (via the debug overlay) for tuning D4/D5.

## Out of scope

- Late join / reconnect / desync recovery (issue #56 cluster 3)
- Prediction of match lifecycle (snap only)
- NPC prediction (PvP has none; TrainingMatch is already local-sim exact)
- Delay-based fallback modes
- Wire-format versioning (both sides ship together in this repo)
