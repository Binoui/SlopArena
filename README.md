# SlopArena

**"The Melee of Battle Arenas"** — A high-execution, open-source 3D Arena Brawler.

SlopArena fuses the visceral movement of platform fighters (Smash Bros, DKO) with
character kits from hero brawlers. Built with **Godot 4.6 (.NET C#)**, it features
a data-driven character system with a tick-based simulation shared between client and server.

> **Status:** Early prototype. Core movement, combat, and 3 classes (Vanguard, Wraith, Channeler) are functional in sandbox mode.

---

## Core Philosophy

- **PvP First** — Pure player-vs-player skill. No PvE, no farming.
- **Small Character Kits** — Each class has 6 abilities (LMB, RMB, Q, E, R, F). Every ability is meaningful.
- **Data-Driven** — All character data (stats, ability stages, hitboxes) lives in `Shared/CharacterDefinition.cs`. Adding a new character means adding a factory function, not writing gameplay code.
- **Tick-Based Netcode Ready** — The simulation runs at 60Hz. Cooldowns, stuns, and durations are `ushort` ticks. Hit detection uses `CombatMath.cs` pure C# math — no Godot physics.

---

## Features

### Movement System (Platform Fighter)
- World-space movement (ZQSD = fixed NSEW, camera-independent)
- Ground friction, air acceleration with drag
- Dash (ground) with cooldown, cancelable by jump
- Air dodge (directional, limited resource)
- Sprint/dash-dance with turnaround lag
- Double jump, knockback with DI, tech roll
- Tick-based timers for all durations

### 3 Playable Classes (Data-Driven)
| Class | Style | Stats |
|-------|-------|-------|
| **Vanguard** | Heavy, slow, tanky | Walk 9, Sprint 12, Dash 30, 2 jumps |
| **Wraith** | Fast, light, hit-and-run | Walk 11, Sprint 15, Dash 35, 2 jumps |
| **Channeler** | Ranged, control, zone | Walk 10, Sprint 13, Dash 30, 2 jumps |

Each class has 6 data-defined abilities with:
- **Stages** — Melee cone, circle AoE, beam, projectile (damage, knockback, stun, chain window)
- **Charged variants** — Hold RMB for charged version
- **Special effects** — Status application, teleports, delayed AoE, projectile spawning

### Combat System
- All 6 ability slots use the same `AbilityData` struct — no distinction between basic attacks and class abilities
- Hit detection via `SpellResolver` (Shared/, pure C# math) — `ResolveConeHit`, `ResolveCircleHit`
- Tick-based stun, anim lock, and chain window for combos
- Airborne modifiers (RMB down spike in air)
- Status effects: Slowed, Vulnerable, Marked, Shielded, Burn, Electrified
- Knockback with directional influence

### UI & Controls
- WoW-style camera (SpringArm3D with mouse orbit + zoom)
- Action bar with tick-based cooldown display
- Tab targeting / left-click targeting with targeting ring
- Unit frames (player HP bars)
- Dummy NPCs for sandbox testing (5 training dummies)
- Escape menu with key rebinding

### Architecture
- **3-project .NET solution:** Godot client, Shared library, Headless server
- `Shared/` has **zero** Godot dependencies — usable by client, server, and AI
- `Simulation.SimulateTick()` in Shared/ processes one tick of movement + combat — pure C#
- `CharacterState` struct holds all entity state (pos, vel, action, cooldowns, HP)
- Tick-based timers everywhere (`ushort` counters decremented per tick)
- UDP-based server-authoritative model (server WIP)

---

## Dependencies

- **Godot Engine 4.6+ (.NET version)**
- **.NET SDK 8.0+**

### Arch Linux / CachyOS
```bash
sudo pacman -S dotnet-sdk
```

Download the .NET version of Godot from [godotengine.org](https://godotengine.org).

---

## Project Structure

```
SlopArena/
├── project.godot            # Godot project config
├── global.json              # .NET SDK version
├── main.tscn                # Main arena scene
│
├── Scripts/                 # Godot client-side scripts
│   ├── World/
│   │   ├── Main.cs               # Entry point, wires up all systems
│   │   └── ArenaManager.cs       # Arena loading, spawns, void death
│   ├── Entities/
│   │   ├── PlayerController.cs   # Thin orchestrator: input → movement → combat → animation
│   │   ├── AnimationController.cs# FBX loading, Mixamo path remapping, animation state machine
│   │   ├── ClassAbilities.cs     # Special effects for class abilities (status, projectiles)
│   │   └── DummyManager.cs       # Sandbox training dummies
│   ├── Combat/
│   │   ├── MovementComponent.cs  # Wraps CharacterState + Simulation for Godot
│   │   ├── CombatComponent.cs    # Per-entity combat state (HP, statuses, hit detection)
│   │   └── LocalSimulation.cs    # Entity registry + hit/status routing
│   ├── Characters/
│   │   └── AbilityRegistry.cs    # Maps string keys → ClassAbilities methods
│   ├── Spells/
│   │   └── StatusSpells.cs       # Visual helpers only (cone, circle, beam, impact)
│   ├── UI/
│   │   ├── ActionBarHUD.cs       # Bottom action bar with cooldowns
│   │   ├── UnitFrames.cs         # Player + target HP bars
│   │   ├── SettingsUI.cs         # Key rebinding
│   │   └── EscapeMenuUI.cs       # Pause menu
│   ├── Camera/
│   │   └── WowCamera.cs          # SpringArm3D WoW-style camera
│   └── Combat/ (additional)
│       ├── Hurtbox.cs / Hitbox.cs
│       └── ...
│
├── Shared/                     # Pure C# library (NO Godot dependency)
│   ├── SlopArena.Shared.csproj
│   ├── CharacterDefinition.cs  # Class stats, ability data, character registry
│   ├── AttackData.cs           # AbilityData, AttackStage structs
│   ├── CharacterState.cs       # Full per-tick entity state
│   ├── InputState.cs           # Bool input struct
│   ├── Simulation.cs           # SimulateTick() — movement + combat in pure C#
│   ├── CombatMath.cs           # IsInCircle, IsInCone, CalculateKnockback
│   ├── SpellResolver.cs        # ResolveConeHit, ResolveCircleHit
│   ├── ActionState.cs          # Action state enum
│   ├── StatusType.cs           # Status effect enum
│   ├── ArenaDefinition.cs      # Arena + spawn points
│   ├── ClientInputPacket.cs    # UDP packet: 14 bytes (client → server)
│   └── CharacterStatePacket.cs # UDP packet: 31 bytes (server → client)
│
├── Server/                     # Headless authoritative server (WIP)
│   ├── SlopArena.Server.csproj
│   └── Program.cs              # UDP server, 60Hz physics loop
│
├── assets/                     # 3D models, animations, FBX files
├── textures/                   # Kenney prototype textures (CC0)
└── run_server.sh               # Helper to start the server
```

---

## Running the Project

### Sandbox Mode (no server required)
1. Open Godot (.NET version)
2. Import the project (`project.godot` at root)
3. Press **Play** (F5)

The sandbox runs a local simulation with 5 training dummies, 3 playable classes, and WoW-style controls.

### With the Authoritative Server
```bash
# Terminal 1: Start the physics server
./run_server.sh

# Terminal 2: Run the Godot client (connects to localhost:7777)
```

---

## Controls

| Input | Action |
|-------|--------|
| **ZQSD / WASD** | Movement (world-space, camera-independent) |
| **Space** | Jump (double jump available) |
| **LMB** | Basic attack (3-hit combo) |
| **RMB** | Heavy attack (hold to charge) |
| **Q** | Class ability slot 3 |
| **E** | Class ability slot 4 |
| **R** | Class ability slot 5 |
| **F** | Class ability slot 6 |
| **Shift** | Dash / Air dodge |
| **C** | Crouch |
| **Scroll Wheel** | Zoom in/out |
| **Tab** | Cycle target (dummies) |
| **Escape** | Pause menu / release mouse |

---

## Adding a New Character

1. Add a `CharacterClass` enum value in `Shared/CharacterDefinition.cs`
2. Write a `BuildXxx()` factory function with `MovementStats` and 6 `AbilityData` slots
3. Register it in `BuildRegistry()`
4. If the character has unique special effects, add methods to `ClassAbilities.cs` and register them in `AbilityRegistry.cs`

No changes to `PlayerController`, `MovementComponent`, or `ExecuteSlot` needed — everything is data-driven.

---

## Game Design

See [gdd.md](gdd.md) for the full Game Design Document.

---

## Contributing

SlopArena is a **community-driven project** — everyone is welcome!

- **🐛 Found a bug?** [Open an issue](https://github.com/Binoui/SlopArena/issues/new)
- **💡 Have an idea?** Submit a feature request
- **🛠️ Want to code?** Read the [Contributing Guide](CONTRIBUTING.md) to get started
- **🎨 Designer / artist / writer?** Non-code contributions are just as valuable

By participating, you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

### Quick Start

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
# Open project.godot in Godot 4.6+ (.NET version) and press F5
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full guide.

## License

MIT — see [LICENSE](LICENSE).
