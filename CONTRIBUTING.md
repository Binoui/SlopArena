# Contributing to SlopArena

SlopArena welcomes code, gameplay design, art, documentation, and playtesting contributions. All project communication and code use English. Read the [Code of Conduct](CODE_OF_CONDUCT.md) before participating.

## Development setup

Install:

- Unity `6000.0.78f1` through [Unity Hub](https://unity.com/download);
- .NET SDK 8 through [dotnet.microsoft.com](https://dotnet.microsoft.com).

Clone the repository and open `client/Unity/` in Unity Hub for the local training flow:

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

The repository has three runtime layers:

- `src/Shared/` — pure C# `netstandard2.1` deterministic simulation and content compiler;
- `src/Server/` — headless .NET GameServer and match orchestration;
- `client/Unity/` — Unity presentation, input, networking, Ability Lab, and asset cooking.

The generated Shared DLL is copied to `client/Unity/Assets/Plugins/SlopArena.Shared/` by the Shared build.

## Core rules

- The Shared simulation is the gameplay authority. Do not implement gameplay mechanics only in Unity.
- Keep `src/Shared/` free of Unity types and physics queries. Use the existing deterministic math and resolver APIs.
- Represent gameplay time in 60 Hz simulation ticks. Do not replace tick durations with frame-rate-dependent seconds.
- The client renders authoritative state and presentation events. Visuals, animation, VFX, and audio must not feed back into simulation.
- Preserve immutable package and match-content identity. A failed or stale cook must not silently fall back to another definition.
- Keep changes focused. Follow the repository's existing C# and Unity conventions instead of introducing parallel abstractions.

## Character contributions

New characters use the package-native workflow. Start with [Adding a Character](docs/characters/adding-a-new-character.md). The editable package lives under `client/Unity/Assets/CharacterPackages/<package>/` and contains:

- `package.json` for package identity, dependencies, creator, license, and attribution;
- `character.json` for gameplay semantics and the canonical 16-slot move grid;
- `CharacterAssetCatalog.asset` for package-local Unity asset bindings.

Cooked runtime content belongs under `content-cooked/<package>/` and is admitted through the cooked roster manifest. Do not add a registry factory, raw runtime JSON loader, manual FightGuy animation configuration, or standalone skeleton payload for new work. Manki, Kistu, and Nilus are legacy compatibility cases; do not copy their path into new packages.

## Verification

After Shared changes:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
```

After server changes:

```bash
dotnet build src/Server/ --nologo
```

After character package or Unity changes, inspect and cook the package through the Unity CLI:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target fightguy --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target fightguy --format json
```

Use the target package ID for another package. Require a successful semantic cook and a clean inspect status. For Unity-facing changes, recompile the Editor, read current console errors, then exercise the affected Training, Ability Lab, or local PvP path. See [Testing and verification](docs/testing.md) and [Unity CLI](docs/contributing/unity-cli.md).

## Pull requests

1. Create a focused branch.
2. Explain the user-visible behavior and the authoritative data path.
3. Include the verification commands and their results.
4. Link relevant issues, ADRs, or design documents.
5. Keep generated artifacts and unrelated local changes out of the pull request.

Do not commit or push on behalf of another contributor. Follow the repository's [commit conventions](docs/contributing/conventions.md) when creating commits.

## Non-code contributions

Open an issue or pull request for:

- fighter and move design;
- 3D models, animation, VFX, audio, or arena work;
- UI and accessibility improvements;
- documentation and research;
- reproducible playtest findings.

For design changes, describe the intended counterplay, timing, and interaction with the shared simulation before implementation.
