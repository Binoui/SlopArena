# SlopArena Project Context

## About the Dev

- French solo dev, English only communication.
- Uses Godot 4.6.3 C# with .NET 8.
- Uses Blender 5.1 (detected from `~/.config/blender/5.1/extensions/`).
- Server-authoritative architecture: the server simulation is always the source of truth, never use client-side hacks (position overrides, state checks) for gameplay mechanics.
- Preference: `dotnet build --nologo` after every change.
- Squash commits, then push.
- NEVER install anything without asking.
- "Stop saying no to my choices" was a direct correction. Implement numeric choices without arguing. Suggest once only if correctness issue, then implement their value.
- Follows the project CLAUDE.md workflow: think before coding, state assumptions explicitly, explain before editing, present tradeoffs. For multi-step changes state a brief plan first.
- Never change files without explaining the plan first. The flow is: state the problem → describe the fix (what files, why) → wait for "vas y" / "go ahead" → implement. See Debugging Protocol below.

## Project Overview

SlopArena is a 3D platform fighter (Smash/DKO-style) with a server-authoritative 60Hz UDP model with client-side prediction + rollback reconciliation.

### Architecture

```
Godot Client (renderer + prediction)    ServerApp (.NET console, authority)
       │                                          │
       │  UDP localhost:9876                      │
       │                                          │
       ├─ Send(entityId + tick + InputState) ────►│
       │                                          ├─ ServerSimulation.Tick()
       │◄─────────────────────────────────────────├─ Send(entityId + tick + CharacterState)
```

- `Shared/` is pure C# with zero Godot dependencies. No `Godot.*` imports.
- All math uses `System.MathF`, not Godot's `Mathf`.
- All tick durations use `ushort` (max 65535 ticks = ~18 minutes).
- Packet serialization uses `System.Buffers.Binary.BinaryPrimitives` (little-endian).
- ClientInputPacket = 14 bytes, CharacterStatePacket = 39 bytes (includes MatchState at offset 38).

## Key Conventions

### Godot UI
- Use `TransitionTo(clear+push)` for forward navigation, never bare `PushScreen`.
- Button font_color = White.
- Export: `embed_pck=true`, `embed_build_outputs=true`.
- Linux → Windows cross-compile: `embed_build_outputs` is BROKEN (needs `data_*` folder).
- CI: `firebelley/godot-export@v8.0.0`.
- `.bin` files = `EmbeddedResource` in `.csproj`.
- Exclude `tools/` from exports.
- UI overwrites `export_presets.cfg` — make ALL changes through the Godot Export UI.

### FSM
- FallState animName = "jump" (no fall clip).
- JumpState: 3-tick ground → run/idle.
- LandingState removed (ground snap removed — server no longer sets PY = groundY on landing).
- Hit reaction anims loop (Linear).
- Godot 4.6.3 rejects self-transitions — fix: `start_offset=0`.
- All FSM grounded checks use `Movement.IsGrounded` (reads server `CharacterState.IsGrounded`), NOT `Player.IsOnFloor()`.
- Use `AnimPlayback.Travel()`, not bare `Play()`.

### Movement
- LungeForce on AttackStage implements forward burst in AbilityExecutor.TryStart.
- ProcessNormalMovement no longer runs during Attacking (no friction killing lunge).
- Warp is server-side via ActionState.Warping + Simulation.ProcessWarp.
- Client sets warp target on MovementComponent.State, synced to local sim.
- Camera is a world sibling (instantiated by Main.cs), not a child of PlayerController. Camera yaw is absolute — mouse only, never follows player facing.
- Movement is camera-relative 8-direction (snapped to 45° increments).
- Ground arrow indicator at feet shows input direction.
- Double jump for all classes, dash replaces old air-dodge.

### Combat
- Smash-style % system (no HP). DamagePercent 0-999.
- Knockback scales: `kbScale = 1 + (DamagePercent * 0.01)`.
- Dual-path ability system: data-driven AbilityExecutor for simple attacks, ServerAbility classes for complex abilities (BackflipRoll, MeleeCombo).
- Hit detection via pure math (CombatMath.cs), never Godot physics queries on server.
- Platform-aware ground collision via PlatformDef[] + GetGroundSurfaceY.

### Input
- `InputController._pendingSlotPress` (set by _UnhandledInput) → `BuildInputState()` → `input.ActiveSlot`.
- Sim handles input buffering via `InputBufferWindow=6` ticks.
- Entity IDs: player=1, NPCs=100-104.

### Animation
- GLB embedded animations + .tscn wrapper approach.
- Mixamo FBX exports in cm, Blender uses m — 0.01 scale factor.
- Mixamo Control Rig constraints on mixamorig bones copy FROM helpers TO mixamorig (reverse direction).
- Blender 5.1 uses layered actions API (`action.layers[0].strips[0].channelbags[0].fcurves`).
- `AnimationTreeBuilder` generates FSM from `CharacterDefinition` data at runtime, no .tscn sub-resource editing.
- Animation state crossfade all at 0.15s.
- All clip overrides via `AnimationClipConfig` struct.

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
