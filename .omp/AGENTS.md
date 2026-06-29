# SlopArena Project Context

## About the Dev

- French solo dev, English only communication.
- Uses Unity 6 C# with .NET 8. Shared code (`src/Shared/`) targets netstandard2.1, compiled as a DLL imported by Unity via `client/Unity/Assets/Plugins/SlopArena.Shared/`.
- Uses Blender 5.1 (detected from `~/.config/blender/5.1/extensions/`).
- Server-authoritative architecture: the server simulation is always the source of truth, never use client-side hacks (position overrides, state checks) for gameplay mechanics.
- Preference: `dotnet build src/Shared/ --nologo` after every Shared change (auto-copies DLL to Unity Plugins).
- Squash commits, then push.
- NEVER install anything without asking.
- "Stop saying no to my choices" was a direct correction. Implement numeric choices without arguing. Suggest once only if correctness issue, then implement their value.
- Follows the project CLAUDE.md workflow: think before coding, state assumptions explicitly, explain before editing, present tradeoffs. For multi-step changes state a brief plan first.
- Never change files without explaining the plan first. The flow is: state the problem → describe the fix (what files, why) → wait for "vas y" / "go ahead" → implement. See Debugging Protocol below.

## Project Overview

SlopArena is a 3D platform fighter (Smash/DKO-style) with a server-authoritative 60Hz UDP model with client-side prediction + rollback reconciliation.

### Architecture

```
Unity Client (renderer + prediction)    ServerApp (.NET console, authority)
       │                                          │
       │  UDP localhost:9876                      │
       │                                          │
       ├─ InputState (22 bytes, 60Hz) ───────────►│
       │                                          ├─ ServerSimulation.Tick()
       │◄─────────────────────────────────────────├─ CharacterState (44 bytes per entity)
```

- `Shared/` is pure C# with zero Unity dependencies. No `UnityEngine.*` imports.
- All tick durations use `ushort` (max 65535 ticks = ~18 minutes).
- Packet serialization uses `System.Buffers.Binary.BinaryPrimitives` (little-endian).
- ClientInputPacket = 22 bytes, CharacterStatePacket = 44 bytes per entity.

## Key Conventions

### Project Structure
- `src/Shared/` — canonical shared code (netstandard2.1). Real .cs files, single source of truth.
- `client/Unity/Assets/Plugins/SlopArena.Shared/` — compiled DLL, auto-copied via post-build.
- `dotnet build src/Shared/` → rebuilds DLL and copies to Unity Plugins.
- `client/Unity/Assets/Scripts/Runtime/` — Unity MonoBehaviour scripts (Input, Renderer, Camera, UI).
- `tests/Shared.Tests/` — xUnit tests for simulation.

### Unity Conventions
- Use `MonoBehaviour.Update/FixedUpdate`, not Godot `_Process`/`_PhysicsProcess`.
- Animator Controller with trigger-driven 1-layer state machine.
- Input via Unity InputSystem (`Keyboard.current`, `Mouse.current`).
- Button text color = White.

### Movement
- LungeForce on AttackStage implements forward burst.
- No normal movement processing during Attacking state.
- Warp is server-side via Simulation.ProcessWarp.
- Camera is a world sibling (instantiated by TrainingMatch), absolute yaw — mouse only.
- Double jump for all classes, dash replaces old air-dodge.

### Combat
- Smash-style % system (no HP). DamagePercent 0-999.
- Knockback scales: `kbScale = 1 + (DamagePercent * 0.01)`.
- ServerAbility lifecycle (OnStart/Tick/OnEnd) for complex abilities; data-driven HitboxEvents for simple attacks.
- Hit detection via pure math (CombatMath.cs, SpellResolver.cs) — no Unity physics queries on server.

### Input
- InputController.Poll() + BuildInputState() → SlopArena.Shared.InputState.
- Sim handles input buffering via InputBufferWindow=6 ticks.
- Entity IDs: player=1, NPCs=100-104.

### Animation
- Unity Animator Controller with trigger-driven states.
- Mixamo FBX exports in cm, Blender uses m — 0.01 scale factor.
- Mixamo Control Rig constraints on mixamorig bones copy FROM helpers TO mixamorig (reverse direction).
- Blender 5.1 uses layered actions API.
- CharacterAnimationConfig ScriptableObject maps animation names to AnimationClips.


### Debugging Protocol
1. State the problem (1-2 sentences)
2. Describe the fix (2-3 sentences): what, which files, why
3. Wait for confirmation before coding — "vas y", "go ahead"
4. For architecture-level changes: write design doc in `docs/<topic>.md` first, present options with pros/cons
5. One file change at a time for complex edits

### Docs worth reading before system-level work
- `docs/architecture-overview.md` — directory map, data flow, pitfall list
- `docs/systems/netcode-architecture.md` — UDP protocol, rollback, packet layout
- `docs/systems/combat-systems.md` — universal combat mechanics
- `docs/systems/hitbox-system.md` — hit detection, hurtboxes, collision math
- `docs/systems/animation-system.md` — FSM lifecycle, AnimationTree
- `docs/contributing/conventions.md` — art direction, naming, pipeline
- `docs/plans/` — active refactor plans (ability refactor, AnimationTree builder, online PvP roadmap)
