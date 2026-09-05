# SlopArena Documentation Map

Canonical guidance is short and linked below. Dated plans, research, generated reports, handoffs, and accepted ADRs are preserved as project records; they are not automatically current implementation instructions.

## Start here

**Current product goal:** [Playable friends demo reset](plans/2026-09-05-playable-demo-reset.md) — four roster characters, one proven online match path, and player feedback before further platform expansion. This is the execution entry point; older migration plans remain historical records.

| Document | Use it for |
| --- | --- |
| [Architecture overview](architecture-overview.md) | Repository map, cooked/legacy boundary, and runtime flow |
| [Testing and verification](testing.md) | Shared, package, Unity, and local PvP verification ladder |
| [Contributing](../CONTRIBUTING.md) | Contribution rules and required checks |
| [Project context](../CONTEXT.md) | Canonical domain vocabulary and settled mechanics |

## Architecture

| Document | Use it for |
| --- | --- |
| [Ability Architecture](systems/ability-architecture.md) | Cooked timelines, typed operations, capabilities, and interruption |
| [Animation System](systems/animation-system.md) | Animancer playback, semantic bindings, and timing |
| [Netcode Architecture](systems/netcode-architecture.md) | GameServer, prediction, reconciliation, and transport |
| [Release Pipeline](systems/release-pipeline.md) | Build, packaging, and release flow |
| [Unity CLI](contributing/unity-cli.md) | Inspect/cook commands and live Editor verification |

## Gameplay systems

| Document | Use it for |
| --- | --- |
| [Combat Systems](systems/combat-systems.md) | Universal 8-normal/4-special model and combat mechanics |
| [Hitbox System](systems/hitbox-system.md) | Hitbox, hurtbox, and collision geometry |
| [Hitstun DI](systems/hitstun-di.md) | Hitstun, Hitstop, and Combo Influence design |
| [Ability Lab](systems/ability-lab.md) | Package editing and authoritative preview |
| [NPC System](systems/npc-system.md) | Training entities and AI |
| [Blast Zones](systems/blast-zones.md) | Void death and arena boundaries |
| [VFX and Particles](systems/vfx-particles.md) | Presentation effects and visual contracts |

## Visual design

| Document | Use it for |
| --- | --- |
| [Visual language](design/visual-language.md) | Graphic identity, UI composition, palette, typography, motion, and presentation voice |
| [Stage concepts](design/stage-concepts.md) | Gameplay-first PVP stage concepts, readability, topology, and background hierarchy |
| [Art and asset conventions](contributing/conventions.md) | 3D character rendering, asset production, naming, licensing, and hygiene |
| [Visual presentation baseline](visual-baseline.md) | Repeatable gameplay-camera evidence for visual comparisons |

## Character authoring

| Document | Use it for |
| --- | --- |
| [Adding a Character](characters/adding-a-new-character.md) | Package source, asset catalog, cooking, and admission |
| [Character import checklist](characters/character-import-checklist.md) | Asset import and presentation checklist |
| [Kit design principles](characters/character-kit-design-principles.md) | Fighter roles, counterplay, and the canonical move grid |
| [FightGuy reference](characters/fightguy.md) | First cooked package and runtime path |

New packages live under `client/Unity/Assets/CharacterPackages/<package>/`. Cooked runtime artifacts live under `content-cooked/<package>/`.

## Character roster

| Character | Status | Reference |
| --- | --- | --- |
| FightGuy | Cooked package; reference vertical slice | [FightGuy](characters/fightguy.md) |
| Bonk | Cooked package; rostered | [Bonk](characters/bonk.md) |
| Manki | Cooked package; rostered | [Manki](characters/manki.md) |
| Kistu | Cooked package; rostered | [Kistu](characters/kistu.md) |
| Nilus | Legacy compatibility implementation | [Nilus](characters/nilus.md) |

The Nilus page is a modification-only legacy implementation record. Its legacy details
must not be copied into new package work.

## Contributing

| Document | Use it for |
| --- | --- |
| [Art and asset conventions](contributing/conventions.md) | Visual direction, naming, licensing, and asset hygiene |
| [Unity CLI](contributing/unity-cli.md) | Package inspection/cooking and Editor checks |
| [Repository contributing guide](../CONTRIBUTING.md) | Setup, rules, verification, and pull requests |
| [Code of Conduct](../CODE_OF_CONDUCT.md) | Community standards |

## Accepted ADRs

The current package and creator-content direction is defined by these accepted records:

- [ADR-0022: Workshop-first content architecture](adr/0022-workshop-first-content-architecture.md)
- [ADR-0023: Built-in content API](adr/0023-built-in-content-api.md)
- [ADR-0024: Deterministic creator gameplay primitives](adr/0024-deterministic-creator-gameplay-primitives.md)
- [ADR-0025: Workshop package architecture](adr/0025-workshop-package-architecture.md)
- [ADR-0026: Workshop multiplayer synchronization](adr/0026-workshop-multiplayer-synchronization.md)
- [ADR-0027: Open-source ModKit and installed-game preview](adr/0027-open-source-modkit-preview.md)
- [ADR-0028: Built-in content compatibility](adr/0028-built-in-content-compatibility.md)
- [ADR-0029: Character authoring and cooking](adr/0029-character-authoring-and-cooking.md)
- [ADR-0030: Ability Lab canonical slot projection](adr/0030-ability-lab-canonical-slot-projection.md)
- [ADR-0031: Character asset dependency classification](adr/0031-character-asset-dependency-classification.md)

Older accepted ADRs remain the decision record for the mechanics they cover. If a living guide conflicts with an accepted ADR or current code, update the living guide; do not rewrite the historical decision.

## Research and reference

The `research/` directory contains design analysis and external references, including:

- [Melee frame data](research/melee-frame-data.md)
- [Melee frame analysis](research/melee-frame-analysis.md)
- [Melee Knockback model](research/melee-knockback-model.md)
- [Melee movement audit](research/melee-movement-audit.md)
- [Melee/netcode impact](research/melee-model-netcode-impact.md)
- [DKO mechanics](research/dko-mechanics.md)
- [DKO character kits](research/dko-character-kits.md)

- [Stage authoring POC debrief](research/stage-authoring-poc-debrief.md) — imported-asset lessons and recommendations for a future stage-authoring skill.

These documents inform design but do not override the Shared implementation or accepted ADRs.

## Historical plans and reports

- `plans/` contains dated roadmaps, implementation plans, handoffs, and superseded approaches.
- `generated/` contains generated move-data, self-play, and analysis reports.
- `handoffs/` contains dated implementation handoffs.
- `superpowers/` contains archived plans and specifications.

Read these for context on why a decision exists. Treat a dated plan as historical unless a current task explicitly adopts it. The [FightGuy cooking cutover](plans/2026-08-26-fightguy-character-cooking-cutover.md) is the current migration record for the first cooked slice.
