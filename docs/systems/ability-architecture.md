# Ability Architecture

SlopArena abilities are authored as deterministic content and executed by the Shared simulation. The current architecture separates engine-owned mechanics, package-authored timelines, trusted built-in capabilities, and legacy compatibility code.

## Authority

```text
Character Package source
  package.json + character.json + CharacterAssetCatalog.asset
                         │
                         ▼
        Shared compiler + Unity asset cook
                         │
                         ▼
              immutable cooked package
                         │
                         ▼
            Match Content Catalog
                         │
                         ▼
             Shared ServerSimulation
```

The GameServer and client prediction consume the same cooked representation. Unity does not execute gameplay logic independently. `PlayerRenderer` and other presentation systems consume authoritative state and semantic presentation events only.

## Package-authored ability model

A package has sixteen canonical entries: grounded and aerial variants for `1`, `2`, `3`, `4`, `A`, `E`, `R`, and `F`. Physical controls are input adapters; package identity is the canonical slot ID.

Each slot contains a fixed, ordered timeline of stages. A stage owns duration, IASA, landing-lag/auto-cancel timing, animation IDs, and typed operations. Operations have explicit versioned contracts, units, bounds, defaults, and budget cost. Same-tick operations execute in authored order.

Supported operation categories include:

- deterministic velocity changes;
- hitbox and projectile spawns;
- aim-state changes;
- starts of approved stateful capabilities;
- semantic presentation events;
- explicit timeline completion.

Authoring does not contain arbitrary branches, expressions, or transition predicates. Hold/release and other variable-duration behavior lives in bounded engine capabilities. Ground/air aliases are expanded by the compiler and do not exist as runtime dispatch rules.

## Engine-owned mechanics

The engine owns deterministic implementation of movement, target/aim state, hitbox geometry, baked-bone resolution, projectiles, explosions, damage, Knockback, Hitstun, Hitstop, Clash, Burst, cooldowns, IASA, landing lag, and air-use limits. Package data composes these capabilities; it does not execute code on the authoritative server.

`CookedTimelineAbility` is the current Shared interpreter. It advances stages, executes operations, starts capability instances, and emits presentation events. `InternalCapabilityRegistry` resolves trusted built-in capabilities by versioned semantic ID.

## Interruption and lifecycle ownership

The generic runtime owns interruption. Hitstun, death, Burst, a new ability, and simulation-owned overrides cancel active timelines/capabilities through the cancellation path. Natural completion uses the completion path. Both paths must deterministically clear or preserve the state they own; authored timelines must not rely on a cleanup branch that may never execute.

A presentation event contains stable match-tick/entity/operation identity. Clients resolve its semantic asset ID and deduplicate it across prediction and rollback. Presentation never feeds back into simulation.

## Trusted temporary capabilities

FightGuy currently uses explicitly admitted `slop.internal.fightguy.*` capability IDs for native behavior that has not yet been decomposed into public creator primitives. These IDs are available only to the trusted built-in cook profile. A package cannot grant itself access, and Workshop content cannot reference them. Each exception needs an owner, reason, scope, and migration path under [ADR-0022](../adr/0022-workshop-first-content-architecture.md).

## Legacy ServerAbility compatibility

`ServerAbility` remains a concrete Shared lifecycle seam. The existing legacy Nilus implementation and built-in capability adapters may implement:

- `OnStart` for activation;
- `Tick` for per-tick behavior;
- `OnEnd` for natural completion;
- `OnCancel` for interruption cleanup;
- `OnHitEntity` for hit-time effects;
- resolver, baked-data, simulation-state, arena, and presentation-event context supplied by `ServerSimulation`.

The base class is also the current superclass of `CookedTimelineAbility`. This implementation detail does not make polymorphic subclasses the universal authoring contract. Do not present `AbilityFactory(CharacterClass, slot)` or a new character-specific subclass as the default path for package content. New behavior belongs in the compiler's typed timeline/capability model unless it is an explicitly recorded trusted exception.

## Runtime flow

```text
InputState
  → ServerSimulation selects a catalog slot
  → CookedTimelineAbility executes the current stage
  → typed operation invokes Shared primitive/capability
  → SpellResolver resolves hitboxes/projectiles
  → CharacterState receives authoritative results
  → presentation events/state reach the client
```

The Match Content Catalog is immutable for the match. A later cook applies only to a later match. Missing, invalid, stale, incompatible, or hash-mismatched content fails closed; it does not fall back to raw source, C# definitions, or a similar capability.

## Modifying abilities

1. Confirm the behavior is not already provided by an engine primitive.
2. If package-authored, add or adjust a typed operation/timeline field with explicit schema, units, bounds, and deterministic semantics.
3. If variable-duration or native behavior is required, extend an approved stateful capability and record compatibility/versioning.
4. Define natural completion, cancellation, hit identity, and presentation events.
5. Validate and cook the package, then test the Shared observable behavior through the catalog path.

For package creation, follow [Adding a Character](../characters/adding-a-new-character.md). For universal mechanics, see [Combat Systems](combat-systems.md). For legacy maintenance, scope changes to the affected compatibility implementation and do not use it as a new-content template.
