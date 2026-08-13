# Melee Feel Implementation Plan

> Status: Execution plan after ADR-0019, ADR-0020, and ADR-0021 accepted on 2026-08-13.
>
> Scope: migrate the shared server-authoritative simulation, kit data, rollback behavior, and Unity presentation to the accepted global physics/feel model. This is an implementation plan, not a new design proposal.

## Invariants

- `src/Shared/` remains pure C# with zero Unity dependencies.
- The server simulation remains authoritative; Unity only renders and supplies input.
- No wire growth is planned by ADR-0019/0020/0021. Re-check packet sizes and rollback classifiers after state changes.
- Every Shared change requires `dotnet build src/Shared/ --nologo`.
- Golden changes require reasoned review before regeneration is accepted.
- Do not commit or push without explicit permission.

## Current impact inventory

### ADR-0019: hit response

Affected core paths:

- `src/Shared/Simulation.cs`: hitstop queue application, `ProcessHitstun`, `ProcessKnockback`, `ApplyKnockback`.
- `src/Shared/ServerSimulation.cs`: `ComputeHitstopTicks`, hit resolution, hitstun animation tier, queued launch data.
- `src/Shared/CharacterState.cs`: `LaunchMagnitude`, DI/queued launch fields, hitstun state.
- `src/Shared/CharacterDefinition.cs`: add static character `Weight`.
- `src/Shared/KnockbackProfile.cs`: delete profile resolution after all callers migrate; retain explicit per-hit angle/base/growth data only.
- `src/Shared/CSharpCharacterWriter.cs`: migrate serialized hitbox output away from profile fields.
- `src/Shared/Abilities/` and `src/Shared/Characters/`: expand canned profiles and update custom hit paths.
- Tests: `ComboInfluenceTests`, `HitstopTests`, `KnockbackPhysicsDataTests`, combat integration tests, kit regression/golden tests.

Contract changes:

- Knockback magnitude: `(base + growth * (P / 100 + 1) + damage * 0.1) * 200 / (weight + 100)`.
- `StunTicks == 0` means no hitstun; otherwise applied hitstun derives from applied knockback.
- Constant KV during hitstun; post-hitstun horizontal friction and flight gravity.
- DI rotates launch at hitstop exit; one-shot SDI plus ASDI; Combo Influence and `LaunchMagnitude` are removed.
- Hitstop becomes `min(12, (damage / 3 + 6) * multiplier)` with one authoring multiplier.

### ADR-0020: movement

Affected core paths:

- `src/Shared/CharacterDefinition.cs`: replace walk/sprint and fall-ramp fields with Run/air/fast-fall/jump fields.
- `src/Shared/Simulation.cs`: ground movement, dash coast/pivot, air acceleration/friction, jump, fast-fall, gravity, ledge snap.
- `src/Shared/ActionState.cs`: add `Run` and `LedgeHang`; classify `LedgeHang` as Complex for rollback.
- `src/Shared/CharacterState.cs`: ledge/invulnerability state fields as required; confirm wire impact.
- `src/Shared/Rollback/ActionStateClassifier.cs`: classify the new state correctly.
- `src/Shared/Characters/*.cs`: per-character movement tuning.
- Unity runtime renderer/input/state presentation: Run, pivot/coast, ledge hang/drop/jump/stand.
- Tests: movement, fast-fall, short-hop, dash, ledge, rollback classifier, and kit goldens.

Contract changes:

- Run replaces selectable walk/sprint locomotion.
- Dash remains the Shift defensive mechanic; dash ending becomes friction coast.
- Air movement uses per-character max speed, acceleration, and linear friction.
- Fast-fall sets velocity; no gravity multiplier/latch.
- Short-hop has its own force; double jump is weaker additive impulse.
- FallRamp machinery is deleted; FloatWindow remains only for recovery/post-hit/landing resets.
- Ledge hang replaces the old auto-pop and supports S drop, jump ledge-jump, W stand, occupancy, and grab invulnerability.

### ADR-0021: frame timing

Existing engine support is partially present, so this phase is policy enforcement and cleanup rather than greenfield implementation.

Affected paths:

- `src/Shared/ServerSimulation.cs`: IASA gate, landing-lag branch, landing-frame behavior, double-fire verification.
- `src/Shared/Simulation.cs`: buffer and dash gates around IASA/landing lag.
- `src/Shared/AttackData.cs`: required timing fields are already present; enforce authoring completeness.
- `src/Shared/Characters/*.cs`: normal IASA values, aerial landing lag, before/after auto-cancel windows, special/normal cooldown policy.
- Charge ability data/classes: rework temporary charge normals as specials where required.
- Tests: `IasaTests`, `LandingLagTests`, attack lifecycle and golden tests.

Contract changes:

- Normals author IASA; specials/recovery remain at zero IASA.
- Normals have zero cooldown; specials retain cooldowns.
- Every standard aerial declares landing lag and both auto-cancel windows.
- Auto-cancel ends the aerial immediately; lag-zone landing ends the aerial and applies hard landing lock.
- IASA never bypasses landing lag. Burst and air-jump remain the deliberate landing-frame exceptions.

## Execution order

### Phase 1 — ADR-0019 foundation

1. Add focused pure-function tests for formula, weight, hitstun, hitstop, DI, SDI, and flight-law boundaries.
2. Change shared state and simulation contracts.
3. Migrate `ServerSimulation` hit resolution and queued-hitstop launch data.
4. Remove Combo Influence and `LaunchMagnitude`.
5. Migrate all profile-backed hitboxes to explicit values.
6. Regenerate only hit-response-affected goldens and inspect diffs.

Acceptance: Shared build passes; hit-response tests prove the new model; packet size remains unchanged; full combat pipeline remains deterministic.

### Phase 2 — ADR-0020 movement

1. Add Run and LedgeHang state semantics plus rollback classification.
2. Replace movement/gravity/fast-fall/jump implementation.
3. Replace ledge auto-pop with occupied ledge hang and escapes.
4. Migrate per-character movement data.
5. Update Unity rendering/input-facing state behavior.
6. Regenerate movement goldens and run Unity compile/playtest gate.

Acceptance: movement and ledge tests pass; no obsolete walk/sprint/ramp fields remain; server/client rollback state remains coherent.

### Phase 3 — ADR-0021 timing

1. Verify and correct IASA and landing-lag engine paths.
2. Add authoring validation tests for every normal/aerial policy requirement.
3. Migrate all four kits' timing data and charge-normal classifications.
4. Regenerate timing goldens.

Acceptance: IASA/landing tests pass; no designed aerial has all-zero landing timing; special commitment and landing-frame exceptions behave as specified.

### Phase 4 — integration verification

Run:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/
dotnet build src/Server/
```

Then perform Unity compilation and the playtest checklist in `TESTING-UNITY.md` for client-facing changes. Review packet lengths, rollback classifiers, generated DLL placement, and the final diff.

## Golden policy

Do not regenerate all goldens after each edit. Regenerate by contract area:

- hit response: hitstop, knockback, hitstun, DI/SDI, combat integration, kit snapshots;
- movement: dash, air movement, jump, fast-fall, ledge, rollback snapshots;
- timing: IASA, landing lag, auto-cancel, attack-to-idle, kit snapshots.

Each changed golden must have a test or documented scenario explaining the intended contract change.

## Deferred by the ADRs

- Full ledge getup attack and getup roll kit.
- Missed-tech/knockdown/getup and ground/wall tech flow.
- Dedicated dash-attack move authoring.
- Final balance pass beyond migration-safe provisional values.
- Aerial airdodge during post-hitstun flight.
