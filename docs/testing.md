# Testing and Verification

## Local iteration

Source or asset tuning uses the existing Editor development catalog and the affected
Ability Lab or Training path. A source edit can be compiled in memory without changing
`content-cooked`, generated animation catalogs, or roster pins. Invalid source blocks
preview with diagnostics, not stale fallback. Recompile for changed C#/Shared plugin, or
when Unity requests compilation; do not force a project recompile for prose or every JSON
numeric edit. Read current Unity errors and exercise the changed behavior. Local-only
preview is not persisted package verification.

## Integrated change

Shared edits require focused behavioral coverage while iterating, then `dotnet build
src/Shared/ --nologo` and `dotnet test tests/Shared.Tests/ --nologo` at delivery. Server
edits require `dotnet build src/Server/ --nologo` and `dotnet test tests/Server.Tests/
--nologo`. Unity-facing behavior needs the affected runtime check and current
compile/console evidence, not all unrelated scenes. Accepted character source/asset
changes intended to ship cross the explicit cook/inspect/roster-refresh boundary before
delivery. Documentation/tooling-only edits use the Python checks from the documentation
checker; no Shared build or Unity session is required for this cleanup.

## Distributable demo

Cook changed accepted packages, verify exact roster identities and all required payloads
in fresh publish outputs, then build and exercise the packaged client/server
join-to-rematch path. Link current release operations and the reset plan's reproduced
packaging blocker; the present FightGuy-only script is not roster-complete.

### Shared build and tests

`src/Shared/` is pure C# `netstandard2.1`. Run after Shared changes:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
```

Use a focused test filter while iterating, then run the full Shared suite before delivery. Tests should assert observable simulation behavior: state transitions, timing boundaries, collision, damage/Knockback, interruption, deterministic serialization, and catalog identity. Avoid assertions tied only to implementation details or volatile test totals.

### Targeted contract tests

Choose tests that cover the changed boundary:

- movement, jump, Dash, ledge, and air-use behavior;
- hitbox/projectile geometry and collision;
- Hitstun, Hitstop, Knockback, Combo Influence, Clash, and Burst;
- cooked timeline execution, typed operations, capability admission, interruption, and presentation events;
- package compiler diagnostics, deterministic bytes, manifest/hash validation, and Match Content Catalog admission;
- codecs and server/client content requirements.

New observable behavior needs a behavioral test when existing coverage would not fail for a plausible regression. Keep tests in `tests/Shared.Tests/` and use the existing helpers and fixtures.

### Package and cook checks

For a package change, inspect before cooking:

```bash
unity pipeline list --format json
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

For a rostered package, require a successful semantic result, valid inspect status,
`dirtyOrStale: false`, and matching source/cooked/package hashes. For a source-only probe,
require successful package resolution plus structured diagnostics and semantic cook failure
only when the probe intentionally has unresolved bindings. A failed cook must preserve the
last valid artifact; with no prior artifact, verify the cooked directory remains absent.
Check `content-cooked/<package>/` and the exact roster requirement only when the package is
built-in.

### Bonk pipeline probe evidence

The Bonk probe is not gameplay coverage. Record:

- inspect `success: true`, `packageId: bonk`, and sixteen canonical slots;
- cook semantic `success: true` with the shared dash/hit bindings;
- cooked payload hashes and preservation of `content-cooked/bonk/` after any later failure;
- package-specific stale tracking after Bonk source/catalog/dependency notifications;
- one queue request for repeated notifications, with unrelated package statuses unchanged;
- Unity recompile status and current console errors.

`KitScenario` golden tests begin only after an approved Bonk kit and successful cooked
package exist. The probe deliberately has no damage, timing, recovery, or capability
contract to golden-test.

The maintained FightGuy check is:

```bash
scripts/verify-fightguy-package.sh
```

### Ability Lab and Unity Training

For integrated Unity-facing changes and accepted package verification:

1. Confirm the Unity Pipeline is reachable, recompile the Editor when code or the
   Shared plugin changed, and inspect current Unity errors.
2. Run `EditorDevelopmentContentSelfTest.Run()` (or the named menu item) when
   content-resolution or cook behavior changed. A valid `character.json` edit must
   change the next Editor Training catalog without changing `content-cooked` or the
   generated animation catalog bytes. An invalid operation must report
   `value.out-of-range` at `character.operation.tick` with its code, path, and message,
   block the local catalog, and never use the old persisted package.
3. Confirm `TryBuildPersistedLocalMatchCatalog` still loads the exact persisted roster
   requirement and hashes. Keep the existing `content-cooked` package/release verification.
4. Open the affected package in Ability Lab, preview a valid persisted cooked draft,
   then open Training and exercise movement, the changed move, collision, interruption,
   and landing behavior. For the Editor `edit → Play` path, observe the changed source
   behavior and semantic animation/rig without running a publishing cook.

Local iteration uses the transient development catalog and affected runtime path; it
does not require this persisted-catalog self-test for every numerical tuning edit.

The authoritative preview is the cooked Shared path for accepted content. Ability Lab
may show a clearly non-authoritative editing pose for invalid drafts, but Training and
matches must never silently use invalid or stale content.

### Local GameServer/PvP

When the change crosses networking or match composition:

```bash
dotnet build src/Server/ --nologo
dotnet test tests/Server.Tests/ --nologo
```

Exercise a local two-client match where practical. Verify that the GameServer admits the
exact cooked package set, clients receive the same content requirements, attacks resolve
from server state, and respawn/stock flow remains intact. Use [Netcode Architecture](systems/netcode-architecture.md)
for packet and reconciliation details; do not duplicate volatile wire layouts here.

### Verification evidence

Record:

- commands run and whether they passed;
- package/cook status and hashes when content changed;
- Unity console status and the Training/Ability Lab surface exercised;
- the local server/client path exercised when applicable;
- any unexercised runtime surface.

Do not claim a live runtime result from a build or static inspection alone.
