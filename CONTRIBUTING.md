# Contributing to SlopArena

First off, thanks for being here! SlopArena is a community-driven project — every contribution, whether code, design, documentation, or bug reports, makes the arena better for everyone.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Project Architecture](#project-architecture)
- [Coding Guidelines](#coding-guidelines)
- [Adding a New Character](#adding-a-new-character)
- [How to Contribute](#how-to-contribute)
- [Pull Request Process](#pull-request-process)

## Code of Conduct

This project is governed by the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating, you agree to uphold a harassment-free, inclusive environment for everyone.

## Getting Started

SlopArena is a Unity 6 C# project. You'll need:

- **Unity 6000.0.47f1** — from [Unity Hub](https://unity.com/download)
- **.NET SDK 8.0+** — `sudo pacman -S dotnet-sdk` (Arch/CachyOS) or from [dotnet.microsoft.com](https://dotnet.microsoft.com)

### Clone and Run

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

1. Open `client/Unity/` in Unity Hub and click **Play**
2. Build shared code with `dotnet build src/Shared/` (auto-copies DLL to Unity Plugins)

## Project Architecture

```
SlopArena/
├── src/Shared/              ← Pure C# library (netstandard2.1, zero Unity deps)
│   ├── Simulation.cs        ← SimulateTick(): movement + combat
│   ├── SpellResolver.cs     ← Hit detection math
│   ├── CharacterState.cs    ← Per-tick entity state
│   ├── CharacterDefinition.cs ← Data-driven characters
│   ├── Abilities/           ← ServerAbility implementations
│   └── Characters/          ← MankiData, BunnyData
├── client/Unity/            ← Unity game client
│   └── Assets/Plugins/SlopArena.Shared/  ← Imported DLL (build artifact)
├── tests/Shared.Tests/      ← xUnit simulation tests
├── docs/                    ← Design docs, research, conventions
├── data/                    ← Baked skeleton data, arenas
└── tools/                   ← Asset pipeline scripts
```

### Key Design Decisions
- **Data-driven characters** — All stats and abilities in `CharacterRegistry` (CharacterDefinition.cs). Adding a new character is adding data, not gameplay code.
- **Tick-based simulation** — All timers are `ushort` ticks decremented at 60Hz. No `float -= delta` for gameplay.
- **Platform fighter movement** — World-space (camera-independent), instant directional speed on ground, air acceleration + drag, dash/air-dodge with resources
- **Unified ability system** — All 6 slots (LMB/RMB/Q/E/R/F) use the same `AbilityData` struct. Stages handle hit detection via SpellResolver, SpecialEffectKeys handle complex behavior.

### Data Flow

```
_PhysicsProcess
  → BuildInputState() → SlopArena.Shared.InputState
  → PlayerController.ExecuteSlot(slotIndex, charged, airborne)
      → _charDef.GetSlotAbility(slotIndex) → AbilityData
      → Stages → ResolveAbilityStages() → SpellResolver (Shared/)
      → SpecialEffectKeys → AbilityRegistry.Execute() → ClassAbilities
      → Set cooldown in CharacterState
  → MovementComponent.Tick(input)
      → sync Godot body → CharacterState
      → Simulation.SimulateTick(ref State, def, input, arena)
      → apply State → Godot body → MoveAndSlide()
  → AnimationController.ProcessAnimation()
```

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

### Unity-Specific

- Use `MonoBehaviour` lifecycle: `Awake()`, `Start()`, `Update()`, `FixedUpdate()`
- Use `[SerializeField]` for inspector-exposed fields, `[Tooltip]` for descriptions
- Use `GetComponent<T>()` or `FindAnyObjectByType<T>()` for component lookups
- Prefer `Debug.Log()` for debug output
- Keep `Update()` lightweight — use `FixedUpdate()` for physics and sim ticks

### Debug Logging

**Use sparingly.** Too many logs hurt performance and make debugging harder.

**When to log:**
- Critical errors (`GD.PrintErr()`)
- Gameplay events (respawn, death, level transitions)
- Missing FSM/AnimationTree setup (once per entity)

**When NOT to log:**
- Per-frame or per-tick operations
- Successful initialization ("Camera acquired!", "Loaded model")
- Attack execution ("First attack melee", "Combo chain to X")
- Target lock updates ("Locked onto NPC_4")

**Instead of logs, use:**
- Visual debug tools (`DebugHitboxDraw`, F3 toggle)
- In-game UI (damage numbers, status icons)
- Godot debugger breakpoints

**Before committing:** Review your diffs and remove temporary debug logs.

### Project Conventions

- One class per file, filename matches class name
- No explicit namespace for Godot scripts (global namespace is fine)
- `SlopArena.Shared` namespace for Shared/ files
- Keep `Shared/` free of Godot types — use plain C# structs and math
- XML-doc public APIs, especially in Shared/

### Netcode Rules (Tick-Based 60Hz)

SlopArena is built for a server-authoritative model with client-side prediction.

**Shared/ is sacred.** No Godot imports, no `Vector3`, no `GetWorld3D().DirectSpaceState` — EVER in Shared/. All hit detection uses `CombatMath.cs` + `SpellResolver.cs` pure math.

**Tick-based timers, not delta.**
```csharp
ushort cooldownTicks = 90; // 1.5s at 60Hz
if (cooldownTicks > 0) cooldownTicks--;
```
NOT `float -= delta`. All durations in `CharacterState` are `ushort`.

**CharacterState is the authority.** Position, velocity, action state, cooldowns, HP — everything lives in `CharacterState`. The Godot body mirrors it for rendering.

**Visuals are client-only.** Rendering, particles, sounds, animations never affect game state. State comes from Shared/ simulation and is authoritative on the server.

**Pure math for hit detection.**
```csharp
// Server-side: no Godot physics
var results = SpellResolver.ResolveConeHit(posX, posY, posZ, dirX, dirZ, halfAngle, range, ...);
```

## Adding a New Character

Characters are defined entirely in `Shared/CharacterDefinition.cs`. Here's the process:

1. **Add enum value** — Add your class to `CharacterClass` enum
2. **Write factory** — Add a `BuildXxx()` method to `CharacterRegistry`:
   ```csharp
   private static CharacterDefinition BuildMyClass()
   {
       return new CharacterDefinition
       {
           Class = CharacterClass.MyClass,
           DisplayName = "My Class",
           Movement = new MovementStats { ... },
           LMB = new AbilityData { Name = "Combo", Stages = new[] { ... } },
           RMB = new AbilityData { Name = "Heavy", Stages = new[] { ... }, ChargedStages = new[] { ... } },
           Q = new AbilityData { Name = "Special", SpecialEffectKeys = new[] { "MySpecialEffect" } },
           // E, R, F slots...
       };
   }
   ```
3. **Register** — Add `BuildMyClass()` to `BuildRegistry()`
4. **Special effects** — If your ability needs complex behavior (teleport, delayed AoE, status apply), add a method to `ClassAbilities.cs` and register it in `AbilityRegistry.cs`

### AbilityData Fields

```csharp
public struct AbilityData
{
    public string Name;              // Display name
    public ushort CooldownTicks;     // 0 = no cooldown
    
    // Hit detection stages (resolved via SpellResolver)
    public AttackStage[] Stages;         // Shape, damage, range, knockback, stun, etc.
    public AttackStage[]? ChargedStages; // Hold-to-charge variant
    public ushort ChargeHoldTicks;
    
    // Complex behavior (called AFTER stage resolution)
    public string[]? SpecialEffectKeys;  // References AbilityRegistry
}
```

### AttackStage Fields

```csharp
public struct AttackStage
{
    public AttackShape Shape;        // MeleeCone, CircleAOE, Projectile, Beam, SelfBuff
    public float Damage, Range, HitAngleDeg, Radius;
    public float BaseKnockback, KnockbackGrowth, KnockbackUpward, LungeForce;
    public ushort StunTicks, SelfLockTicks, ChainWindowTicks;
}
```

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

- **Game design** — Propose new characters, abilities, game modes
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
