# SlopArena

<p align="center">
  <img src="https://img.shields.io/badge/Unity-6000-FFFFFF?logo=unity&logoColor=white" alt="Unity 6000">
  <img src="https://img.shields.io/badge/C%23-.NET%208-512BD4?logo=dotnet&logoColor=white" alt="C# .NET 8">
  <img src="https://img.shields.io/github/license/Binoui/SlopArena" alt="License">
  <img src="https://img.shields.io/badge/status-playable-2ea043" alt="Status">
</p>

**Brawl your way to the top.** — An open-source 3D Arena Brawler with platform fighter movement.

SlopArena fuses platform fighter movement with
character kits from hero brawlers. Built with **Unity 6000**,
featuring a data-driven character system with a tick-based simulation
shared between client and server.

> **Status:** Playable prototype — movement, combat, and **Manki** (mad bomber monkey) are functional in training mode.

---

## Quick Start

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

1. Install **Unity 6000.0.47f1** from [Unity Hub](https://unity.com/download)
2. Install **.NET SDK 8.0+** (`sudo pacman -S dotnet-sdk` on Arch)
3. Open `client/Unity/` in Unity Hub and press **Play**

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
| **Shift** | Dash |
| **Tab** | Cycle target |
| **Scroll Wheel** | Zoom |
| **Escape** | Pause menu |

---

## Architecture Overview

```
┌─ Unity Client ─────────────────────────────┐
│  TrainingMatch (orchestrator)               │
│   ├─ InputController (polling)              │
│   ├─ LocalSimulationBridge (wraps sim)      │
│   ├─ PlayerRenderer (drives Animator)       │
│   └─ CombatFeedback (hit reactions, VFX)   │
├─ Shared/ (pure C#, zero Unity deps) ────────┤
│  CharacterDefinition → AbilityData → Stages │
│  Simulation.SimulateTick()                  │
│  SpellResolver (hit detection)              │
│  CharacterState (pos, vel, cooldowns...)    │
├─ Server/ (headless) ────────────────────────┤
│  UDP loop at 60Hz                           │
└─────────────────────────────────────────────┘
```

Key design decisions:
- **Data-driven characters** — all stats, abilities, and animations live in `CharacterDefinition.cs`. Adding a new character = writing a factory function.
- **Tick-based everything** — durations are `ushort` ticks (1/60s). Netcode-ready.
- **Server-authoritative** — simulation is the source of truth. Client predicts, server reconciles.
- **Pure C# Shared/** — no Unity dependencies in the core library. Runnable in tests and server independently.

---

## Project Structure

```
SlopArena/
├── client/Unity/            # Unity game client
├── src/
│   ├── Shared/              # Pure C# library (no Unity deps)
│   │   ├── Simulation.cs    # SimulateTick() — movement + combat
│   │   ├── SpellResolver.cs # Hit detection math
│   │   └── CharacterState.cs# Per-tick entity state
│   ├── Server/              # Headless server
│   └── ServerApp/           # Server host (prototype)
├── tests/                   # xUnit simulation tests
├── assets/                  # 3D models, textures, UI source
├── data/                    # Skeleton data, arenas
├── tools/                   # Asset pipeline scripts
└── docs/                    # Design docs, research, conventions
```

---

## Current Roster

| Character | Style | Abilities |
|-----------|-------|-----------|
| **Manki** | Agile rushdown / mad bomber | 3-hit melee combo, aerosol flamethrower, round bomb, dynamite jump, dive bomb, overclock ult |

See `docs/characters/manki.md` for the full kit.

---

## Adding a Character

1. Add `CharacterClass` enum value in `Shared/CharacterDefinition.cs`
2. Write a `BuildXxx()` factory with `MovementStats` + 8 `AbilityData` slots
3. Register it in `BuildRegistry()`
4. Add CharacterAnimationConfig ScriptableObject in Unity

No changes to gameplay code needed — everything is data-driven.

Full guide: [`docs/characters/adding-a-new-character.md`](docs/characters/adding-a-new-character.md)

---

## Documentation

> Full index: [`docs/README.md`](docs/README.md)

| Section | Key docs |
|---------|----------|
| **Orientation** | [`docs/architecture-overview.md`](docs/architecture-overview.md), [`docs/testing.md`](docs/testing.md) |
| **Systems** | [`docs/systems/combat-systems.md`](docs/systems/combat-systems.md), [`docs/systems/animation-system.md`](docs/systems/animation-system.md), [`docs/systems/netcode-architecture.md`](docs/systems/netcode-architecture.md) |
| **Characters** | [`docs/characters/manki.md`](docs/characters/manki.md), [`docs/characters/bunny.md`](docs/characters/bunny.md), [`docs/characters/adding-a-new-character.md`](docs/characters/adding-a-new-character.md) |
| **Contributing** | [`docs/contributing/conventions.md`](docs/contributing/conventions.md), [`docs/contributing/quality.md`](docs/contributing/quality.md) |

---

## AI Usage

The name *SlopArena* is obviously ironic, but I don't intend for this to be a low quality project.

**Code.** I've been a software developer for years. I wrote code before AI assistants existed, and I still do. At work, ~90% of my output is agent-written, reviewed, and shipped, and it's quality code. I don't think I'm an exception — this is the state of the industry as of today. Agentic coding is a powerful tool, especially when you know what you're doing. SlopArena is built exactly the way I work: using agents to think, plan, document, and write code. That's how I've been able to build a scalable game architecture while actively learning game development at the same time.

**Assets.** I used a few platforms to generate art assets. I'm busy with code, I've never had any artistic talent, and although I've tried, it's really not my thing. I don't think AI-generated assets are *better* — they're often unoptimized, generic, and soulless. But they're a quick and cheap way to get something on screen, and I don't have a problem with that tradeoff.

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
