# SlopArena

SlopArena is an open-source 3D platform fighter inspired by Smash and DKO. Fighters bring distinct hero kits to a shared arena: normals, recovery tools, playmaking specials, and high-commitment power moves.

The playable demo is built in **Unity 6** with a pure C# simulation shared by the client and GameServer. The simulation advances at **60 Hz**, keeps gameplay deterministic, and supports client prediction with server reconciliation and rollback.

> **Current state:** FightGuy is the first complete cooked-character vertical slice. Manki, Kistu, and Nilus remain playable through the legacy compatibility path while they are migrated package by package.

## Quick start

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

Install Unity `6000.0.78f1` and .NET SDK 8. Open `client/Unity/` in Unity Hub and press Play for the local training flow.

Build the simulation and run its tests from the repository root:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
dotnet build src/Server/ --nologo
```

Run the headless GameServer locally:

```bash
dotnet run --project src/Server/
```

## The game

SlopArena combines:

- camera-relative 8-direction movement, jump arcs, double jumps, fast-fall, ledges, and Dash;
- Smash-style damage percent, Knockback, Hitstun, Hitstop, Combo Influence, Clash, and Burst;
- readable hero kits with generous 3D hitboxes and meaningful recovery, zoning, and finisher choices;
- a server-authoritative GameServer for online matches and a local Shared simulation for prediction and training.

Each kit has a canonical **16-entry grid**: grounded and aerial variants for normals `1`, `2`, `3`, `4` and specials `A`, `E`, `R`, `F`. `LMB` and `RMB` are not persisted move identities; physical controls map to the canonical slots through the client input layer.

## Current roster

| Fighter | Style | Content status |
| --- | --- | --- |
| **FightGuy** | Direct martial-arts fundamentals, projectile, launcher, and beam | Cooked package; reference vertical slice |
| **Manki** | Agile rushdown and explosive space control | Legacy compatibility path |
| **Kistu** | Sword pressure and counterplay | Legacy compatibility path |
| **Nilus** | Void-based zoning and control | Legacy compatibility path |

See the [FightGuy package reference](docs/characters/fightguy.md) and the [character roster](docs/README.md#character-roster) for details.

## Architecture in one view

```text
Unity client ── input ──► Shared ServerSimulation ◄── input/state ── GameServer
     │                            │
     └── render authoritative state/events

Editable Character Package
  package.json + character.json + CharacterAssetCatalog.asset
                    │
                    ▼
       Shared compiler + Unity asset cook
                    │
                    ▼
Immutable content-cooked package
  manifest + runtime definition + poses + client bindings
```

The server and client consume the same cooked deterministic representation. Runtime matches pin package IDs, versions, hashes, dependencies, and capability versions in an immutable Match Content Catalog. Raw authoring JSON is cook input, not a runtime contract.

Explore the [interactive runtime architecture diagram](docs/generated/runtime-architecture.html) for component relationships, trust boundaries, and the primary runtime path.

The longer-term direction is cautious Workshop/package support: creators compose approved deterministic primitives and package-owned assets; they do not ship arbitrary simulation code, native plugins, or direct Unity-path dependencies. See [ADR-0022](docs/adr/0022-workshop-first-content-architecture.md) through [ADR-0030](docs/adr/0030-ability-lab-canonical-slot-projection.md).

## FightGuy and Ability Lab

FightGuy demonstrates the package-native workflow:

- edit `client/Unity/Assets/CharacterPackages/fightguy/`;
- inspect and cook through the Unity CLI;
- validate source, asset bindings, deterministic pose data, generated client bindings, and hashes;
- load the immutable result in Ability Lab, Training, PvP, and GameServer paths.

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target fightguy --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target fightguy --format json
```

Ability Lab is the primary gameplay editor for package drafts. It previews valid drafts through the same cooked definition and interpreter used by the game; an invalid draft is never silently substituted into a match.

To add a new character, start with [Adding a Character](docs/characters/adding-a-new-character.md). Do not copy the legacy C# registry path for new work.

## Documentation

Start with the [documentation map](docs/README.md), then read:

- [Architecture overview](docs/architecture-overview.md)
- [Combat systems](docs/systems/combat-systems.md)
- [Ability architecture](docs/systems/ability-architecture.md)
- [Ability Lab](docs/systems/ability-lab.md)
- [Animation system](docs/systems/animation-system.md)
- [Netcode architecture](docs/systems/netcode-architecture.md)
- [Testing and verification](docs/testing.md)
- [Contributing](CONTRIBUTING.md)

## Contributing

Issues, design feedback, code, art, documentation, and testing are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. This project uses the [MIT license](LICENSE) and follows the [Code of Conduct](CODE_OF_CONDUCT.md).

## AI usage

SlopArena uses agent-assisted development and generated art as practical tools. Contributions remain reviewed project work: determinism, licensing, gameplay correctness, and maintainability matter more than how an asset or patch was produced.
