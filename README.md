# SlopArena

**"The Melee of Battle Arenas"** — A high-execution, open-source 3D Arena Brawler.

SlopArena fuses the visceral movement of platform fighters (Smash Bros, DKO) with
character kits from hero brawlers. Built with **Godot 4.6 (.NET C#)**, it features
a data-driven character system with a tick-based simulation shared between client and server.

> **Status:** Playable prototype — movement, combat, custom FSM, and **Manki** (mad bomber monkey) are functional in sandbox mode.

---

## Quick Start

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

1. Install **Godot 4.6+ (.NET version)** from [godotengine.org](https://godotengine.org)
2. Install **.NET SDK 8.0+** (`sudo pacman -S dotnet-sdk` on Arch)
3. Open `project.godot` in Godot and press **F5**

No server required — the sandbox runs everything locally with training dummies.

---

## Controls

| Input | Action |
|-------|--------|
| **ZQSD / WASD** | Movement (camera-relative) |
| **Space** | Jump (double jump available) |
| **LMB** | Light attack (3-hit combo) |
| **RMB** | Heavy attack (hold to charge) |
| **Q / E / R** | Abilities |
| **F** | Ultimate |
| **Shift** | Dash / Air dodge |
| **Tab** | Cycle target |
| **Scroll Wheel** | Zoom |
| **Escape** | Pause menu |

---

## Architecture Overview

```
┌─ Godot Client ──────────────────────────────┐
│  PlayerController (orchestrator)            │
│   ├─ InputController (centralized polling)  │
│   ├─ MovementComponent (wraps Simulation)   │
│   ├─ Custom C# FSM (StateMachine.cs)        │
│   │   ├─ IdleState / RunState               │
│   │   ├─ AirState (BlendSpace1D jump↔fall)  │
│   │   ├─ LandingState / AttackState         │
│   │   └─ AnimTree (flat StateMachine root)  │
│   └─ CombatComponent (statuses, hit routing)│
├─ Shared/ (pure C#, zero Godot deps) ────────┤
│  CharacterDefinition → AbilityData → Stages │
│  Simulation.SimulateTick()                  │
│  SpellResolver (cone/circle/beam hits)      │
│  CharacterState (pos, vel, cooldowns...)    │
├─ Server/ (headless, WIP) ────────────────── │
│  UDP loop at 60Hz                           │
└─────────────────────────────────────────────┘
```

Key design decisions:
- **Data-driven characters** — all stats, abilities, and animations live in `CharacterDefinition.cs`. Adding a new character = writing a factory function.
- **Tick-based everything** — durations are `ushort` ticks (1/60s). Netcode-ready.
- **Queue-based input buffer** (max 2) for responsive LMB combos, like souls-like FSM.
- **InputController** centralizes input polling — states never call `Input.Get*()` directly.
- **All animation states wrapped in BlendTree+TimeScale** for runtime speed control.

---

## Project Structure

```
SlopArena/
├── project.godot / global.json
├── main.tscn
├── Scripts/
│   ├── Animation/           # Custom FSM (State.cs, StateMachine.cs, States/)
│   ├── Entities/            # PlayerController, AnimationController, DummyManager
│   ├── Combat/              # MovementComponent, CombatComponent, LocalSimulation
│   ├── InputController.cs   # Centralized input (Jump, Dash)
│   ├── Camera/              # CameraMount (h/v SpringArm orbit)
│   ├── UI/                  # ActionBarHUD, UnitFrames, Settings, EscapeMenu
│   └── World/               # Main.cs entry point, ArenaManager
├── Shared/                  # Pure C# library (no Godot)
│   ├── CharacterDefinition  # Stats, abilities, character registry
│   ├── Simulation.cs        # SimulateTick() — movement + combat
│   ├── SpellResolver.cs     # Hit detection math
│   └── CharacterState.cs    # Per-tick entity state
├── Server/                  # Headless server (WIP)
├── assets/                  # 3D models, animations
└── docs/                    # Design docs, research, conventions
```

---

## Current Roster

| Character | Style | Abilities |
|-----------|-------|-----------|
| **Manki** | Agile rushdown / mad bomber | 3-hit melee combo, aerosol flamethrower, round bomb, dynamite jump, dive bomb, big boom ult |

See `docs/characters/manki.md` for the full kit.

---

## Adding a Character

1. Add `CharacterClass` enum value in `Shared/CharacterDefinition.cs`
2. Write a `BuildXxx()` factory with `MovementStats` + 8 `AbilityData` slots
3. Register it in `BuildRegistry()`
4. If it needs special effects, add to `AbilityRegistry.cs`

No changes to gameplay code needed — everything is data-driven.

Full guide: [`docs/adding-a-new-character.md`](docs/adding-a-new-character.md)

---

## Documentation

| Doc | What it covers |
|-----|---------------|
| [`docs/characters/manki.md`](docs/characters/manki.md) | Manki kit, concept, design notes |
| [`docs/animation-system.md`](docs/animation-system.md) | FSM lifecycle, AnimationTree structure |
| [`docs/combat-systems.md`](docs/combat-systems.md) | Universal combat mechanics |
| [`docs/character-kit-design-principles.md`](docs/character-kit-design-principles.md) | Design rules for abilities |
| [`docs/conventions.md`](docs/conventions.md) | Art direction, animation naming, bone naming |
| [`docs/adding-a-new-character.md`](docs/adding-a-new-character.md) | Full pipeline guide |
| [`docs/research/dko-mechanics.md`](docs/research/dko-mechanics.md) | DKO systems reference |

---

## Contributing

SlopArena is a **community-driven project** — everyone is welcome!

- **🐛 Found a bug?** [Open an issue](https://github.com/Binoui/SlopArena/issues/new)
- **💡 Have an idea?** Submit a feature request
- **🛠️ Want to code?** Check the docs above, then open a PR
- **🎨 Designer / artist / writer?** Non-code contributions are just as valuable

We use **Roslynator analyzers** for C# linting — `dotnet build` will show warnings.

By participating, you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

---

## License

MIT — see [LICENSE](LICENSE).
