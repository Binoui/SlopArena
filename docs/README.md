# SlopArena Documentation

> Start here. Pick your section.

---

## 🗺️ Orientation

| Doc | Read when |
|-----|-----------|
| [`architecture-overview.md`](architecture-overview.md) | **First.** Codebase map, data flow, naming conventions. |
| [`testing.md`](testing.md) | How to verify changes. Sandbox, PvP, tools. |

---

## ⚙️ Systems — How the game works

| Doc | Covers |
|-----|--------|
| [`systems/animation-system.md`](systems/animation-system.md) | Animancer clip playback, server-timed transitions |
| [`systems/combat-systems.md`](systems/combat-systems.md) | Universal combat mechanics |
| [`systems/hitbox-system.md`](systems/hitbox-system.md) | Hitbox/hurtbox architecture |
| [`systems/attack-hitbox-system.md`](systems/attack-hitbox-system.md) | Attack hitbox data pipeline |
| [`systems/ability-lab.md`](systems/ability-lab.md) | Visual hitbox/hurtbox authoring tool |
| [`systems/hitstun-di.md`](systems/hitstun-di.md) | Hitstun & directional influence |
| [`systems/netcode-architecture.md`](systems/netcode-architecture.md) | Server-authoritative model, prediction, reconciliation |
| [`systems/blast-zones.md`](systems/blast-zones.md) | Void death, kill boundaries |
| [`systems/npc-system.md`](systems/npc-system.md) | Training dummies, AI |
| [`systems/spell-vfx.md`](systems/spell-vfx.md) | Spell visual effects |
| [`systems/vfx-particles.md`](systems/vfx-particles.md) | Impact particles, text VFX, shader contracts |
| [`systems/range-based-combat.md`](systems/range-based-combat.md) | Range & attack distance design |

---

## 🎮 Characters — Roster & pipeline

| Doc | Covers |
|-----|--------|
| [`characters/adding-a-new-character.md`](characters/adding-a-new-character.md) | Full pipeline guide |
| [`characters/character-import-checklist.md`](characters/character-import-checklist.md) | Asset import checklist |
| [`characters/character-kit-design-principles.md`](characters/character-kit-design-principles.md) | Canonical 8-normal / 4-special kit structure |
| [`characters/manki.md`](characters/manki.md) | Manki — Mad Bomber Monkey |
| [`characters/fightguy.md`](characters/fightguy.md) | FightGuy — Martial Arts Champion |

---

## 🤝 Contributing — For contributors

| Doc | Covers |
|-----|--------|
| [`contributing/conventions.md`](contributing/conventions.md) | Art direction, animation naming, bone naming |
| [`contributing/quality.md`](contributing/quality.md) | Code quality guidelines |
| [`contributing/security.md`](contributing/security.md) | Security considerations |
| [`contributing/unity-cli.md`](contributing/unity-cli.md) | Local Unity CLI workflow, verified commands, Pipeline compatibility gate |

---

## 📚 Research — Design reference (not active)

| Doc | Covers |
|-----|--------|
| [`research/dko-character-kits.md`](research/dko-character-kits.md) | DKO character kit analysis |
| [`research/dko-mechanics.md`](research/dko-mechanics.md) | DKO systems reference |
| [`research/frame-data-reference.md`](research/frame-data-reference.md) | DKO manual frame counts |
| [`research/melee-frame-data.md`](research/melee-frame-data.md) | Melee frame data — full reference (25 chars) |
| [`research/melee-frame-analysis.md`](research/melee-frame-analysis.md) | Melee comparative analysis → 8+4 kit profiles |
| [`research/melee-knockback-model.md`](research/melee-knockback-model.md) | Melee KB/hitstun/flight/DI/weight — decompiled formulas + migration deltas |

---

## 📋 Plans — Implementation roadmaps

| Doc | Covers |
|-----|--------|
| [`plans/2026-08-01-pvp-roadmap-v2.md`](plans/2026-08-01-pvp-roadmap-v2.md) | Online PvP implementation plan (current) |
 
## 🧭 Architecture Decision Records

| ADR | Decision |
|-----|----------|
| [`adr/0022-workshop-first-content-architecture.md`](adr/0022-workshop-first-content-architecture.md) | Workshop-first product framing and content boundary |
| [`adr/0023-built-in-content-api.md`](adr/0023-built-in-content-api.md) | Semantic built-in capability IDs and dynamic registry |
| [`adr/0024-deterministic-creator-gameplay-primitives.md`](adr/0024-deterministic-creator-gameplay-primitives.md) | Data-driven, engine-owned creator gameplay primitives |
| [`adr/0025-workshop-package-architecture.md`](adr/0025-workshop-package-architecture.md) | Immutable package identity, cooking, dependencies, and compatibility |
| [`adr/0026-workshop-multiplayer-synchronization.md`](adr/0026-workshop-multiplayer-synchronization.md) | Exact package synchronization and online admission |
| [`adr/0027-open-source-modkit-preview.md`](adr/0027-open-source-modkit-preview.md) | Open ModKit and installed-game authoritative preview |
| [`adr/0028-built-in-content-compatibility.md`](adr/0028-built-in-content-compatibility.md) | Versioned built-in capability contracts and support windows |
