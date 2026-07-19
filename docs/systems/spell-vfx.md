# Spell Visual Effects

> Per-ability VFX for Manki, FightGuy, and future characters.
> This doc is a placeholder — spell VFX are not yet implemented.

---

## Pipeline

Spell VFX will be routed through a dedicated `SpellVFXManager` that reads ability activation events from the simulation (separate from `CombatFeedback` which handles generic hit sparks).

## Planned VFX per Character

### Manki

| Ability | VFX | Status |
|---------|-----|--------|
| RMB (Aerosol Flame) | Cone-shaped flame particle, streams while held | ❌ TODO |
| Q (Round Bomb) | Projectile arc + explosion sphere on impact | ❌ TODO |
| E (Bazooka) | Rocket trail + explosion | ❌ TODO |

### FightGuy

| Ability | VFX | Status |
|---------|-----|--------|
| Spell attacks | Ice/spell VFX from existing assets | ❌ TODO |

## Reference

- Generic hit spark system: [`vfx-particles.md`](vfx-particles.md)
- PvP roadmap: [`docs/plans/2026-07-03-pvp-roadmap.md`](../plans/2026-07-03-pvp-roadmap.md)
