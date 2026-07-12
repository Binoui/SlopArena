# Architecture Overview — Codebase Map

> **For agents and new contributors.** Read this first — it tells you where everything lives.
> For netcode theory, see `docs/systems/netcode-architecture.md`. For art/naming, see `docs/contributing/conventions.md`.

---

```
SlopArena/
├── client/Unity/Assets/Scripts/Shared/  ← REAL files (Unity compiles these directly)
│   ├── Characters/     ← MankiData, FightGuyData (CharacterRegistry)
│   ├── Abilities/      ← AbilityFactory, ServerAbility implementations
│   ├── Simulation.cs   ← SimulateTick(): one tick of movement + combat
│   ├── SpellResolver.cs← Hitbox spawn/Tick: sphere-capsule collision math
│   ├── CharacterState.cs← Per-tick entity state (pos, vel, cooldowns, deaths)
│   ├── CharacterDefinition.cs ← Data-driven characters: stats, abilities, hitboxes
│   ├── AttackData.cs   ← HitboxEvent, AttackStage, AbilityData structs
│   ├── CombatMath.cs   ← Knockback, facing, damage scaling
│   ├── ServerSimulation.cs ← Wraps Simulation + SpellResolver for server tick
│   ├── CharacterStatePacket.cs ← UDP packet (39 bytes), FromState/Serialize
│   ├── ClientInputPacket.cs ← Client->server input (14 bytes)
│   ├── InputState.cs   ← Normalized input (MoveX/Y, flags, ActiveSlot)
│   ├── BakedAnimationData.cs← Offline-baked bone positions per frame
│   ├── ServerSkeleton.cs← FBX/GLB JSON parser for skeleton data
│   ├── ArenaDefinition.cs← Arena data (platforms, spawns, kill height)
│   └── MovementProfiles.cs← (deleted — dead code)
│
├── src/Shared/          ← SYMLINKS → client/Unity/Assets/Scripts/Shared/ (shared code root)
│   ├── Abilities/           ← ServerAbility implementations
│   │   ├── ServerAbility.cs     ← Base class: OnStart/Tick/OnEnd lifecycle
│   │   ├── AbilityFactory.cs    ← Maps AbilityTypeId to concrete implementations
│   │   ├── MankiLmbCombo.cs     ← Manki LMB: 3-hit combo with lunge
│   │   ├── MankiRoundBomb.cs    ← Manki Q: hold-to-aim parabolic bomb
│   │   ├── MankiAerosolFlame.cs ← Manki RMB: hold-to-charge flamethrower
│   │   ├── MankiBazooka.cs      ← Manki R: rise-aim-fire bazooka
│   │   └── MankiOverclock.cs    ← Manki F: self-buff 8s
│   ├── Simulation.cs        ← shared logic
│   ├── SpellResolver.cs     ← hitbox collision math
│   ├── CharacterState.cs    ← entity state
│   └── ... (all files are symlinks to client/Unity/Assets/Scripts/Shared/)
│
├── client/Unity/         ← Unity game client
│   └── Assets/Scripts/
│       ├── Runtime/
│       │   ├── Entities/       ← PlayerRenderer, StatusBillboard, WeaponAttach
│       │   ├── World/          ← MatchBase, TrainingMatch, PvPMatch (match orchestration)
│       │   ├── Simulation/     ← LocalSimulationBridge, NetworkSimulationBridge, ISimulationBridge
│       │   ├── Network/        ← NetworkClient (UDP, Connect/SendInput/ReceiveStates)
│       │   ├── UI/             ← MatchConfig, MainMenuController, LobbyController, CharSelectController, StageSelectController, HUDManager
│       │   ├── Input/          ← InputController (Unity Input → InputState)
│       │   ├── Camera/         ← CameraMount, AimCameraMount (orbit + aim camera)
│       │   ├── Combat/         ← CombatFeedback, AimHandler, AimIndicator
│       │   └── Animation/      ← CharacterAnimationConfig (ScriptableObject)
│       ├── Editor/
│       │   └── SlopArenaAnimatorGenerator.cs ← Generates AnimatorControllers
│       └── Shared/         ← REAL FILES (source of truth for Shared code)
│           ├── Characters/     ← MankiData, FightGuyData
│           ├── Abilities/      ← AbilityFactory, ServerAbility impls
│           └── ... (all Shared code)
│
├── src/
│   ├── Shared/            ← SYMLINKS to client/Unity/Assets/Scripts/Shared/
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

┌─ CLIENT (Unity) ──────────────────────────────────────────────┐
│                                                                  │
│  MainMenu → Lobby → CharSelect → StageSelect                    │
│       ↓ (MatchConfig: Mode, PlayerClass, ArenaName, ServerIP)   │
│  MatchBase.Start() → OnMatchStart()                             │
│    ├── TrainingMatch  → LocalSimulationBridge.Tick(inputs)      │
│    │   └── ServerSimulation.Tick()                              │
│    │       ├── PreTickAbilities() / SimulateMovement()           │
│    │       └── SpellResolver.Tick()                             │
│    └── PvPMatch  → NetworkSimulationBridge.Tick(inputs)         │
│                     ├── NetworkClient.SendInput()               │
│                     └── NetworkClient.ReceiveStates()           │
│                                                                  │
│  InputController.Poll() → InputState                            │
│  PlayerRenderer.ApplyServerState(state)                         │
│       └── UpdateAnimationState() → Animator transitions         │
└──────────────────────────────────────────────────────────────────┘

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
→ `Shared/Characters/MankiData.cs` or `FightGuyData.cs`
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
## Common Pitfalls

1. **Don't use `UnityEngine.*` in `Shared/`** — it breaks the pure C# contract. Use `System.MathF`.
2. **Durations are `ushort` ticks, not `float` seconds** — `_timer -= delta` is wrong.
3. **Don't modify `CharacterDefinition.cs` values without understanding them** — source of truth for balance and hit registration.
4. **`ServerApp/` and `Server/` are two different servers** — `Server/` is the real one (`MatchInstance`). `ServerApp/` is a prototype stub. Use `Server/`.
5. **`Shared/` is built as a netstandard2.1 DLL** — run `dotnet build src/Shared/` after editing Shared code. Auto-copies to `client/Unity/Assets/Plugins/SlopArena.Shared/` via post-build target.
6. **Cooldown struct persistence** — `CharacterState` is a value type. Always `_states[id] = state` after modifying cooldowns, otherwise the change is discarded.
7. **Dash duration comes from `MovementStats.DashDurationTicks`** — not the const `Simulation.DashDurationTicks`. Character definition is authoritative.
8. **Proportional friction is asymptotic** — `VelocityDeadZone` (0.015) in `ApplyVelocityDeadZone()` snaps horizontal velocity to 0. Applied after ground friction and air drag.
9. **`MatchConfig` is static** — it persists across scene loads. Call `MatchConfig.Reset()` in `MainMenuController.OnEnable` so stale values from a previous match don't leak into the next one.

### Add a new character
→ Full guide: `docs/characters/adding-a-new-character.md`
→ Quick version: add `CharacterClass` enum value → write `BuildXxx()` in `CharacterDefinition.cs` → register in `BuildRegistry()` → add `AbilitySpec.Description` for each ability slot → create `CharacterAnimationConfig` ScriptableObject.

---

## Quick Commands

```bash
# Build Shared library (run after any src/Shared/ change)
dotnet build src/Shared/ --nologo

# Run simulation unit tests
dotnet test tests/Shared.Tests/ --nologo

# Run headless server
dotnet run --project src/Server/
```

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
| `docs/superpowers/specs/2026-07-09-menu-ui-flow-design.md` | Menu flow design: MainMenu → Lobby → CharSelect → StageSelect |
| `docs/plans/match-architecture.md` | MatchBase/ISimulationBridge seam design |
| `CLAUDE.md` | Coding rules (Shared/ purity, tick-based, no engine physics in Shared/) |
