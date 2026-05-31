# SlopArena

**A high-execution, open-source 3D arena brawler** built with Unreal Engine 5.7 (C++).

SlopArena is a third-person PvP brawler focused on tight ability kits, physics-driven combat, and pure player skill. No PvE, no grind, no microtransactions — just the arena.

> **Status:** Early prototype. Core movement, combat, and character systems are skeleton-implemented. GAS (Gameplay Ability System) integration is in progress.

---

## Core Philosophy

- **PvP First** — No PvE, no farming, no laning. Pure player-vs-player skill in compact arenas.
- **Small Kits, Big Decisions** — Each character has 6 abilities (Light, Heavy, 3 skills + Ultimate). Every cooldown matters.
- **Open Source** — Built by the community, for the community. MIT license.

---

## Features

### Characters (5 Playable)

Each character is a pre-assembled kit with distinct stats and playstyle:

| Character | Role | HP | Speed | Weight | Signature Mechanic |
|-----------|------|----|-------|--------|--------------------|
| **Colossus** | Brawler | 120 | 550 | 1.3 | Grapple, Shield Bash, War Cry |
| **Marksman** | Ranger | 80 | 650 | 0.8 | Power Shot, Snare Trap, Arrow Volley |
| **Wraith** | Assassin | 70 | 700 | 0.7 | Shadow Step, Poison Blade, Death Mark |
| **Titan** | Tank | 150 | 480 | 1.6 | Charge, Fortify, Arena (trapping ult) |
| **Sol** | Sun Mage | 75 | 625 | 0.75 | Solar Orb, Sunbeam, Sunburst |

Stats are defined in a compact data table (`CharacterRegistry.cpp`) and will eventually live as DataAssets in the Content Browser for editor-driven balance patches.

### Combat System (In Progress)

- **GAS (Gameplay Ability System)** — UE5's ability framework. Light attacks, heavies, and abilities are all `UGameplayAbility` subclasses.
- **Hit Detection** — Shape-based detection (MeleeCone, Projectile, CircleAoE, Beam) via SpellResolver
- **Knockback & Hitstun** — Physics-driven knockback with directional influence
- **Status Effects** — Burn, slow, stun, poison via `FGameplayTag`

### Movement

- Dash with cooldown
- Jump with air control
- Knockback with directional influence during hitstun
- Action state machine (Idle, Jogging, Attacking, Hitstun, Dashing)

### Networking (Planned)

- Server-authoritative architecture with client-side prediction
- UDP-based 60Hz tick rate
- `Server/` folder ready for headless dedicated server

---

## Project Structure

```
SlopArena/
├── Source/
│   └── SlopArena/
│       ├── SlopArena.cpp/.h       # Module entry point
│       ├── Characters/            # Character definitions, abilities, pawn
│       │   ├── CharacterRegistry.*     # Roster data table
│       │   ├── SlopArenaCharacter.*    # Main player pawn (ACharacter)
│       │   ├── SlopArenaCharacterDefinition.h  # Data asset schema
│       │   └── Abilities/              # GAS ability classes
│       ├── AI/                     # Bot controllers and behavior
│       ├── Combat/                 # Projectile management, hit detection
│       ├── Network/                # CharacterState, netcode primitives
│       ├── Shared/                 # Combat math, enums, movement sim
│       ├── World/                  # GameMode, arenas
│       │   └── Arenas/             # Arena definitions and registry
├── Server/                         # Headless server (reserved, WIP)
└── SlopArena.uproject             # UE5 project file
```

---

## Dependencies

- **Unreal Engine 5.7** (C++ development build)
- **Visual Studio 2022** or **Rider** for Windows / **Clang** toolchain on Linux

### Linux (CachyOS / Arch)

```bash
# Install prerequisites
sudo pacman -S clang lldb lld cmake ninja
# UE5 handles the rest via SlopArena.uproject
```

---

## Building

1. Clone the repo
2. Right-click `SlopArena.uproject` → **Generate Project Files** (or use the UE command line)
3. Open `SlopArena.uproject`
4. Press **Ctrl+Shift+B** to compile C++ code

> **Note:** The project is currently in a headless-build state. The editor cannot render on certain Intel GPUs due to Vulkan driver limitations (Lunar Lake / Mesa issue). Compilation and headless testing (`-nullrhi`) are the primary workflows.

---

## Controls (Planned)

| Input | Action |
|-------|--------|
| **WASD** | Movement (camera-relative) |
| **Space** | Jump |
| **Left Click** | Light Attack |
| **Right Click** | Heavy Attack |
| **Q / E / R** | Abilities 1-3 |
| **F (or Shift+F)** | Ultimate |
| **Shift** | Dash |
| **Mouse** | Camera orbit |

---

## Contributing

SlopArena is a community-driven project. See [CONTRIBUTING.md](CONTRIBUTING.md) to get started.

---

## License

MIT — see [LICENSE](LICENSE).
