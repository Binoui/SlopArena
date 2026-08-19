# FightGuy Normal-Attack Accent Grammar

Issue: #155

## Selected grammar

Normal-attack accents follow the attacking limb and use the existing authored
`AttackStage.HitboxEvents` window. Emission begins at `TriggerTick` and clears at the
exclusive end tick. Startup, recovery, idle, movement, hitstun, and respawn do not emit
or retain an accent.

- **Light prototype:** grounded slot 1 uses one narrow stroke, 0.08 seconds of
  persistence, and 90 particles/second. FightGuy g1 demonstrates this on the right foot
  during ticks 4–8.
- **Heavy prototype:** grounded slot 4 uses broader 1.8× strokes, 0.16 seconds of
  persistence, and 150 particles/second. Its two authored trail bones produce paired
  strokes during ticks 10–16.
- **Color:** retain the character-authored trail color. FightGuy remains blue-white.
- **Boundary:** this is presentation-only. Simulation timing, hitboxes, damage,
  knockback, and animation data remain unchanged.

This active-window/limb-path/weight-tier rule is the rollout grammar. Width, lifetime,
emission density, and color remain presentation tuning knobs; timing must continue to
come from `HitboxEvents` rather than a parallel presentation event system.

## Verification

Headless integration on 2026-08-19:

- `dotnet build src/Shared/ --nologo`: 0 warnings, 0 errors.
- `dotnet build src/Server/ --nologo`: 0 errors; 11 pre-existing nullable warnings.
- Unity project script compilation: `COMPILE-OK` after a forced domain reload.
- Full Shared suite: 735 passed, 7 failed. The failures are existing simulation/test
  debt in Nilus abilities, Kistu reach, dash behavior, and hitstun re-hit animation;
  this presentation-only change does not touch those paths.

Gameplay-distance visual review and bright/dark arena judgment are intentionally left to
the project owner. The exact checklist is in `TESTING-UNITY.md`.
