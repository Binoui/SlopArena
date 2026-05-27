# Contributing to SlopArena

First off, thanks for being here! SlopArena is a community-driven project — every contribution, whether code, design, documentation, or bug reports, makes the arena better for everyone.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Project Architecture](#project-architecture)
- [Coding Guidelines](#coding-guidelines)
- [Spell System Guide](#spell-system-guide)
- [How to Contribute](#how-to-contribute)
- [Pull Request Process](#pull-request-process)

## Code of Conduct

This project is governed by the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating, you agree to uphold a harassment-free, inclusive environment for everyone.

## Getting Started

SlopArena is a Godot 4 .NET C# project. You'll need:

- **Godot Engine 4.6+ (.NET version)** — [godotengine.org](https://godotengine.org)
- **.NET SDK 8.0** — `sudo pacman -S dotnet-sdk-8.0` (Arch/CachyOS) or from [dotnet.microsoft.com](https://dotnet.microsoft.com)

### Clone and Run

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

1. Open Godot (.NET version)
2. Click **Import** → select `project.godot`
3. Press **F5** to run

> The sandbox runs a local simulation with 5 training dummies, a full spell system, and WoW-style controls. No server required.

## Project Architecture

```
SlopArena/
├── Scripts/          # Godot client scripts (C#)
│   ├── World/        # Entry point, heightmap
│   ├── Entities/     # PlayerController, DummyManager
│   ├── Combat/       # Combat simulation, projectiles, hitboxes
│   ├── Spells/       # Spell definitions and effects
│   ├── UI/           # Action bar, spellbook, unit frames
│   └── Camera/       # WoW-style camera
├── Shared/           # Pure C# library (no Godot dependency)
│   ├── CombatMath.cs # Hit detection, knockback math
│   ├── SpellResolver.cs # Spell resolution logic
│   ├── PhysicsConfig.cs # Physics constants and simulation
│   ├── MovementProfiles.cs # Movement profiles
│   └── ...           # Packets, enums, data structs
├── Server/           # Headless authoritative server (WIP)
├── assets/           # 3D models and animations
└── textures/         # Prototype textures
```

**Key design decisions:**
- `Shared/` has **zero** Godot dependencies — usable from client, server, and AI
- Combat math is pure C# (no engine physics) for deterministic simulation
- UDP-based server-authoritative 60Hz tick rate

## Coding Guidelines

### Language

**All code MUST be in English.** No exceptions. This includes:
- Variable names, method names, class names
- Comments and XML documentation
- String literals shown to the player
- Commit messages and PR descriptions

### Style

- **Braces:** Allman style (opening brace on new line)
- **Indentation:** Tabs (1 tab = 1 level)
- **Naming:**
  - `PascalCase` for classes, methods, properties, public fields
  - `_camelCase` for private fields (underscore prefix)
  - `camelCase` for local variables and parameters
- **Access modifiers:** Always explicit (`public`, `private`, `protected`, `internal`)
- **Nullable:** Enable nullable reference types (`#nullable enable` at file top)
- **File encoding:** UTF-8 without BOM

### Godot-Specific

- Use `_Ready()`, `_Process(delta)`, `_PhysicsProcess(delta)` overrides
- Use `[Export]` for inspector-exposed fields
- Use `GetNode<T>()` or `GetNodeOrNull<T>()` for node references
- Prefer `GD.Print()` over `Console.WriteLine()`
- Keep `_Process()` lightweight — use `_PhysicsProcess()` for physics

### Project Conventions

- One class per file, filename matches class name
- Namespace = folder path (e.g., `Scripts.Entities`, `SlopArena.Shared`)
- Keep `Shared/` free of Godot types — use plain C# structs and math
- Use `readonly struct` for packet and state types
- XML-doc public APIs

## Spell System Guide

Spells are the heart of SlopArena. Here's how they work:

### Spell Definition

Spells are defined in `Shared/SpellDefinition.cs`. Each spell has:

- **ID** — Unique integer identifier
- **Name** — Display name
- **Cooldown** — Seconds before reuse
- **Cast time** — Seconds of channel before effect
- **Duration** — Seconds the effect lingers
- **Shape** — How it hits: `FastProjectile`, `SlowProjectile`, `Beam`, `MeleeCone`, `DelayedAoE`, `Trap`
- **Handler** — Function pointer to the spell effect logic

### Adding a New Spell

1. Add an entry in `SpellSystem.cs` using `Register(id, name, cooldown, castTime, duration, handler)`
2. Implement the handler in the appropriate file:
   - `StatusSpells.cs` — Status-application and status-consumption spells
   - `RangedSpells.cs` — Projectile-based spells
   - `MeleeSpells.cs` — Melee cone spells
3. The handler receives `(Vector3 origin, Vector3 targetDir, int targetId, float chargeRatio, CombatComponent caster)` and returns a `SpellResult`

## How to Contribute

### Reporting Bugs

Open an issue with:
- A clear title and description
- Steps to reproduce
- Expected vs actual behavior
- Screenshots or video if applicable

### Suggesting Features

Open an issue with:
- What you want to achieve
- How it fits the game's design philosophy
- Rough implementation idea (optional but helpful)

### Code Contributions

1. Fork the repository
2. Create a branch: `git checkout -b feature/your-feature`
3. Make your changes
4. Test that the project still builds and runs
5. Submit a Pull Request

### Non-Code Contributions

- **Game design** — Propose new spells, balance changes, game modes
- **3D art** — Characters, animations, environment props
- **UI/UX** — HUD improvements, menu flow
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
