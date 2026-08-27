# Testing and Verification

SlopArena has a layered verification ladder. Start with the smallest layer that proves the changed contract, then move outward when the change crosses a boundary.

## 1. Shared build and tests

`src/Shared/` is pure C# `netstandard2.1`. Run after Shared changes:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
```

Use a focused test filter while iterating, then run the full Shared suite before delivery. Tests should assert observable simulation behavior: state transitions, timing boundaries, collision, damage/Knockback, interruption, deterministic serialization, and catalog identity. Avoid assertions tied only to implementation details or volatile test totals.

## 2. Targeted contract tests

Choose tests that cover the changed boundary:

- movement, jump, Dash, ledge, and air-use behavior;
- hitbox/projectile geometry and collision;
- Hitstun, Hitstop, Knockback, Combo Influence, Clash, and Burst;
- cooked timeline execution, typed operations, capability admission, interruption, and presentation events;
- package compiler diagnostics, deterministic bytes, manifest/hash validation, and Match Content Catalog admission;
- codecs and server/client content requirements.

New observable behavior needs a behavioral test when existing coverage would not fail for a plausible regression. Keep tests in `tests/Shared.Tests/` and use the existing helpers and fixtures.

## 3. Package and cook checks

For a package change, inspect before cooking:

```bash
unity pipeline list --format json
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

Require a successful semantic result, a valid inspect status, `dirtyOrStale: false`, and matching source/cooked/package hashes. A failed cook must preserve the last valid artifact; verify that no invalid draft was promoted. Check the generated runtime package under `content-cooked/<package>/` and the exact roster requirement when the package is built-in.

The maintained FightGuy check is:

```bash
scripts/verify-fightguy-package.sh
```

## 4. Ability Lab and Unity Training

After Unity-facing changes:

1. confirm the Unity Pipeline is reachable;
2. recompile the Editor and inspect current console errors;
3. open the affected package in Ability Lab;
4. preview a valid cooked draft and confirm slot identity, timing, hitboxes, and presentation;
5. open Training and exercise movement, the changed move, collision, interruption, and landing behavior.

The authoritative preview is the cooked Shared path. Ability Lab may show a clearly non-authoritative editing pose for invalid drafts, but Training and matches must never silently use invalid or stale content.

## 5. Local GameServer/PvP

When the change crosses networking or match composition:

```bash
dotnet build src/Server/ --nologo
dotnet run --project src/Server/
```

Exercise a local two-client match where practical. Verify that the GameServer admits the exact cooked package set, clients receive the same content requirements, attacks resolve from server state, and respawn/stock flow remains intact. Use [Netcode Architecture](systems/netcode-architecture.md) for packet and reconciliation details; do not duplicate volatile wire layouts here.

## Verification evidence

Record:

- commands run and whether they passed;
- package/cook status and hashes when content changed;
- Unity console status and the Training/Ability Lab surface exercised;
- the local server/client path exercised when applicable;
- any unexercised runtime surface.

Do not claim a live runtime result from a build or static inspection alone.
