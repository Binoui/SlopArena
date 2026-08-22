# Architecture Overview — Codebase Map

> **For agents and new contributors.** Read this first — it tells you where everything lives.
> For netcode theory, see `docs/systems/netcode-architecture.md`. For art/naming, see `docs/contributing/conventions.md`.

---

```
SlopArena/
├── client/Unity/Assets/Scripts/  ← Shared is NOT under Assets/Scripts; see src/Shared + Plugins/SlopArena.Shared DLL
│
├── src/Shared/          ← canonical Shared code (netstandard2.1), built to client/Unity/Assets/Plugins/SlopArena.Shared/
│   ├── Abilities/           ← ServerAbility implementations
│   │   ├── ServerAbility.cs     ← Base class: OnStart/Tick/OnEnd lifecycle
│   │   ├── AbilityFactory.cs    ← Maps AbilityTypeId to concrete implementations
│   │   ├── LmbCombo.cs          ← Manki LMB: 3-hit combo with lunge (StageChainAbility)
│   │   ├── MankiRoundBomb.cs    ← Manki Q: hold-to-aim parabolic bomb
│   │   ├── MankiAerosolFlame.cs ← Manki RMB: hold-to-charge flamethrower
│   │   ├── MankiBazooka.cs      ← Manki R: rise-aim-fire bazooka
│   │   ├── MankiOverclock.cs    ← Manki F: self-buff 8s
│   │   └── Kistu*/Nilus*/FightGuy* ← per-character ability implementations
│   ├── Characters/          ← MankiData, FightGuyData, KistuData, NilusData (per-character definitions)
│   ├── Simulation.cs        ← SimulateTick(): one tick of movement + combat
│   ├── SpellResolver.cs     ← hitbox collision math
│   ├── CharacterState.cs    ← per-tick entity state
│   ├── CharacterStatePacket.cs ← UDP packet (63 bytes, +13B envelope)
│   ├── ClientInputPacket.cs ← legacy — the wire uses InputState; kept for compat
│   ├── InputState.cs        ← normalized input (MoveX/Y, flags, ActiveSlot), 19 bytes
│   ├── AttackData.cs        ← HitboxEvent, AttackStage, AbilityData structs
│   ├── CombatMath.cs        ← knockback, facing, damage scaling
│   ├── BakedAnimationData.cs← offline-baked bone positions per frame
│   ├── ArenaDefinition.cs   ← arena data (platforms, spawns, kill height)
│   └── ... (lobby/codec DTOs: LobbyPayloadCodec, MasterServerClient, HostedServerConfig, ServerLogParser)
│
├── client/Unity/         ← Unity game client
│   └── Assets/Scripts/
│       ├── Runtime/
│       │   ├── Entities/       ← PlayerRenderer, StatusBillboard, WeaponAttach
│       │   ├── World/          ← GameManager, MatchBase, TrainingMatch, PvPMatch (match orchestration)
│       │   ├── Simulation/     ← ISimulationBridge, LocalSimulationBridge, NetworkSimulationBridge
│       │   ├── Network/        ← NetworkClient (UDP, Connect/SendInput/ReceiveStates)
│       │   ├── UI/             ← MatchConfig, MainMenuController, LobbyController, CharSelectController, StageSelectController, HUDManager
│       │   ├── Input/          ← InputController (Unity Input → InputState)
│       │   ├── Camera/         ← CameraMount, AimCameraMount (orbit + aim camera)
│       │   ├── Combat/         ← CombatFeedback, AimHandler, AimIndicator
│       │   └── Animation/      ← CharacterAnimationConfig (ScriptableObject)
│       ├── Editor/
│       │   ├── SlopArenaBaker.cs        ← skeleton bake (bone positions per anim frame → .bin)
│       │   ├── SlopArenaArenaBaker.cs   ← arena bake
│       │   ├── SlopArenaSceneSetup.cs   ← scene setup
│       │   ├── PopulateAbilityClips.cs  ← populate ability clip slots
│       │   ├── SetupArenaLighting.cs    ← arena lighting
│       │   └── SetupArenaSkybox.cs      ← arena skybox
│
├── src/
│   ├── Shared/            ← canonical Shared code (netstandard2.1)
│   ├── Server/            ← Headless .NET server (MatchInstance, UDP loop)
│
├── tests/
│   └── Shared.Tests/      ← xUnit tests (ServerSimulation, SpellResolver, etc.)
│
├── docs/                 ← All documentation
├── data/                 ← Baked binary data (.arena, _skeleton.bin). Versioned source;
│                           staged into client/Unity/Assets/StreamingAssets/ by
│                           scripts/build-release.sh for player builds. Clients resolve
│                           via BakedContentPaths (StreamingAssets first, repo data/
│                           fallback for the Editor) — issue #77
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
│       └── UpdateAnimationState() → _animancer.Play(clip)        │
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
3. **`Shared/` is built as a netstandard2.1 DLL** — run `dotnet build src/Shared/` after editing Shared code. Auto-copies to `client/Unity/Assets/Plugins/SlopArena.Shared/` via post-build target.
4. **Cooldown struct persistence** — `CharacterState` is a value type. Always `_states[id] = state` after modifying cooldowns, otherwise the change is discarded.
5. **Dash duration comes from `MovementStats.DashDurationTicks`** — not the const `Simulation.DashDurationTicks`. Character definition is authoritative.
6. **Proportional friction is asymptotic** — `VelocityDeadZone` (0.015) in `ApplyVelocityDeadZone()` snaps horizontal velocity to 0. Applied after ground friction and air drag.
7. **`MatchConfig` is static** — it persists across scene loads. Call `MatchConfig.Reset()` in `MainMenuController.OnEnable` so stale values from a previous match don't leak into the next one.

### Add a new character
→ Full guide: `docs/characters/adding-a-new-character.md`
→ Quick version: add `CharacterClass` enum value → create `src/Shared/Characters/<Name>Data.cs` → register in `BuildRegistry()` → add `AbilitySpec.Description` for each ability slot → create `CharacterAnimationConfig` ScriptableObject.

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
| `docs/systems/animation-system.md` | Animancer clip playback, server-timed transitions, extrapolation |
| `docs/systems/netcode-architecture.md` | Server-authoritative model, prediction, reconciliation |
| `docs/systems/ability-architecture.md` | ServerAbility pattern, lifecycle, creating new abilities |
| `docs/systems/combat-systems.md` | Universal combat mechanics |
| `docs/contributing/conventions.md` | Art direction, animation naming, bone naming |
| `docs/characters/adding-a-new-character.md` | Full pipeline for new characters |
| `docs/superpowers/specs/2026-07-09-menu-ui-flow-design.md` | Menu flow design: MainMenu → Lobby → CharSelect → StageSelect |
| `docs/plans/match-architecture.md` | MatchBase/ISimulationBridge seam design |
| `CLAUDE.md` | Coding rules (Shared/ purity, tick-based, no engine physics in Shared/) |
