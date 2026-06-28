# Architecture Overview — Codebase Map

> **For agents and new contributors.** Read this first — it tells you where everything lives.
> For netcode theory, see `docs/systems/netcode-architecture.md`. For art/naming, see `docs/contributing/conventions.md`.

---

## Directory Map

```
SlopArena/
├── Shared/              ← Pure C# library. Compiled to net8.0 DLL for Unity.
│   ├── Abilities/           ← ServerAbility implementations (pure C#, no Unity deps)
│   │   ├── ServerAbility.cs     ← Base class: OnStart/Tick/OnEnd lifecycle
│   │   ├── AbilityFactory.cs    ← Maps AbilityTypeId to concrete implementations
│   │   ├── MankiLmbCombo.cs     ← Manki LMB: 3-hit combo with lunge
│   │   ├── MankiRoundBomb.cs    ← Manki Q: parabolic projectile
│   │   └── MankiAerosolFlame.cs ← Manki RMB: hold-to-charge flamethrower
│   ├── Simulation.cs        ← SimulateTick(): one tick of movement + combat
│   ├── SpellResolver.cs     ← Hitbox spawn/Tick: sphere-capsule collision math
│   ├── CharacterState.cs    ← Per-tick entity state (pos, vel, cooldowns, deaths)
│   ├── CharacterDefinition.cs ← Data-driven characters: stats, abilities, hitboxes
│   ├── AttackData.cs        ← HitboxEvent, AttackStage, AbilityData structs
│   ├── CombatMath.cs        ← Knockback, facing, damage scaling
│   ├── ServerSimulation.cs  ← Wraps Simulation + SpellResolver for server tick
│   ├── CharacterStatePacket.cs ← UDP packet (39 bytes), FromState/Serialize
│   ├── ClientInputPacket.cs ← Client→server input (14 bytes)
│   ├── InputState.cs        ← Normalized input (MoveX/Y, flags, ActiveSlot)
│   ├── BakedAnimationData.cs← Offline-baked bone positions per frame
│   ├── ServerSkeleton.cs    ← FBX/GLB JSON parser for skeleton data
│   ├── ArenaDefinition.cs   ← Arena data (platforms, spawns, kill height)
│   └── MovementProfiles.cs  ← (deleted — dead code)
│
├── client/Unity/         ← Unity client (replacing old Godot client)
│   └── Assets/Scripts/
│       ├── Runtime/
│       │   ├── Entities/       ← PlayerRenderer (MonoBehaviour, drives Animator)
│       │   ├── World/          ← TrainingMatch, MatchBase (match orchestration)
│       │   ├── Input/          ← InputController (Unity Input → InputState)
│       │   ├── Camera/         ← CameraMount (orbit cam)
│       │   ├── Simulation/     ← LocalSimulationBridge (wraps ServerSimulation)
│       │   └── Animation/      ← CharacterAnimationConfig (ScriptableObject)
│       ├── Editor/
│       │   └── SlopArenaAnimatorGenerator.cs ← Generates AnimatorControllers
│       └── Shared/         ← Mirrors src/Shared/ for Unity compilation
│           ├── Characters/     ← MankiData, BunnyData (CharacterRegistry)
│           └── Abilities/      ← AbilityFactory, ServerAbility implementations
│
├── src/
│   ├── Shared/            ← Source for Shared/ (compiled via dotnet build)
│   ├── Server/            ← Headless .NET server (MatchInstance, UDP loop)
│   └── ServerApp/         ← Prototype test server
│
├── tests/
│   └── Shared.Tests/      ← xUnit tests (ServerSimulation, SpellResolver, etc.)
│
├── docs/                 ← All documentation
├── data/                 ← Baked binary data (.arena, _skeleton.bin)
└── tools/                ← Python scripts, build tools
```

---

## Data Flow (one tick at 60Hz)

```
┌─ CLIENT (Unity) ──────────────────────────────────┐
│                                                     │
│  InputController.Poll() → InputState                │
│       │                                             │
│       ▼                                             │
│  MatchBase.FixedUpdate()                            │
│    ├── TrainingMatch.OnMatchFixedUpdate()            │
│    │   ├── InputController.ConsumePendingSlotPress()│
│    │   ├── InputController.BuildInputState()        │
│    │   ├── LocalSimulationBridge.Tick(inputs)       │
│    │   │   └── ServerSimulation.Tick()              │
│    │   │       ├── SimulateMovement()               │
│    │   │       ├── SimulateAbilities()              │
│    │   │       └── SpellResolver.Tick()             │
│    │   └── PlayerRenderer.ApplyServerState(state)   │
│    │       ├── transform.position/rotation          │
│    │       └── UpdateAnimationState()               │
│    │           ├── SetBool("IsGrounded", ...)       │
│    │           ├── SetFloat("Speed", ...)           │
│    │           ├── SetTrigger("Attack"/"Dash"/etc)  │
│    │           └── Animator transitions naturally   │
│    └── (future) NetworkClient send/receive          │
└─────────────────────────────────────────────────────┘
```

---

## Key Naming Conventions

| Convention | Meaning | Example |
|------------|---------|---------|
| `PX, PY, PZ` | World position (Y=up) | `state.PX` |
| `VX, VY, VZ` | World velocity | `state.VY` (jump velocity) |
| `ushort` durations | ALL durations in ticks (1/60s) | `DashCooldownTicks = 56` |
| `_fieldName` | Private instance field | `_serverTick` |
| `EntityId` | `ulong` unique ID per entity | player=1, opponent=2, NPCs=100+ |
| `Tick` suffix | Duration in ticks | `StunTicks`, `DurationTicks` |
| `Def` suffix | Definition struct | `_charDef`, `HurtboxBoneDef` |

---

## Changing Gameplay Data

### Tune a character's stats
→ `Shared/Characters/MankiData.cs` or `BunnyData.cs`
- `Movement` struct: speed, jump, gravity, dash
- `HurtboxBoneDefs[]`: bone-attached hurtbox spheres
- `LMB/RMB/Q/E/R/F` abilities: `AbilitySpec` with `AbilityTypeId` and `Params`

### Tune a specific ability's behavior
→ `Shared/Characters/MankiData.cs` → the ability's `Params` dictionary
- Tunable parameters like `lunge_duration`, `explosion_damage`, `charge_threshold`
- No code recompilation needed for balance changes
- Logic lives in `Shared/Abilities/<CharacterName><AbilityName>.cs`

### Tune a specific ability's hitbox
→ `Shared/Characters/MankiData.cs` → the ability's `Stages[].HitboxEvents[]`
- `TriggerTick`: when during the animation the hitbox spawns
- `DurationTicks`: how long it lives
- `Radius`: hitbox size (sphere) or capsule radius
- `OffX/OffY/OffZ`: offset from character center (rotated by facing)
- `Damage`, `KnockbackForce`, `KnockbackUpward`, `StunTicks`

### Add a new ability effect (VFX)
→ `Scripts/Characters/AbilityRegistry.cs` — register the effect key
→ `Scripts/VFX/` — create a new VFX class
→ `CharacterDefinition.cs` — add `SpecialEffectKeys` to the ability

### Add a new character
→ Full guide: `docs/characters/adding-a-new-character.md`
→ Quick version: add `CharacterClass` enum value → write `BuildXxx()` → register in `BuildRegistry()`

---

## Common Pitfalls

1. **Don't use `Godot.` in `Shared/`** — it breaks the pure C# contract. Use `System.MathF`.
2. **Durations are `ushort` ticks, not `float` seconds** — `_timer -= delta` is wrong.
3. **Don't modify `CharacterDefinition.cs` values without understanding them** — they're the source of truth for balance and hit registration.
4. **`MatchManager` is hybrid** — it supports both sandbox (NPCs) and PvP (opponent). Future: split into `TrainingMatch` and `PvPMatch`.
5. **`ServerApp/` and `Server/` are two different servers** — `Server/` is the real one (`MatchInstance`). `ServerApp/` is a prototype stub. Use `Server/`.

---

## Verifying Changes

```bash
# Build all projects (Shared + Godot client + Server)
dotnet build --nologo

# Run ALL simulation unit tests (63+ tests: physics, abilities, combat, edges)
dotnet test tests/Shared.Tests/ --nologo

# Run a specific test category
dotnet test tests/Shared.Tests/ --nologo --filter "PhysicsTests|AbilityLifecycle"

# Run linter
make lint

# Run sandbox (Godot editor → F5)

# Run headless server (separate terminal)
dotnet run --project Server/SlopArena.Server.csproj
```

> The simulation tests are the **first thing to run** after any `src/Shared/` change.
> They validate state transitions, ability lifecycles, and hit detection without
> needing Godot or a server. Build & test together takes <3 seconds.

---

## Related Docs

| Doc | Covers |
|-----|--------|
| `docs/systems/animation-system.md` | Unity Animator: 1-layer trigger-driven, generator tool, pitfalls |
| `docs/systems/netcode-architecture.md` | Server-authoritative model, prediction, reconciliation |
| `docs/systems/ability-architecture.md` | ServerAbility pattern, lifecycle, creating new abilities |
| `docs/systems/combat-systems.md` | Universal combat mechanics |
| `docs/contributing/conventions.md` | Art direction, animation naming, bone naming |
| `docs/characters/adding-a-new-character.md` | Full pipeline for new characters |
| `CLAUDE.md` | Coding rules (Shared/ purity, tick-based, no Godot physics) |
