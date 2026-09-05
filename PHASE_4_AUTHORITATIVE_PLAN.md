# Phase 4 — Authoritative Presentation Events
> **Historical execution plan.** Preserved as a dated decision/implementation record, not a current checklist. Current product work follows the [playable friends demo reset](docs/plans/2026-09-05-playable-demo-reset.md). Revalidate historical status claims against current code before reuse.

## Status

Planning revision — implementation is blocked until `PHASE_3_COMPLETION_PLAN.md`
passes its acceptance gate.

## Authority and scope

This refines Phase 4 of
`docs/plans/2026-08-26-fightguy-character-cooking-cutover.md` and ADR-0029.
It concerns semantic events emitted by the authoritative Shared timeline runtime.
It does **not** add Ability Lab editor features or visual adapters.

The current Ability Lab editor is already complete for its documented hitbox-authoring
scope. Its existing preview behavior must remain unchanged during this phase.

## Problem

The Phase 3 scaffolding has a presentation-event sink, but the event is not yet a
stable cross-track contract:

- `TimelinePresentationEvent` has no key object.
- `CookedEmitPresentationOperation` uses a stage-local operation cursor.
- local simulation, predicted replay, and server confirmation have no common drain
  or deduplication surface;
- UDP has no independent event packet; and
- required packet, replay, loss, and late-confirmation tests do not exist.

Phase 4 must solve those transport concerns without allowing presentation data to
change simulation state.

## Locked event contract

A cooked emit operation produces:

```text
TimelinePresentationEvent(
    MatchTick,
    EntityId,
    OperationIndex,
    PresentationId)
```

Its stable identity is:

```text
PresentationEventKey(MatchTick, EntityId, OperationIndex)
```

`PresentationId` is payload, not identity. A duplicate key with a conflicting ID is
ignored. The cooked operation index is generated, never authored, and is unique
across the package by canonical slot order, stage order, and authored operation
order. Source JSON does not contain it.

The event is semantic client-facing data. It cannot mutate CharacterState, hitboxes,
resolver state, rollback decisions, or ability lifecycle.

## Shared and rollback contract

1. `ServerSimulation.GetPresentationEvents(clear: true)` remains the authoritative
   queue drain.
2. `LocalSimulationBridge` copies and clears that queue after every simulation tick.
3. `LocalTrack` and `PredictedTrack` expose their Shared event drains, including
   events generated during held-input replay.
4. `RollbackSimulator` accepts events from local prediction, predicted replay, and
   transport. It publishes each key once for the lifetime of the match.
5. `RollbackSimulationBridge.Tick` processes entity packets and event packets
   independently. Missing entity packets never suppress local prediction or event
   ingestion.
6. `LastTickPresentationEvents` is the only per-tick bridge surface. Consumers may
   resolve presentation IDs, but resolution is client-only and has no simulation
   feedback.

The deduplication set is match-scoped. It is not pruned during a match and is not a
general event bus.

## Network contract

Events use one independent unreliable UDP datagram per event:

| Bytes | Field |
|---|---|
| `0..3` | little-endian magic `0x53455250` (`PRES`) |
| `4` | version `1` |
| `5..8` | little-endian `MatchTick` (`uint`) |
| `9..16` | little-endian `EntityId` (`ulong`) |
| `17..20` | little-endian `OperationIndex` (`int`) |
| `21` | UTF-8 ID length (`1..64`) |
| `22..` | UTF-8 `PresentationId` |

The packet exposes `Version`, `HeaderSize`, `MaxPresentationIdBytes`, `MaxSize`,
`WireSize`, `Serialize`, and non-throwing `TryDeserialize`. Serialization rejects
empty/overlong/invalid IDs, negative operation indexes, and undersized buffers.
Deserialization rejects wrong magic/version, truncation, invalid UTF-8, invalid
lengths, negative indexes, and trailing bytes.

`NetworkClient` classifies event datagrams before the generic state fallback and
queues decoded events separately. Malformed event datagrams are ignored without
affecting state or match-result queues.

After each authoritative simulation tick, `MatchInstance` drains events once and
broadcasts the same serialized datagrams to every connected client. Event loss is
accepted. No retransmission, batching, state-packet mutation, or visual transport
is added.

## Test-first acceptance

Before any Unity adapter work, tests must prove:

- event-key and payload round-trip;
- exact fixed/variable packet sizes;
- all malformed packet cases above;
- dropped/truncated datagrams do not block later valid events;
- same-tick authored ordering and completion cutoff;
- deterministic package ordinals and canonical bytes;
- local event emission;
- predicted replay emission, including held-last input;
- duplicate prediction replay suppression;
- late server confirmation suppression;
- acceptance of a different tick/entity/operation key; and
- unchanged rollback state when event datagrams are missing.

The baseline trace records drained presentation events per tick. The Phase 3
FightGuy fixture/catalog contains the real
`presentation.cyclone-kick.start` operation, so the cooked trace must contain that
event for ground R and its air alias. The older hotfixed baseline remains empty by
design; this named presentation-only delta is approved in the Phase 3 plan and
does not alter gameplay state. No second production loader is introduced.

## Phase 4 acceptance gate

Phase 4 is complete only when one cooked timeline produces exactly one observable
event per authored emit operation across local, predicted, and server-confirmed paths,
with no duplicates after rollback replay or late confirmation; malformed/lost event
packets leave gameplay unchanged; and focused plus complete Shared tests pass.

No Unity visual playtest is claimed. Visual ID resolution belongs to a later phase.

## Verification order

1. Focused presentation packet, timeline, compiler, and rollback tests.
2. Complete `dotnet test tests/Shared.Tests/` with known unrelated debt separated.
3. `dotnet build src/Shared/ --nologo`.
4. `dotnet build src/Server/`.
5. Unity CLI compile/status gate for the changed network/bridge interfaces.
6. Update gitignored `TESTING-UNITY.md` with a no-console-error checklist for
   Ability Lab, Training, and PvP; visible VFX are not expected.
