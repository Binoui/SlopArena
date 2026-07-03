> ⚠️ **DEPRECATED** — Contents are stale. The doc was written after Phase 1 and never updated.
> See `docs/plans/2026-07-03-pvp-roadmap.md` for the current, audited roadmap.

# Unity Client Architecture Plan

> **Goal:** Replace the hacky offline mode with a clean architecture where training and PvP share the same simulation pipeline. Training = local `ServerSimulation` without network. PvP = network-backed `ServerSimulation` with prediction/rollback.
> **Principle:** Iterative phases, each producing a testable deliverable. Every phase ends with a playable build.
> **Reference:** `Scripts/` is the Godot reference implementation — the spec. Each Godot file is ported to Unity, then deleted. Never delete a `Scripts/` file until its Unity equivalent is written, tested, and working.

---

## Shared Source: Symlink, Not DLL

`client/Unity/Assets/Scripts/Shared/` is a symlink to `../../../../src/Shared/`. Unity compiles the `.cs` files directly with its own Roslyn — same as Godot's `<ProjectReference>` did. No DLL boundary, no version mismatch, instant iteration.

`ServerSkeleton.cs` was moved from `Shared/` to `Server/` (it's the GLB parser, only needed server-side). This removed the `System.Text.Json 8.0.5` dependency that Unity's CoreCLR couldn't load — the root cause of the original DLL approach failing.

---

## Pre-Phase: Godot Infrastructure Cleanup

Delete these immediately — they're Godot editor scaffolding, not game logic. No code references them.

```
DELETE: .godot/                     (editor cache)
DELETE: project.godot               (project file)
DELETE: export_presets.cfg          (export config)
DELETE: main.tscn                   (main scene)
DELETE: icon.svg, icon.svg.import   (icon)
DELETE: SlopArena.csproj            (godot client .csproj)
DELETE: SlopArena.csproj.old        (old version)
DELETE: SlopArena.sln               (godot solution)
DELETE: global.json                 (.NET SDK pin)
DELETE: .editorconfig               (godot C# conventions)
DELETE: heightmap.bin               (godot-generated)
DELETE: Makefile                    (godot lint target)
DELETE: .githooks/                  (godot pre/post-commit)
DELETE: addons/                     (godot_ai/)
DELETE: scenes/                     (CameraMount.tscn)

DELETE: .github/workflows/build.yml     (godot export CI)
DELETE: .github/workflows/release.yml   (godot release CI)

DELETE: assets/**/*.tscn                (godot scene wrappers, 5 files)
DELETE: assets/**/*.import              (godot import metadata, ~120 files)
DELETE: assets/ui/keys/*.png            (keep only 6 used)
DELETE: assets/textures/kenney_prototype/*.import (orphaned, ~90 files)
DELETE: assets/characters/bunny/rabbit.tscn   (abandoned 3-line scene)

DELETE: tools/headless_bake.gd + .uid    (replaced by Unity SlopArenaBaker.cs)
DELETE: tools/bake_arenas.tscn            (replaced by Unity SlopArenaArenaBaker.cs)
DELETE: tools/BakeArenas.cs + .csproj    (replaced by Unity editor tool)
DELETE: tools/bake_all.sh                (godot bake pipeline)
DELETE: tools/obj/                       (bake tool build artifacts)
DELETE: tools/bin/                       (bake tool build artifacts)

DELETE: all *.uid files                  (godot UID cache, unity uses .meta)
DELETE: all *.uid.meta files             (unity metadata for deleted .uid files)
```

**What stays:** `assets/characters/**/*.glb`, `assets/textures/` (non-kenney), `assets/ui/font/`, `assets/ui/buttons/`, `tools/*.py`, `data/*.arena`, `data/*.bin`, `ci/`, `.github/workflows/nuget-publish.yml`, `.github/workflows/discord-push.yml`.

---

## Target Architecture

```
ISimulationBridge (interface)
├── LocalSimulationBridge — wraps ServerSimulation directly
└── NetworkSimulationBridge — UDP → server → state + reconciliation

MatchBase (abstract MonoBehaviour)
├── TrainingMatch — LocalSimulationBridge, NPCs
└── PvPMatch — NetworkSimulationBridge, remote opponent
```

Tick flow (FixedUpdate, 60Hz):
1. InputController.Poll() → BuildInputState() → InputState
2. (PvP only) Send input → NetworkClient → UDP
3. Tick(sim, inputs) — local OR receive server state
4. (PvP only) Compare predicted vs server → rollback
5. Renderer.ApplyState(entityId, state)
6. VFXManager.Process(state, events)
7. HUD.Update(state)

Update (variable rate):
8. CameraMount (mouse look)
9. UI polling (menus, overlays)

---

## Phase 1 — Foundation: Match Loop + Local Sim ✅ (2026-06-27)

**Status: COMPLETE** — tested in Editor, Manki moves/jumps/camera works. 63 xUnit tests
validate the simulation ground truth.

| Deliverable | Status |
|---|---|
| Manki moves with WASD (camera-relative 8-dir) | ✅ |
| Jump with JumpSquat (6t squat → VY applied) | ✅ |
| Double jump | ✅ |
| Camera orbits with mouse (Cinemachine + HardLookAt) | ✅ |
| Dummy NPC spawns and stands idle | ✅ |
| No terminal/server process needed | ✅ |
| Frame-by-frame animation driving | ✅ |
| 63 xUnit tests (physics, abilities, combat, edge cases) | ✅ |
| `_pendingJump` sticky input flag (replaces volatile edge detector) | ✅ |
| `ActivateAbility` AttackSlot bugfix | ✅ |
| Input buffering: FSM gate preserves pending jump through locked states | ✅ |

**Notes for next phase:** 21 new tests validate physics (jump/dash/land/hitstun),
ability lifecycles (LMB/RMB/Q/E activation + data-driven expiry), and multi-entity
stability. ServerAbility classes (MankiLmbCombo, MankiAerosolFlame) are partially
implemented — tests document basic activation. `Simulation.SimulateTick` ~260 lines
may need refactoring before adding more combat logic.

---

## Phase 2 — Combat Feedback: Hit Reactions + Knockback

**Deliverable:** Hit reactions play on the dummy when hit. Knockback visually moves the target. Hitstun state transitions work.

**Godot reference:**

| Unity file | Reads from Godot |
|---|---|
| `CombatFeedback.cs` | `Scripts/Combat/CombatComponent.cs` — hitbox spawning, damage routing, TakeDamage() |
| | `Scripts/Spells/StatusSpells.cs` — VFX triggers on status/hit |
| `PlayerRenderer.cs` (knockback) | `Scripts/Server/LocalServerBridge.cs:87-98` — ApplyStateToBody() sets pos + vel |

**Files to create:**
- `Runtime/Combat/CombatFeedback.cs`

**Files to modify:**
- `PlayerRenderer.cs` — smooth knockback interpolation (currently snaps to position instantly)
- `TrainingMatch.cs` — after `_localSim.Tick()`, read hit results → route to `CombatFeedback`

**Godot files deleted after this phase:**
- `Scripts/Combat/CombatComponent.cs`
- `Scripts/Spells/StatusSpells.cs`

**Test:** Press LMB near dummy → dummy plays hit reaction + slides backward. Dummy returns to idle after hitstun expires.


**Prerequisite:** All 63 xUnit tests pass before Phase 2 work begins. The tests validate
that hit detection (SpellResolver), damage/knockback (CombatMath), and state transitions
(PhysicsTests) are correct — without needing Unity visuals.
>
**Test-driven approach:** Write a test for the new combat behavior (e.g., "LMB hitbox
spawns at tick 6, NPC takes damage") before wiring the Unity renderer. Makes the
simulation truth debuggable in <3s without opening the editor.
---

## Phase 3 — Dummy AI: Standing Reset + Basic Movement

**Deliverable:** Dummy respawns after void death, faces player, doesn't just T-pose. Optional: walks toward player slowly.

**Godot reference:**

| Unity file | Reads from Godot |
|---|---|
| `InputController.cs` (AI path) | `Scripts/Entities/PlayerController.cs` — NPC branch of BuildInputState() |
| `TrainingMatch.cs` (respawn) | `Scripts/World/TrainingMatch.cs:85-100` — SpawnNPCs() |

**Files to modify:**
- `TrainingMatch.cs` — void death → reset NPC position + state
- `InputController.cs` — NPC AI input (face player, walk forward) via `InjectAI()`
- `PlayerRenderer.cs` — handle `Death` + respawn state

**Test:** Knock dummy off platform → respawns at spawn point. Dummy walks toward player slowly.

---

## Phase 4 — HUD: Damage % + Action Bar

**Deliverable:** Smash-style damage % display. Ability slot cooldown indicators. Minimal but functional.

**Godot reference:**

| Unity file | Reads from Godot |
|---|---|
| `DamageHUD.cs` | `Scripts/UI/UnitFrames.cs` — damage % display |
| `ActionBarHUD.cs` | `Scripts/UI/ActionBarHUD.cs` — ability slot cooldown icons |

**Files to create:**
- `Runtime/UI/DamageHUD.cs`
- `Runtime/UI/ActionBarHUD.cs`
- Canvas + UI prefabs

**Godot files deleted after this phase:**
- `Scripts/UI/UnitFrames.cs`
- `Scripts/UI/ActionBarHUD.cs`

**Test:** Damage % increases when hitting dummy, cooldowns tick down after using abilities.

---

## Phase 5 — Combat VFX: Hit Sparks + Spell Effects

**Deliverable:** Visual feedback for attacks hitting + Manki's spell effects (flamethrower, bomb, aerosol).

**Godot reference:**

| Unity file | Reads from Godot |
|---|---|
| `SpellVFXManager.cs` | `Scripts/VFX/SpellVFXManager.cs` — routes ability → VFX |
| `FlamethrowerVFX.cs` | `Scripts/VFX/FlamethrowerVFX.cs` — Manki RMB VFX |
| `BombVFX.cs` | `Scripts/Abilities/RoundBomb.cs` — bomb projectile + explosion |

**Files to create:**
- `Runtime/VFX/SpellVFXManager.cs`
- `Runtime/VFX/FlamethrowerVFX.cs`
- `Runtime/VFX/BombVFX.cs`
- VFX prefabs + particle systems

**Godot files deleted after this phase:**
- `Scripts/VFX/SpellVFXManager.cs`
- `Scripts/VFX/FlamethrowerVFX.cs`
- `Scripts/Abilities/RoundBomb.cs`

**Test:** Hit dummy → sparks at impact. Q → bomb arc + explosion. RMB → flame cone.

---

## Phase 6 — Network Bridge: Prediction + Rollback

**Deliverable:** Same gameplay as Training but over network. Local prediction + server reconciliation.

**Godot reference:**

| Unity file | Reads from Godot |
|---|---|
| `NetworkSimulationBridge.cs` | `Scripts/World/MatchManager.cs` — ring buffers, prediction, rollback loop |
| `PvPMatch.cs` | `Scripts/World/PvPMatch.cs` — network match lifecycle |

**Files to create:**
- `Runtime/Simulation/NetworkSimulationBridge.cs`
- `Runtime/World/PvPMatch.cs`

**Implementation:**
- 10-frame ring buffer for states + inputs (same as MatchManager._stateBuffer / _inputBuffer)
- Tick: send input → local predict → store → receive server → compare → re-sim if mismatch
- Uses existing `NetworkClient` for UDP

**Godot files deleted after this phase:**
- `Scripts/World/MatchManager.cs`
- `Scripts/World/PvPMatch.cs`

**Test:** Two Unity editor instances, one hosts (MatchInstance server), one joins. Both see each other move + fight. No rubberbanding.

---

## Phase 7 — UI Flow: Menu → Character Select → Match

**Deliverable:** Full menu flow: Main Menu → Character Select → (Training or PvP) → Match.

**Godot reference:**

| Unity file | Reads from Godot |
|---|---|
| `MainMenuUI.cs` | `Scripts/UI/main_menu.tscn` + `Scripts/World/Main.cs` — screen stack |
| `CharacterSelectUI.cs` | `Scripts/UI/character_select.tscn` + `Scripts/UI/CharacterSelectUI.cs` |
| `MatchOrchestrator.cs` | `Scripts/World/Main.cs` — TransitionTo() screen flow |

**Files to create:**
- Scenes: `MainMenu.unity`, `CharacterSelect.unity`
- `Runtime/UI/MainMenuUI.cs`
- `Runtime/UI/CharacterSelectUI.cs`
- `Runtime/World/MatchOrchestrator.cs`

**Godot files deleted after this phase:**
- `Scripts/World/Main.cs`
- `Scripts/UI/CharacterSelectUI.cs`
- `Scripts/UI/MainMenuUI.cs`

**Test:** Click Training → pick Manki → drop into arena. Click Online → join match.

---

## Phase 8 — Bunny + Polish

**Deliverable:** Second character fully playable. Remaining polish.

**Godot reference:**

| Unity file | Reads from Godot |
|---|---|
| Bunny AnimatorController | `assets/characters/bunny/bunny.glb` — model + animations |
| | `Scripts/Characters/Bunny/BunnyAbilities.cs` — special effects |

**Files to create:**
- Bunny FBX import + material + `CharacterAnimationConfig`
- Bunny AnimatorController (via `SlopArenaAnimatorGenerator`)

**Godot files deleted after this phase:**
- `Scripts/Characters/Bunny/BunnyAbilities.cs`

**Test:** Select Bunny → all animations play correctly, abilities function identically to Manki.

---

## Phase 9 — Final Cleanup: Remove Godot Reference

**Deliverable:** All remaining `Scripts/` files deleted. Docs updated. CI updated.

By this point every Godot `.cs` file has a working Unity equivalent. Delete `Scripts/` entirely. Update `docs/architecture-overview.md` to remove Godot references. Add Unity CI (`game-ci/unity-builder`).

This is `docs/plans/post-migration-cleanup.md` executed — plus removing now-unnecessary Godot reference docs.

---

## Dependency Graph

```
Pre-Phase (Infrastructure Cleanup)
  └── Phase 1 (Sim Loop)
        ├── Phase 2 (Combat Feedback)
        │     ├── Phase 3 (Dummy AI)
        │     ├── Phase 4 (HUD)
        │     └── Phase 5 (VFX)
        ├── Phase 6 (Network)
        │     └── Phase 7 (UI Flow)
        ├── Phase 8 (Bunny)
        └── Phase 9 (Cleanup)
```

Phases 2–5 can run in parallel after Phase 1. Phase 6 is independent of 2–5. Phase 7 needs Phase 6. Phase 8 needs Phase 1+2. Phase 9 is last — it deletes `Scripts/`.

---

## Godot File Fate by Phase

| Phase | Godot files deleted |
|---|---|
| 1 | `Scripts/Server/LocalServerBridge.cs`, `Scripts/World/TrainingMatch.cs` |
| 2 | `Scripts/Combat/CombatComponent.cs`, `Scripts/Spells/StatusSpells.cs` |
| 3 | (none — NPC logic is new in TrainingMatch, no separate Godot file) |
| 4 | `Scripts/UI/UnitFrames.cs`, `Scripts/UI/ActionBarHUD.cs` |
| 5 | `Scripts/VFX/SpellVFXManager.cs`, `Scripts/VFX/FlamethrowerVFX.cs`, `Scripts/Abilities/RoundBomb.cs` |
| 6 | `Scripts/World/MatchManager.cs`, `Scripts/World/PvPMatch.cs` |
| 7 | `Scripts/World/Main.cs`, `Scripts/UI/CharacterSelectUI.cs`, `Scripts/UI/MainMenuUI.cs` |
| 8 | `Scripts/Characters/Bunny/BunnyAbilities.cs` |
| 9 | Everything remaining in `Scripts/` |

---

## Key Design Decisions

1. **`ISimulationBridge` interface** — training and PvP share identical tick loop. Only the bridge changes.

2. **No server process for offline** — direct `ServerSimulation.Tick()`. The `dotnet run --project ServerApp` hack dies.

3. **`InputController` is the single input source** — no raw polling elsewhere. NPCs inject AI input via `InjectAI()`.

4. **Position via Transform** — server sim handles all movement. Client applies `transform.position` directly (smooth lerp for knockback).

5. **Unity Animator over custom FSM** — Godot's FSM was workaround for AnimationTree limits. Unity's Animator handles it natively.

6. **Shared via symlink, not DLL** — `Assets/Scripts/Shared/` → `src/Shared/`. Unity's Roslyn compiles `.cs` directly. Instant iteration, zero version risk.

7. **Scripts/ stays until ported** — it's the reference implementation. Each file deleted individually after its Unity equivalent works. Never speculatively.

---

## File Map (Target State — after Phase 9)

```
SlopArena/
├── src/
│   ├── Shared/                         (Pure C# sim, symlinked into Unity)
│   ├── Server/                         (Headless match server)
│   └── ServerApp/                      (Prototype test server)
├── client/
│   └── Unity/
│       └── Assets/Scripts/
│           ├── Shared/                 (symlink → src/Shared/)
│           ├── Runtime/
│           │   ├── Simulation/         (ISimulationBridge, Local/Network bridges)
│           │   ├── World/              (MatchBase, TrainingMatch, PvPMatch, MatchOrchestrator)
│           │   ├── Entities/           (PlayerRenderer, EntityRegistry)
│           │   ├── Input/              (InputController)
│           │   ├── Camera/             (CameraMount)
│           │   ├── Combat/             (CombatFeedback)
│           │   ├── VFX/                (SpellVFXManager, FlamethrowerVFX, BombVFX)
│           │   ├── UI/                 (DamageHUD, ActionBarHUD, MainMenuUI, CharacterSelectUI)
│           │   ├── Network/            (NetworkClient)
│           │   └── Animation/          (CharacterAnimationConfig)
│           └── Editor/                 (ArenaBaker, AnimatorGenerator, SceneSetup, SkeletonBaker)
├── assets/                             (Source art — GLB, textures, font)
├── data/                               (Baked binaries — .arena, _skeleton.bin)
├── tools/                              (Python pipeline scripts)
├── ci/                                 (Server CI)
├── docs/                               (Documentation)
└── .github/workflows/                  (NuGet + notifications CI)
```

> ⚠️ **DEPRECATED** — See `docs/plans/2026-07-03-pvp-roadmap.md` for the current roadmap.
