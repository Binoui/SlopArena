# FightGuy Normal-Attack Accent Grammar

Issues: #155, #156

## Selected grammar

Normal-attack accents follow the attacking limb and use the existing authored
`AttackStage.HitboxEvents` window. Emission begins at `TriggerTick` and clears at the
exclusive end tick. Startup, recovery, idle, movement, hitstun, and respawn do not emit
or retain an accent.

- **Light:** 0.75× authored width and 0.08 seconds of persistence.
- **Medium:** 1.25× authored width and 0.12 seconds of persistence.
- **Heavy:** 1.8× authored width and 0.16 seconds of persistence, plus one
  restrained parallel fragment per attack window.
- **Color:** retain the character-authored trail color. FightGuy remains blue-white.
- **Boundary:** this is presentation-only. Simulation timing, hitboxes, damage,
  knockback, and animation data remain unchanged.

This active-window/limb-path/weight-tier rule is the rollout grammar. Each limb uses a
cached, tapered `TrailRenderer` arc sampled from its actual motion; the legacy billboard
particle ribbon is not used. Width, lifetime, and color remain presentation tuning
knobs; timing must continue to come from `HitboxEvents` rather than a parallel
presentation event system.

## FightGuy normal coverage

Every grounded and aerial normal uses the same renderer path. The attack variant is
captured when the attack starts, so walking off a ledge cannot switch a grounded move to
its aerial presentation. Multi-hit moves activate only the limb named by the currently
active hitbox; capsule hitboxes activate both endpoint limbs when both have authored
trails.

| Normal | Active ticks | Limb path | Weight |
|---|---:|---|---|
| Ground 1 — Low Kick | 4–8 | Right foot | Light |
| Air 1 — Double Punch | 6–10, 16–20 | Right hand, then left hand | Light |
| Ground 2 — Straight Punch | 5–9 | Right hand | Medium |
| Air 2 — Floating Kick | 7–31 | Left foot | Medium early, light late |
| Ground 3 — Sweeping Kick | 7–12 | Right foot | Medium |
| Air 3 — High Kick | 14–19 | Right foot | Medium |
| Ground 4 — Double Kick | 10–16 | Left and right feet | Heavy |
| Air 4 — Air Smash | 20–26 | Right hand | Heavy |

No FightGuy normal is intentionally unaccented. Normals without an active authored
hitbox window emit nothing during startup, gaps between hits, and recovery.

## Verification

Headless integration on 2026-08-19:

- `dotnet build src/Shared/ --nologo`: 0 errors; 5 pre-existing nullable warnings.
- `dotnet build src/Server/ --nologo`: 0 errors; 11 pre-existing nullable warnings.
- Unity project script compilation: `COMPILE-OK` after a forced domain reload.
- Full Shared suite: 735 passed, 7 failed. The failures are existing simulation/test
  debt in Nilus abilities, Kistu reach, dash behavior, and hitstun re-hit animation;
  this presentation-only change does not touch those paths.
- Live Play Mode renderer probe: slot 3/tick 4 created one cached arc with one emitting
  primary trail (`cache=1; emitting=1; enabled=True`).

Project-owner gameplay-camera review approved the arc-plus-heavy-fragment result on
2026-08-19. That approval is the visual acceptance artifact for issues #155 and #156;
bright/dark screenshots are not required. The remaining repeat-use, interruption, and
Training/PvP parity checks are retained in `TESTING-UNITY.md` as regression checks.
