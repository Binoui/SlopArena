---
name: sloparena-entity-system
description: SlopArena entity architecture — NPCs, hitboxes, entity IDs, processing order, bone attachments, and per-character components
---

# SlopArena Entity System

Architectural patterns for SlopArena's entity/hitbox/NPC system. Covers identity management, hit detection pipeline, NPC input flow, processing order, and character-specific component extraction.

## ⭐ Workflow Rule — Design Doc First for Architecture Changes

Before making ANY architecture-level change (new system, refactor of existing system, protocol changes), write a brief design doc FIRST:
1. Describe the problem and the current approach
2. Present 2-3 options with pros/cons
3. Propose the chosen design with a clear data flow diagram
4. Ask Binoui for feedback BEFORE implementing

This prevents the "back-and-forth on decisions" problem. The doc goes in `docs/<topic>.md`.

Triggers: user says "je veux faire mais j'ai l'impression qu'on fasse des aller retours" or asks for a comparison of approaches. Don't code the first idea — document it, discuss it, then implement.

## ⭐ Workflow Rule — Explain Before Editing (MANDATORY)

Applies to ALL SlopArena code. **This is the #1 rule. Violating it erodes trust faster than any bug.**

### Before ANY edit:
1. **State the problem** (1-2 sentences): "Le bug c'est X parce que Y."
2. **Describe the fix** (2-3 sentences minimum): what you'll change, in which files, and why this approach
3. **Wait for confirmation** — do not start coding until the user says "vas y" or equivalent

### For multi-file changes (>2 files):
Same as above, but also list the files you intend to modify and what each change does.

### For architecture-level changes:
Write a design doc in `docs/<topic>.md` first. Present options with pros/cons. Get feedback before implementing.

### Trigger phrases — STOP, explain, wait:
- "je préfère que tu m'expliques un peu plus ce que tu changes"
- "encore une fois, explique moi chaque changement que tu fais"
- "tu viens encore de changer 15 fichiers sans m'expliquer"
- "c'est quoi ce délire ?" or "t'as fais nimp"
- Any variant of "parle-moi de ton plan AVANT"

**Failure mode from this session (June 12):** I made 5+ file changes in rapid succession without explaining the plan. User: "encore une fois tu as implémenté sans me dire ce que t'as fais et t'as fais nimp. c'est quoi ce délire activeSlot=2 ? le rmb fait le meme coup que le lmb maintenant et le qerf fait plus rien". The issue wasn't the code — it was that the user had no chance to review the design before I broke things.

## Architecture

Unity client (renderer only) + `src/Server` .NET console over UDP localhost. There is **no client-side prediction** in Phase 1:

- Training mode: `LocalSimulationBridge` ticks the shared `ServerSimulation` in-process.
- PvP: `NetworkSimulationBridge` sends input over UDP and returns server states.
- `PlayerRenderer.ApplyServerState(state)` renders the server state directly (one-tick display latency is intentional).

Reference docs: `docs/systems/hitbox-system.md`, `docs/systems/npc-system.md`, `docs/systems/netcode-architecture.md`, `docs/architecture-overview.md`.

## Entity Identity

Each entity (player or NPC) has a **unique, consistent entity ID** from spawn to combat resolution. It is the `OwnerId` on every hitbox this entity spawns.

| Entity | ID |
|--------|----|
| Player | `1` |
| NPCs | `100-104` (100 + index) |

The classic bug: hardcoding `1` for every NPC makes each NPC's hitbox carry `OwnerId=1` while they're registered in the simulation as `100-104` — the self-filter never triggers. Match the registered ID exactly.

## NPC Input Flow

NPCs do not read keyboard/mouse. Two mechanisms exist:

- **`InputController.InjectAI(InputState)`** (`client/Unity/Assets/Scripts/Runtime/Input/InputController.cs:61`) — public API that switches an `InputController` into AI mode (`_aiControlled = true`); `Poll()` then consumes the injected input instead of the InputSystem. `ClearAI()` reverts. Declared but not yet called by any scene code.
- **Live training path** — `TrainingMatch.OnMatchFixedUpdate()` builds NPC input itself via `BuildNpcInput(npcState, playerState, tick)` (driven by the `NpcAiMode` enum: `Attack` / `Idle`) and passes player + NPC inputs together into `_bridge.Tick(inputs)`.

NPC visuals render from server state exactly like players — `PlayerRenderer.ApplyServerState(state)` — always authoritative, no prediction.

## Hit Detection Pipeline

Sim-authoritative, pure math — the server never runs physics queries.

1. `HitboxEvent.TriggerTick` — data-driven definition (`AttackStage.HitboxEvents[]`): when `state.AttackElapsedTicks == evt.TriggerTick`, the simulation spawns the hitbox.
2. `ServerSimulation.Tick()` spawns hitboxes into `SpellResolver` — position = `entityPos + rotate(OffX, OffY, OffZ)` by facing yaw (or resolved from a bone name via baked data when `BoneName` is set).
3. `SpellResolver.Tick()` — sphere/capsule collision vs hurtboxes, damage, knockback, hitstun. No engine physics queries.
4. Hurtboxes come from **baked skeleton data** (`SlopArenaBaker`, `client/Unity/Assets/Scripts/Editor/SlopArenaBaker.cs` bakes `.bin` files), not live bone reads. `CharacterDefinition.HurtboxBoneDefs[]` (bone spheres) replaces `HurtboxCapsules[]` (fixed local-space capsules) when loaded; `BakedDataPath` points at the `.bin`.

Client role in combat: render only. `Runtime/` contains `ProjectileVFXManager`, `AimHandler`, `AimIndicator`, `TargetIndicator`, `CombatFeedback` — no ability or FSM classes. Projectile visuals are prefab-driven via `ProjectileVFXManager`; hit detection stays on the server.

## Per-Character Components

- **Server definition** — `CharacterDefinition` lives in `src/Shared/Characters/<Name>Data.cs` (`MankiData.cs`, `KistuData.cs`, `FightGuyData.cs`, `NilusData.cs`): movement stats, `AbilitySpec[]`, `HurtboxBoneDefs`/`HurtboxCapsules`, `BakedDataPath`, `AnimationNames[]`.
- **Client visuals** — `PlayerRenderer` (`Runtime/Entities/PlayerRenderer.cs`) plays clips via Animancer from the sim state; `WeaponAttach` + `WeaponAttachConfig` (`Runtime/Entities/`) mount weapons to skeleton bones.
- Registration: `ServerSimulation.RegisterEntity(id, def, state)` on the server; the client bridge mirrors it (no-op for `NetworkSimulationBridge` — the server owns registration).

## Processing Order

- Unity callbacks: `Update` / `FixedUpdate` — there is no separate physics callback to hook.
- The match loop lives in `TrainingMatch.OnMatchFixedUpdate()` and `PvPMatch.FixedUpdate()` (`client/Unity/Assets/Scripts/Runtime/World/`): poll input → build `InputState` → `_bridge.Tick(inputs)` → `ApplyServerState`.
- No parent-child process-priority tricks — that is an engine-specific concept and does not apply here.
