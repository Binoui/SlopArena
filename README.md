# SlopArena

**"The Melee of Battle Arenas"** — A high-execution, open-source 3D Arena Brawler.

SlopArena fuses the visceral movement of third-person action games with the tactical positioning and target management of competitive MMORPG PvP. Built with Godot 4 (.NET C#), it features a server-authoritative architecture with client-side prediction.

> **Status:** Early prototype. Core movement, combat, and spell systems are functional in a sandbox environment.

---

## Core Philosophy

- **PvP First** — No PvE, no farming, no laning. Pure player-vs-player skill.
- **Load & Play** — Classless system. Pick your abilities from a universal grimoire.
- **Open Source** — Built by the community, for the community.

---

## Features

### Movement System
- Velocity-decoupled air control (WoW-style momentum preservation)
- Dash with cooldown, chained into ground slides with momentum conservation
- Heightmap-based terrain with wall-jumping and slope collision
- Knockback with Directional Influence (DI) during hitstun
- Action state machine: Idle, Jogging, Dashing, Sliding, Attacking, Hitstun

### Combat & Spells
- **40 spells** in a universal grimoire, divided into 5 roles:
  - **Starter** — Apply status effects (Slow, Burn, Marked, Electrified, Vulnerable, Shielded)
  - **Extender** — Fast bridge attacks to keep combos alive
  - **Finisher** — Consume status effects for bonus damage
  - **Setup** — Zone control, traps, AoE denial
  - **Mobility** — Dashes, blinks, parries, cleanses
- 6 spell shapes: Fast/Slow Projectile, Beam (hitscan), MeleeCone, DelayedAoE, Trap
- Pure C# hit detection (CombatMath) — no Godot dependency, shared between client and server
- Damage, knockback, hitstun, and status effect application

### UI & Controls
- WoW-style camera (SpringArm3D with left-click orbit, right-click steer)
- Action bar with cooldown tracking (8 slots: 1-4, A, E, Shift, R)
- Spellbook UI for assigning spells to slots (drag & drop)
- Tab targeting / left-click targeting with targeting ring
- Unit frames (player + target HP bars)
- Dummy NPCs for sandbox testing (5 training dummies)

### Architecture
- **3-project .NET solution:** Godot client, Shared library, Headless server
- UDP-based server-authoritative simulation at 60Hz
- Client-side prediction with server reconciliation loop
- Pure C# combat logic in `Shared/` — usable by client, server, and AI
- Local simulation mode for sandbox testing (no server required)

---

## Dependencies

- **Godot Engine 4.6+ (.NET / Mono version)**
- **.NET SDK 8.0**

### Arch Linux / CachyOS
```bash
sudo pacman -S dotnet-sdk-8.0
```

Download the .NET version of Godot from [godotengine.org](https://godotengine.org).

---

## Project Structure

```
SlopArena/
├── project.godot          # Godot project config
├── SlopArena.sln            # .NET solution
├── SlopArena.csproj         # Godot C# client project
├── main.tscn              # Main arena scene (CSG-based)
│
├── Scripts/
│   ├── World/
│   │   ├── Main.cs              # Entry point, wires up all systems
│   │   └── HeightmapGenerator.cs
│   ├── Entities/
│   │   ├── PlayerController.cs  # CharacterBody3D with WoW-style movement
│   │   └── DummyManager.cs      # Sandbox training dummies
│   ├── Combat/
│   │   ├── LocalSimulation.cs   # Local combat simulation (projectiles, hit detection)
│   │   ├── CombatComponent.cs   # Per-entity combat state (HP, statuses, cooldowns)
│   │   ├── Projectile.cs        # Projectile visual
│   │   ├── ProjectileManager.cs # Object pool for projectile visuals
│   │   ├── Hitbox.cs / Hurtbox.cs
│   │   └── Fireball.cs
│   ├── Spells/
│   │   ├── SpellSystem.cs       # Slot binding, cooldown management, casting
│   │   ├── RangedSpells.cs      # Projectile-based spell effects
│   │   ├── MeleeSpells.cs       # Melee cone spell effects
│   │   └── StatusSpells.cs      # Status application effects
│   ├── UI/
│   │   ├── ActionBarHUD.cs      # Bottom action bar with cooldowns
│   │   ├── SpellBookUI.cs       # Full-screen spell browser
│   │   └── UnitFrames.cs        # Player + target HP bars
│   └── Camera/
│       └── WowCamera.cs         # SpringArm3D WoW-style camera
│
├── Shared/                      # Shared C# library (no Godot dependency)
│   ├── SlopArena.Shared.csproj
│   ├── PhysicsConfig.cs         # Physics constants, heightmap, SimulateStep
│   ├── MovementProfiles.cs      # Movement profile (Wizard default)
│   ├── ActionState.cs           # Action state enum
│   ├── ClientInputPacket.cs     # UDP packet: client → server
│   ├── CharacterStatePacket.cs  # UDP packet: server → client
│   ├── SpellDefinition.cs       # Spell catalog (40 spells), SpellCatalog
│   ├── SpellData.cs             # Runtime spell wrapper
│   ├── SpellResolver.cs         # Hit detection (projectile, cone, circle AoE)
│   ├── CombatMath.cs            # Math utilities (line-circle, cone, knockback)
│   ├── ProjectileState.cs       # Projectile position update (pure math)
│   ├── HitResult.cs             # Hit result struct
│   ├── StatusType.cs            # Status effect enum
│   └── SpellResolver.cs         # Spell resolution logic
│
├── Server/                      # Headless authoritative server
│   ├── SlopArena.Server.csproj
│   └── Program.cs               # UDP server, 60Hz physics loop
│
├── assets/                      # 3D models, animations, FBX files
├── textures/                    # Kenney prototype textures (CC0)
└── run_server.sh                # Helper to start the server
```

---

## Running the Project

### Sandbox Mode (no server required)
1. Open Godot (.NET version)
2. Import the project (`project.godot` at root)
3. Press **Play** (F5)

The sandbox runs a local simulation with 5 training dummies, a full spell system, and WoW-style controls.

### With the Authoritative Server
```bash
# Terminal 1: Start the physics server
./run_server.sh
# or: dotnet run --project Server/SlopArena.Server.csproj

# Terminal 2: Run the Godot client (connects to localhost:7777)
```

---

## Controls

| Input | Action |
|-------|--------|
| **ZQSD / WASD** | Movement (camera-relative) |
| **Space** | Jump |
| **Left Click (hold + drag)** | Orbital camera rotation |
| **Left Click (tap)** | Target entity under cursor |
| **Right Click (hold)** | Rotate camera + character |
| **Right Click (tap)** | Toggle mouse capture |
| **Scroll Wheel** | Zoom in/out |
| **Tab** | Cycle target (dummies) |
| **1 / 2 / 3 / 4** | Spell slots 1-4 |
| **A** | Spell slot 5 (Control) |
| **E** | Spell slot 6 (Utility) |
| **Shift** | Dash (Slot 7) |
| **R** | Spell slot 8 (Ultimate) |
| **B** | Toggle spellbook |
| **Escape** | Release mouse |

---

## Game Design

See [gdd.md](gdd.md) for the full Game Design Document covering:
- Core philosophy and design heritage
- Mobility chain mechanics
- Combo & hitstun architecture
- Spell typology and counterplay
- Game modes (FFA Deathmatch, Arena Sockets)

---

## Contributing

SlopArena is a **community-driven project** — everyone is welcome!

- **🐛 Found a bug?** [Open an issue](https://github.com/Binoui/SlopArena/issues/new?template=bug_report.yml)
- **💡 Have an idea?** [Submit a feature request](https://github.com/Binoui/SlopArena/issues/new?template=feature_request.yml)
- **🛠️ Want to code?** Read the [Contributing Guide](CONTRIBUTING.md) to get started
- **🎨 Designer / artist / writer?** Non-code contributions are just as valuable — see the guide

By participating, you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

### Quick Start for Contributors

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
# Open project.godot in Godot 4.6+ (.NET version) and press F5
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full guide on spells, architecture, and coding conventions.

## License

MIT — see [LICENSE](LICENSE).
