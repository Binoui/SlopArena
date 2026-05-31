# Contributing to SlopArena

First off, thanks for being here! SlopArena is a community-driven project — every contribution, whether code, design, documentation, or bug reports, makes the arena better for everyone.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Project Architecture](#project-architecture)
- [Coding Guidelines](#coding-guidelines)
- [How to Contribute](#how-to-contribute)
- [Pull Request Process](#pull-request-process)

## Code of Conduct

This project is governed by the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating, you agree to uphold a harassment-free, inclusive environment for everyone.

## Getting Started

SlopArena is an Unreal Engine 5.7 C++ project. You'll need:

- **Unreal Engine 5.7** (source build or prebuilt from Epic Games Launcher)
- **C++ toolchain:** Visual Studio 2022 (Windows), Rider, or Clang (Linux)

### Clone and Build

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

1. Right-click `SlopArena.uproject` → **Generate Visual Studio Project Files** (or use the UE command line)
2. Open `SlopArena.uproject` — UE will prompt to build C++ code
3. Press **Ctrl+Shift+B** to compile

> **Note:** The project is designed for headless compilation on Linux. Editor rendering has known Vulkan driver limitations on certain Intel GPUs.

## Project Architecture

```
SlopArena/
├── Source/SlopArena/
│   ├── Characters/            # Character definitions, abilities, pawn
│   │   ├── CharacterRegistry.*     # Roster data table (compiled fallback)
│   │   ├── SlopArenaCharacter.*    # Main player pawn (ACharacter)
│   │   ├── SlopArenaCharacterDefinition.h  # Data asset schema (UDataAsset)
│   │   └── Abilities/              # GAS ability classes
│   ├── AI/                     # Bot brain and controller
│   ├── Combat/                 # Projectile management, hit detection
│   ├── Network/                # Character state, netcode primitives
│   ├── Shared/                 # Combat math, enums, movement sim
│   └── World/                  # GameMode, arena definitions
├── Server/                     # Headless server (reserved, WIP)
└── SlopArena.uproject
```

**Key design decisions:**
- Characters are defined as data (UDataAsset or a compact C++ table) — balance is data, not code
- Combat math lives in `Shared/` with zero engine dependencies where possible
- GAS (Gameplay Ability System) handles ability activation, effects, and attributes
- Server-authoritative architecture — all combat decisions validated server-side

## Coding Guidelines

### Language

**All code MUST be in English.** No exceptions. This includes:
- Variable names, method names, class names
- Comments and documentation
- String literals shown to the player
- Commit messages and PR descriptions

### Style

- **Braces:** Allman style (opening brace on new line)
- **Indentation:** Tabs (1 tab = 1 level)
- **Naming:**
  - `PascalCase` for classes, methods, properties, member functions
  - `PascalCase` with `F` prefix for non-UObject structs
  - `PascalCase` with `U`/`A`/`S` prefix for UE classes (UObject, AActor, SlopArena...)
  - `_camelCase` for private fields (underscore prefix)
  - `camelCase` for local variables and parameters
- **Access modifiers:** Always explicit (`public:`, `private:`, `protected:`)
- **Includes:** Use the project name prefix for cross-directory includes (e.g., `"SlopArena/Characters/SlopArenaCharacter.h"`)

### UE5-Specific

- Every `UCLASS()` / `USTRUCT()` / `UENUM()` needs `GENERATED_BODY()`
- Include the generated header as the last include in `.cpp` files
- Never shadow built-in parameter names (e.g., `Instigator` → `DamageInstigator`)
- Prefer `TObjectPtr<T>` over raw pointers for UProperties
- Use `FGameplayTag` over string enums when possible
- Keep `_Process()` / `Tick()` lightweight — use timers or GAS for periodic effects

### Project Conventions

- One class per header file, filename matches class name
- All non-trivial classes need a `SLOPARENA_API` or `SLOPARENA_SHARED_API` export macro for DLL linkage
- XML-doc public APIs with `/** ... */`
- Use `namespace` for internal helper code in `.cpp` files

## How to Contribute

### Reporting Bugs

Open an issue with:
- A clear title and description
- Steps to reproduce
- Expected vs actual behavior
- Screenshots or logs if applicable

### Suggesting Features

Open an issue with:
- What you want to achieve
- How it fits the game's design philosophy
- Rough implementation idea (optional but helpful)

### Code Contributions

1. Fork the repository
2. Create a branch: `git checkout -b feature/your-feature`
3. Make your changes
4. Ensure the project compiles (UE5 build)
5. Submit a Pull Request

### Non-Code Contributions

- **Game design** — Propose new characters, ability tweaks, game modes
- **3D art** — Characters, animations, environment props for the arenas
- **UI/UX** — HUD, menu flow, character select screen
- **Level design** — Arena layouts
- **Documentation** — Fix typos, improve guides
- **Playtesting** — Report balance issues, bugs, feel feedback

## Pull Request Process

1. Ensure your code follows the [Coding Guidelines](#coding-guidelines)
2. Keep PRs focused — one feature/fix per PR
3. Write a clear description of what and why
4. Reference any related issues (e.g., `Closes #42`)
5. Be responsive to review feedback
6. A maintainer will merge once approved

---

**Welcome aboard, and happy slopping!**
