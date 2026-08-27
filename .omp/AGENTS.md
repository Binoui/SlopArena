# SlopArena Project Context

## About the project

- SlopArena is a Unity 6 C# 3D platform fighter with a server-authoritative 60 Hz simulation.
- `src/Shared/` targets `netstandard2.1` and is compiled to the Unity plugin at `client/Unity/Assets/Plugins/SlopArena.Shared/`.
- `src/Server/` is the headless .NET GameServer. The Master server is a separate repository and never simulates matches.
- The Shared simulation is the gameplay authority. Never implement gameplay mechanics as client-only corrections or Unity-only rules.
- Do not install dependencies without asking.
- Implement user-selected numeric values unless they create a correctness issue; suggest once, then follow the decision.

## Current content architecture

New characters are package-native. The editable package is:

```text
client/Unity/Assets/CharacterPackages/<package>/
  package.json                 # identity, version, dependencies, license, attribution
  character.json               # gameplay source and canonical 16-slot grid
  CharacterAssetCatalog.asset  # package-local Unity asset bindings
```

The Shared compiler and Unity asset cook produce an immutable runtime package under `content-cooked/<package>/` containing the manifest, normalized runtime definition, deterministic pose data, and generated client bindings. The Match Content Catalog pins exact package IDs, versions, dependencies, capability versions, and hashes for each match. Raw authoring JSON is cook input, never the runtime contract.

FightGuy is the first cooked vertical slice. Manki, Kistu, and Nilus remain legacy compatibility definitions behind `LegacyCharacterCatalogAdapter` until migrated. Legacy files are modification-only compatibility, not templates for new packages.

Every package resolves the canonical 16-entry grid: grounded and aerial variants of `1`, `2`, `3`, `4`, `A`, `E`, `R`, and `F`. Physical controls are input adapters. They are not persisted move identity.

## Architecture

```text
Master server                 GameServer (`src/Server/`)
  lobby/meta  ───────────────► MatchControlServer / MatchInstance
                                      │
Unity client ◄──── UDP ──────────────┘
      │
      └── Shared `ServerSimulation` (`src/Shared/`)
```

`ServerSimulation` runs on the GameServer and on client local/prediction tracks. The GameServer remains authoritative. Training uses `LocalSimulationBridge`; PvP uses `RollbackSimulationBridge`, `LocalTrack`, `PredictedTrack`, and `RawTrack` as appropriate. Unity renders state and semantic presentation events through `PlayerRenderer`; it does not decide hit results, damage, timing, or match admission.

The current ability model is cooked fixed timelines with ordered typed/versioned operations. Engine-owned deterministic primitives implement movement, hitboxes, projectiles, damage, Knockback, Hitstun, Hitstop, Clash, Burst, timing locks, and presentation events. `ServerAbility` remains a Shared lifecycle seam for `CookedTimelineAbility`, trusted temporary FightGuy capabilities, and legacy Manki/Kistu/Nilus implementations. It is not the universal new-content authoring model.

FightGuy may temporarily use explicitly admitted `slop.internal.*` capabilities. Only the trusted built-in cook profile can resolve them. Workshop/package content cannot grant itself access; every exception requires an owner and migration path.

Warp is not a current gameplay contract. Do not add or document Warp-based mechanics as new behavior; use the current movement, targeting, Dash, and recovery rules in `CONTEXT.md` and `docs/systems/combat-systems.md`.

## Key conventions

### Project structure

- `src/Shared/` — compiler, cooked runtime model, deterministic simulation, codecs, and rollback primitives.
- `src/Server/` — GameServer, match control, and match instances.
- `client/Unity/Assets/Scripts/` — Unity input, presentation, networking, UI, and editor tooling.
- `client/Unity/Assets/AbilityLab/` — package editing and authoritative preview.
- `tests/Shared.Tests/` — Shared simulation, package, catalog, and codec tests.
- `content-cooked/` — canonical immutable cooked runtime artifacts.

### Unity

- Use `MonoBehaviour.Update`/`FixedUpdate` and Unity InputSystem APIs.
- Animancer plays semantic package bindings directly. Do not add AnimatorController-based gameplay ownership.
- Unity physics, animation callbacks, VFX, and audio are presentation or authoring aids only.
- Unity Editor/Pipeline operations target the main checkout. Worktree agents must not operate the main Editor through a worktree.

### Shared and simulation

- Keep `src/Shared/` free of Unity types and engine physics queries.
- Use pure deterministic math and the existing `SpellResolver`/geometry APIs for collision.
- Represent gameplay durations as 60 Hz tick counts, normally `ushort`.
- Preserve server/client Shared equivalence and immutable match content.
- Use semantic package IDs and canonical slot projection; do not add a second mapping.

### Verification commands

After Shared changes:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
```

After GameServer changes:

```bash
dotnet build src/Server/ --nologo
```

After package or Unity changes:

```bash
unity pipeline list --format json
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

Require a successful semantic cook, valid inspect status, `dirtyOrStale: false`, and matching hashes. For Unity-facing changes, recompile the Editor, read current console errors, and exercise the affected Ability Lab, Training, or PvP path. See `docs/testing.md` and `docs/contributing/unity-cli.md`.

## Workflow rules

- State the problem and proposed fix before editing; for architecture changes, document options and get approval first.
- Trace the full path before debugging: input → catalog/package → Shared simulation → state/event → Unity presentation.
- Reuse existing infrastructure before adding abstractions.
- Do not commit or push without explicit user permission.
- Preserve unrelated working-tree changes.
- Use Conventional Commits and one squash commit per branch.

## Agent verification protocol

Unity is main-repo-only. Worktree agents must not invoke Unity MCP or operate the main Editor from another worktree. Headless Shared/server builds and tests are mandatory for corresponding code changes. Unity-facing slices must leave a short Test in Unity checklist in the gitignored root `TESTING-UNITY.md`.

## Canonical references

- `CONTEXT.md` — domain vocabulary and settled mechanics.
- `docs/architecture-overview.md` — repository and runtime boundaries.
- `docs/systems/ability-architecture.md` — cooked abilities and lifecycle ownership.
- `docs/systems/combat-systems.md` — universal combat mechanics.
- `docs/systems/netcode-architecture.md` — transport, prediction, rollback, and match flow.
- `docs/characters/adding-a-new-character.md` — package authoring and cooking.
- `docs/contributing/unity-cli.md` — inspect/cook and Editor verification commands.
- `docs/README.md` — documentation map.
