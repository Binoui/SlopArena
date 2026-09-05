# Phase 3 — FightGuy Operation Catalog Completion
> **Historical execution plan.** Preserved as a dated decision/implementation record, not a current checklist. Current product work follows the [playable friends demo reset](docs/plans/2026-09-05-playable-demo-reset.md). Revalidate historical status claims against current code before reuse.

## Status

Planning revision — Phase 3 must be accepted before Phase 4 implementation.

## Authority

This plan refines Phase 3 of `docs/plans/2026-08-26-fightguy-character-cooking-cutover.md`.
It does not replace the parent cooking/cutover plan or the accepted ADR-0029.

`docs/systems/ability-lab.md` is not an implementation authority for this work. The
Ability Lab editor is complete for its current scope. Phase 4 below concerns
simulation presentation events, not editor features.

## Problem

The repository contains the Phase 3 building blocks, but not one proven runtime
owner:

- `CharacterPackageCompiler` already parses typed source operations and emits cooked
  timelines.
- `CookedTimelineAbility` already executes cooked operations and forwards typed
  capability parameters.
- `FightGuyOperationCatalog` and `FightGuyData.BuildFightGuy` still provide a
  production C# representation.
- `CharacterContentSerializer` attaches the static FightGuy catalog when loading
  JSON, so compiled package output is not yet the sole runtime input.
- `ServerSimulation` still contains a temporary FightGuy compatibility path for
  definitions without cooked slots.

The Phase 3 gate needs an explicit transitional boundary instead of silently
allowing multiple authorities.

## Decisions to lock before implementation

1. **Cooked timeline is the runtime contract.** The timeline runtime consumes typed
   `CookedTimelineOperation` objects only. No presentation, movement, or combat
   behavior reads loose string parameter maps.
2. **Internal capabilities are temporary and explicit.** The four current FightGuy
   specials may remain `slop.internal.fightguy.*.v1` capabilities for this slice.
   Each capability is admitted by exact ID/version and receives its typed cooked
   parameter record.
3. **Legacy FightGuy construction is an adapter, not a second behavior path.** Until
   the later package cutover, the existing C#/JSON loader may adapt into the same
   `CookedCharacterDefinition` shape. It must not define divergent gameplay values.
4. **Phase 3 includes one real semantic event.** The FightGuy authoring fixture and
   transitional catalog add `presentation.cyclone-kick.start` to `ground.R` at tick
   zero, after the capability-start operation. `air.R` inherits it through its
   existing alias. This is presentation-only and must not change gameplay state.
5. **No event transport is part of Phase 3.** Phase 3 may expose a local semantic
   event sink only as an interface seam for Phase 4; it does not add UDP fields,
   client queues, deduplication, or visual effects.
6. **Authored order is preserved.** Operations execute in source list order for equal
   ticks. Alias expansion copies the operation list without sorting it.


## Work sequence

### 3.1 Inventory and mapping

Create a table/test fixture covering every FightGuy ground and air slot:

- slot identity and air alias identity;
- stage count and duration;
- animation IDs;
- typed operations in authored order, including the real
  `ground.R`/`air.R` `presentation.cyclone-kick.start` operation and its emitted
  event identity;
- internal capability ID/version and typed parameter record;
- hitbox/projectile values;
- interruption and natural-completion behavior.

Use the hotfixed baseline as the differential reference. Do not retune gameplay
values during this phase.

### 3.2 Finalize typed operation semantics

Define and test the minimum operation set already present:

- set velocity;
- spawn hitbox;
- spawn projectile;
- set aim state;
- start typed capability;
- complete timeline.

For each operation, lock its unit, bounds, same-tick ordering, and state effects.
Reject unknown operations, units, fields, capabilities, and parameter values.

### 3.3 Finalize timeline lifecycle

Prove `CookedTimelineAbility` behavior for:

- operation execution at tick zero and later ticks;
- multi-stage transition timing;
- immediate completion preventing later same-tick operations;
- natural completion;
- hitstun, death, Burst, and other authoritative interruption;
- stateful capability start, tick, end, and cancel;
- no leaked aim, velocity, hitbox, or active capability state.

The runtime owns interruption cleanup. Authored timelines do not need cleanup lists.

### 3.4 Establish one transitional FightGuy adapter

Keep legacy source loading only at the current pre-cutover boundary. Adapt it once to
cooked runtime definitions, then make simulation consume the cooked shape for
FightGuy. Remove any path where `CharacterClass.FightGuy` plus slot selects a
concrete ability directly.

The adapter must be easy to delete in Phase 9. It must not be copied into Ability
Lab, Training, PvP, GameServer, reports, or tests.

### 3.5 Differential verification

Run the Phase 1 baseline harness against the hotfixed definition and the Phase 3
cooked definition for every populated ground/air slot. Compare:

- state transitions and timers;
- movement-resource fields and velocities;
- spawned hitboxes/projectiles;
- damage, knockback, and facing data;
- lifecycle completion/interruption.

Any delta requires an explicit named approval; migration alone is not permission to
change gameplay.

## Phase 3 acceptance gate

Phase 3 is complete only when:

- all FightGuy slots are represented by the typed catalog;
- the real `presentation.cyclone-kick.start` operation is present in the
  FightGuy fixture/catalog and is emitted by ground R and air R;
- same-tick authored ordering is deterministic;
- no FightGuy behavior depends on loose parameter lookup;
- exact internal capability admission is tested;
- interruption and natural completion are tested;
- the temporary adapter is the only legacy boundary;
- no direct FightGuy class/slot ability dispatch remains in simulation;
- differential traces have no unexplained gameplay changes, apart from the named
  presentation-only baseline delta; and
- Shared tests pass without changing existing known-debt expectations.

Only after this gate passes should Phase 4 implementation begin against its locked
event identity and transport contract.


## Explicit non-goals

- Unity visual adapters or VFX.
- Ability Lab editor changes.
- UDP packet changes.
- Package asset cooking, package assembly, or Match Content Catalog cutover.
- New creator operations or speculative capabilities.
