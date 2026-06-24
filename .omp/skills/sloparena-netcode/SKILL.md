---
name: sloparena-netcode
description: SlopArena server-authoritative netcode — UDP localhost, tick system, client-side prediction, rollback, bone-accurate hurtboxes
triggers:
  - sloparena netcode
  - server-authoritative
  - rollback
  - udp client server
  - client-side prediction
  - match manager
  - apply server state
---

# SlopArena Netcode Architecture

## Architecture Overview

```
Godot Client (renderer + prediction)    ServerApp (.NET console, authority)
       │                                          │
       │  UDP localhost:9876                      │
       │                                          │
       ├─ Send(entityId + tick + InputState) ────►│
       │                                          ├─ ServerSimulation.Tick()
       │◄─────────────────────────────────────────├─ Send(entityId + tick + CharacterState)
       │                                          │
       └─ Compare predicted vs server             │
          └─ mismatch → rollback                   │
```

## Data Flow (one frame)

### Client _PhysicsProcess (60Hz):
1. BuildInputState()
2. Assign `_sendTick++`
3. Store input in `_inputBuffer[tick % RollbackFrames]`
4. `localSim.Tick(input)` → predict state
5. Store predicted in `_stateBuffer[tick % RollbackFrames]`
6. `Net.SendInput(input, tick)` → UDP
7. `Player.ApplyServerState(predicted)` → render

### Client _Process:
8. `Net.ReceiveStates()` → server states with ticks
9. Compare `_stateBuffer[serverTick]` vs server state
10. If mismatch: rollback (re-simulate from safe state)

### ServerApp Tick:
1. Receive all client inputs (buffer)
2. `sim.Tick(inputs)` → ServerSimulation
3. Broadcast `CharacterState[]` to all clients

## Packet Format

### Client → Server (26 bytes)
```
[0..7]  entityId (ulong)
[8..11] tick (uint) 
[12..25] InputState (14 bytes: MoveX, MoveY, flags byte, ActiveSlot byte, FacingYaw short, AimYaw short)
```

- InputState.Size = 14 (2 x float + 1 flags + 1 ActiveSlot + 2 FacingYaw + 2 AimYaw)
- FacingYaw = movement-facing (set by Atan2 in sim, sent for completeness). NOT used by sim.
- AimYaw = combat-facing (sent by client camera). NOT used by sim. Reserved.
### Server → Client (52 bytes per entity)
 
```
[0..7]  entityId (ulong)
[8..11] tick (uint) — echoed from client
[12..51] CharacterStatePacket (40 bytes: TickNumber, PX, PY, PZ, VX, VY, VZ, State, IsGrounded, StateTicks, AttackSlot, ComboStage, FacingYaw, MatchState, AnimIndex)
```
 
- `CharacterStatePacket.Size` = 40 bytes (TickNumber=4, 3 pos + 3 vel = 24, State=1, IsGrounded=1, StateTicks=2, AttackSlot=1, ComboStage=1, FacingYaw=4, MatchState=1, AnimIndex=1 = 40)
- **CRITICAL: AttackSlot, ComboStage, FacingYaw, and MatchState must be in the packet.** Without AttackSlot, server ghost stays in idle pose. Without FacingYaw, ghost faces +Z. Without MatchState, clients can't show countdown/game-over UI. Add in FromState(), ToState(), Serialize(), Deserialize() — all 4 methods.
- **MatchState field** (1 byte at packet offset 38) carries the server-authoritative match lifecycle: Waiting, Countdown, Playing, Ended. Set by the server before serialization, read by the client for UI updates. AnimIndex is at packet offset 39.

## Key Classes

- **ServerSimulation** (Shared/): Pure C# game loop. 60Hz tick. Same code on client and server.
  - `GetState(id)` returns a copy of the CharacterState for an entity.
  - `SetState(id, state)` replaces the state for an entity (used for warp data sync in training mode).
- **MatchManager** (Scripts/World/): Orchestrates local sim + network client + rollback.
- **NetworkClient** (Scripts/Network/): UDP send/receive with tick tracking.
- **PlayerController.ApplyServerState()**: Simple struct copy + MoveAndSlide (no hacks).
- **ServerApp** (ServerApp/): Standalone console .NET server process.

## Important: Input-Driven Simulation (Attack System)

The simulation (both local and server) handles ALL attacks via `InputState.ActiveSlot`:
```csharp
// ActiveSlot: 0 = none, 1 = LMB, 2 = RMB, 3 = Q, 4 = E, 5 = R, 6 = F
```

**Attack cycle (sim-authoritative):**
1. `_UnhandledInput` sets `_pendingSlotPress = X` (no direct FSM transition)
2. `BuildInputState()` → `input.ActiveSlot = X` (sim handles buffering via `InputBufferWindow=6`)
3. `SimulateTick`:
   - If `ActiveSlot > 0` && free (`AnimLockTicks==0`, `HitstunTicks==0`) → `StartAttackFromSlot()`
   - If `ActiveSlot > 0` && locked within 6f of unlock → buffer in `BufferedSlot`
4. After any lock expires:
   - `BufferedSlot > 0` → `StartAttackFromSlot()` (handles combo if same slot, fresh if different)
   - `BufferedSlot == 0` → idle
5. Each tick during attack: `AttackElapsedTicks++`
   - When `AttackElapsedTicks == HitboxEvent.TriggerTick` → `SpellResolver.Spawn(hitbox)`
     - Position: `entityPos + rotate(OffX, OffY, OffZ) by facing yaw`
6. When `AnimLockTicks == 0`:
   - If `BufferedSlot == AttackSlot` && next stage exists → immediate combo advance
   - Else → `State = Idle` (buffered different-slots consumed by step 4)

- **Combo chaining (general input buffer):**
- `InputBufferWindow = 6` (Simulation.cs) — inputs within 6 frames of unlock are buffered in `BufferedSlot`
- **Two buffer paths**:
  1. Same slot during current attack (`ActiveSlot == AttackSlot` while `State == Attacking`): **always buffered**, no frame limit, no cooldown check. Guard: `AttackElapsedTicks > 0` prevents the click that STARTED the attack from also being buffered.
  2. Different slot / hitstun / other locks: only within 6-frame window, cooldown checked
- When current stage ends (`AnimLockTicks=0`), `ProcessAttack` checks in order:
  ```csharp
  // Check 1: BufferedSlot (click arrived during earlier frames)
  if (s.BufferedSlot == s.AttackSlot && s.ComboStage < ability.Stages.Length - 1)
  {
      s.BufferedSlot = 0;
      advance combo
  }
  // Check 2: input.ActiveSlot (click arrived on same frame)
  else if (input.ActiveSlot == s.AttackSlot && s.ComboStage < ability.Stages.Length - 1)
  {
      input.ActiveSlot = 0;  // consumed — prevents re-buffer below!
      advance combo
  }
  // Check 3: no combo → idle
  ```
  `ProcessAttack` takes `ref InputState input` so the `input.ActiveSlot = 0` propagates back to `SimulateTick`, preventing the buffer section from re-buffering the same click (double-advance bug).
- Input for a DIFFERENT slot while locked → stored in `BufferedSlot`, consumed by `StartAttackFromSlot()` when free starts fresh attack with that slot
- Works for ALL ability slots (LMB through F), not just LMB
- No `ComboTimerTicks`, no `BufferedChain`, no chain window timing. Works for ALL ability slots (LMB through F).

## FacingYaw + AimYaw (Client → Sim Facing Sync)

The sim needs the character's facing direction to spawn hitboxes/hurtboxes correctly. There are TWO facing systems:

- **FacingYaw** (movement-facing): Set by `ProcessNormalMovement` via `Atan2(VX, VZ)`. Controls the visual body direction.
- **AimYaw** (combat-facing): Sent by the client every frame from the camera yaw. **CURRENTLY UNUSED by the sim.** Reserved for future mouse-aim combat. Do NOT wire it up without discussing with the user first (systemic architecture change). All hitbox/hurtbox rotation uses FacingYaw (from Atan2).

### InputState fields

```csharp
public short FacingYaw; // degrees × 100, movement-facing
public short AimYaw;    // degrees × 100, combat-facing (from camera)
```

Size: 2 × short = 4 bytes. Total InputState: 14 bytes.

### Client sends

```csharp
// BuildInputState:
float deg = Mathf.RadToDeg(GlobalRotation.Y);
input.FacingYaw = (short)Math.Clamp(deg * 100f, -32768, 32767);
float aimDeg = _camera != null ? Mathf.RadToDeg(_camera.GetCameraYaw()) : deg;
input.AimYaw = (short)Math.Clamp(aimDeg * 100f, -32768, 32767);
```

Note: `FacingYaw` is the authoritative facing used by the sim (set via Atan2). `AimYaw` is sent from the client camera but is currently UNUSED by the sim — it's reserved for future mouse-aim combat.

### Sim applies

```csharp
// SimulateTick (first thing):
float aimDeg = input.AimYaw * 0.01f;
s.AimYaw = aimDeg * (MathF.PI / 180f);
// s.FacingYaw is NOT overwritten here — ProcessNormalMovement handles it via Atan2
```

### Where each is used

| System | Uses | Source |
|--------|------|--------|
| Hitbox offset rotation | state.FacingYaw | Sim Atan2(VX, VZ) |
| Hurtbox capsule rotation | state.FacingYaw | Sim Atan2(VX, VZ) |
| Skeleton bone positioning | state.FacingYaw | Sim Atan2(VX, VZ) |
| Visual body facing (walk) | state.FacingYaw | Sim Atan2(VX, VZ) |
| FSM animation blending | state.FacingYaw | Sim Atan2(VX, VZ) |

AimYaw is currently UNUSED by the sim. See the combat-engine skill for details.

### Why separate?

In a 3D platform fighter, the character can move forward (FacingYaw = forward) while attacking backward (AimYaw = backward) — think back-airs or retreating attacks. Two independent facing angles allow this without conflicts.

Sent unconditionally every frame (no `if` conditions). Works identically on localhost and real network.

**Client FSM reaction:**
```csharp
if (simState == ActionState.Attacking && !_fsm.IsInState("attack"))
{
    byte slot = _movementComponent.State.AttackSlot;
    var ability = _charDef.GetSlotAbility(slot - 1, airborne);
    attackState.NextAnimName = ability.AnimationNames[comboStage];
    _fsm.TransitionTo("attack");
    // Trigger SpecialEffectKeys if any
}
```

**No more client-side hitbox spawning.** `ResolveAbilityStages`, `SetPendingResolve`, `TickStartup`, `_hasPendingResolve` — all removed. The sim spawns all hitboxes via `HitboxEvent`.

## Rollback Buffer

- `RollbackFrames = 30` ring buffer (~500ms at 60Hz)
- `_inputBuffer[tick % N]` and `_stateBuffer[tick % N]`
- On server state arrival in `_Process`:
  ```csharp
  int idx = (int)(serverTick % RollbackFrames);
  float dx = predicted.PX - serverState.PX;
  float dy = predicted.PY - serverState.PY;
  float dz = predicted.PZ - serverState.PZ;
  float distSq = dx * dx + dy * dy + dz * dz;
  if (distSq > 0.25f) // 0.5m threshold — 3D distance, not just Y
  {
      // 1. Snapshot NPC states before replacing sim
      var npcStates = new CharacterState[NpcCount];
      for (int i = 0; i < NpcCount; i++)
          npcStates[i] = _localSim.GetState((ulong)(100 + i));

      // 2. Reset sim to server-confirmed state
      _localSim = new ServerSimulation(_arenaDef);
      _localSim.RegisterEntity(_playerEntityId, _charDef, serverState, _playerBakedData);

      // 3. Re-register NPCs (prefer server-confirmed, fallback to snapshot)
      for (int i = 0; i < NpcCount; i++)
      {
          ulong npcId = (ulong)(100 + i);
          var npcState = _serverConfirmedStates.TryGetValue(npcId, out var s)
              ? s : npcStates[i];
          _localSim.RegisterEntity(npcId, _charDef, npcState, _playerBakedData);
      }

      // 4. Re-simulate from serverTick+1 to currentTick
      for (uint t = serverTick + 1; t <= _sendTick; t++)
          _localSim.Tick(new Dictionary<ulong, InputState> {
              { _playerEntityId, _inputBuffer[t % RollbackFrames] }
          });

      // 5. Apply corrected state
      Player.ApplyServerState(_localSim.GetState(_playerEntityId));
  }
  ```

**Key differences from old rollback (pre-June 2026):**
- **3D distance check** (`dx² + dy² + dz² > 0.25f`), not Y-only (`|dy| > 0.5f`). Horizontal desyncs now trigger rollback.
- **RollbackFrames = 30** (was 10). Handles real network latency without buffer overruns.
- **NPC preservation**: snapshot NPC states before recreating sim, re-register them in new sim. Prevents NPCs vanishing on rollback.
- **No skeleton argument**: `_playerBakedData` (BakedAnimationData) is used instead of `_playerSkel` (ServerSkeleton). Baked data is pre-computed bone positions — faster, no runtime skeleton parsing.

## Server Tick Echo

Le serveur lit le tick de chaque client depuis le buffer d'input AVANT de vider le buffer, et écrit ce tick dans les paquets de réponse. Le client utilise ce tick pour retrouver l'état prédit correspondant dans son ring buffer.

```
ServerApp Program.cs:
  var clientTicks = inputBuffer.ToDictionary(kvp.Key → kvp.Value.tick);
  sim.Tick(inputs); inputBuffer.Clear();
  foreach client: packet.tick = clientTicks[entityId]
```

## Rollback Threshold Tuning

- **3D distance check** (`dx² + dy² + dz² > 0.25f` = 0.5m threshold). All axes checked, not just Y.
- **Localhost**: 1-2 frame server delay expected. 0.5m threshold avoids false rollbacks.
- **Remote server**: Can tighten to 0.1-0.3m depending on ping jitter.
- Threshold in MatchManager._Process:
  ```csharp
  float dx = predicted.PX - serverState.PX;
  float dy = predicted.PY - serverState.PY;
  float dz = predicted.PZ - serverState.PZ;
  float distSq = dx * dx + dy * dy + dz * dz;
  if (distSq > 0.25f) // 0.5m threshold — 3D
  ```

## Animation Chaining (Combo Stages)

When the sim advances from Stage N to N+1, the FSM stays in "attack" state. The client must detect the `ComboStage` change and chain the animation:

```csharp
// PlayerController._PhysicsProcess:
private byte _lastComboStage;

if (simState == ActionState.Attacking && _fsm.IsInState("attack") &&
    simComboStage != _lastComboStage && simComboStage > 0)
{
    var ability = _charDef.GetSlotAbility(slot - 1, airborne);
    string animName = ability.AnimationNames[simComboStage % ability.AnimationNames.Length];
    if (ability.AimedCharge.HasValue)
        animName = ability.AimedCharge.Value.AttackAnimName;
    attackState.ChainTo(animName);
}
_lastComboStage = _movementComponent.State.ComboStage;
```

The `ChainTo()` method calls `AnimPlayback.Travel(animName)` — this Travels from the End state of the previous stage to the new stage's animation. No re-entry into the FSM "attack" state.

## Debug Logging Strategy

When debugging netcode/tick/attack issues, logs must be visible:
- **Client-side (Godot)**: Use `GD.Print()` — visible in Godot Editor output panel
- **Sim-side (Shared/)**: Use `System.Console.WriteLine()` — goes to stdout (NOT visible in Godot panel)
- **MatchManager**: After `_localSim.Tick()`, read state and GD.Print any field of interest (BufferedSlot, AttackSlot, ComboStage, AnimLockTicks)
- **Rollback**: Add `GD.Print($"[Rollback] Tick {t}: dy={dy:F3}m")` to see rollback frequency
- **Threshold**: A rollback every frame means the threshold is too tight — increase until rollbacks are infrequent

## Workstyle

The user operates in an EXPLAIN-FIRST workflow. Before editing multiple files:
1. State the problem clearly
2. Describe proposed approach + alternatives
3. Wait for confirmation ("vas y") before coding
4. Keep changes in small explain → implement cycles
Silent multi-file patching without explanation erodes trust faster than any bug. This applies to all netcode, combat, and simulation changes.

## Common Pitfalls

### 0. Explain before implementing (user preference)
The user MUST understand the plan before code changes. Before editing multiple files:
1. State the problem clearly
2. Describe proposed approach + alternatives
3. Wait for confirmation before coding
4. Keep changes in small explain → implement cycles
Breaking this pattern erodes trust faster than any bug. Silent multi-file patching without explanation is the #1 complaint.

### 0b. Y/Z axis swap in custom StateToPacket (FIXED June 2026)
`MatchInstance` had a custom `StateToPacket()` that manually constructed `CharacterStatePacket` with:
```csharp
PositionY = state.PZ, // PZ → world Y (up) — WRONG, swapped
PositionZ = state.PY, // PY → world Z (forward) — WRONG, swapped
```
This swapped the Y (up) and Z (forward) axes. `CharacterStatePacket.FromState()` already does the correct mapping (`PY→PositionY, PZ→PositionZ`). The custom method also dropped `IsGrounded`, `AttackSlot`, `ComboStage`, and `FacingYaw`.

**Fix:** Delete any custom `StateToPacket()` method. Always use `CharacterStatePacket.FromState(state, tick)`. The canonical method includes all fields and correct axis mapping.

### 0c. ServerSimulation integration into MatchInstance (FIXED June 2026)

The production server is `Server/MatchInstance` (multiplayer orchestration). It was calling `Simulation.SimulateTick()` directly, skipping hit detection, hurtbox tracking, and void death. The fix integrates `ServerSimulation`:

```csharp
// Before (MatchInstance.Tick):
Simulation.SimulateTick(ref _p1State, _p1Def, input, _arena);
Simulation.SimulateTick(ref _p2State, _p2Def, input, _arena);

// After:
_sim = new ServerSimulation(_arena);
_sim.RegisterEntity(P1EntityId, p1Def, initialState, bakedData);
_sim.RegisterEntity(P2EntityId, p2Def, initialState, bakedData);
// In Tick():
_sim.Tick(inputs);
var p1State = _sim.GetState(P1EntityId);
```

**Full integration includes:**
- Baked skeleton data loading (from `charDef.BakedDataPath` on filesystem)
- SpellResolver instance per sim (not static — thread-safe across matches)
- Both player states sent to both endpoints (not just P1→P1, P2→P2)
- Connection timeout (5s silence → match stopped)
- Entity IDs: `P1EntityId = 1`, `P2EntityId = 2`

`ServerApp/Program.cs` is now a prototype stub superseded by `Server/MatchInstance`.

### 1. Godot parent/child _PhysicsProcess order
MatchManager (parent) ticks BEFORE PlayerController (child). The sim handles inputs, so client code should REACT to sim state, not start actions.

### 2. No preservation hacks in ApplyServerState
If the sim doesn't process an action, FIX THE SIM, don't add hacks to preserve timers.

### 3. Model Y offset auto-calculation
```csharp
var footPos = sk.GetBoneGlobalPose(footBone).Origin;
float offset = -(footPos.Y + capsuleHalfHeight);
pm.Position = new Vector3(0, offset, 0);
```

### 4. IsGrounded in packets
Must serialize `IsGrounded` or the client FSM stays in "air" forever.

### 5. Documentation sync: always cross-reference serialization code

When updating `docs/netcode-architecture.md`, do NOT just rephrase — the real packet sizes, field names, and struct layouts are in the source code. Always:
1. Read the existing doc first
2. Read the actual struct definitions (InputState.cs, CharacterStatePacket.cs, CharacterState.cs)
3. Read the serialization code (InputState.Write, CharacterStatePacket.Serialize)
4. Read the packet send/receive code (NetworkClient.cs, ServerApp/Program.cs)
5. Cross-reference comment sizes vs actual `Size` constants
   - `InputState.Size` = 14 (MoveX(4) + MoveY(4) + flags(1) + ActiveSlot(1) + FacingYaw(2) + AimYaw(2)), send packet = 8+4+14 = 26B
   - `CharacterStatePacket.Size` = 40, receive packet = 8+4+40 = 52B per entity
6. Fix any stale comments found during cross-referencing — they mislead future readers

### 6. Attack system is part of the netcode now

ALL attacks (LMB through F) flow through the sim pipeline via `ActiveSlot`. The client never transitions FSM directly for attacks — it reacts to `ActionState.Attacking`. When debugging attack bugs, look in the sim first, not the client FSM.

### 9. AttackSlot/ComboStage/FacingYaw must be serialized for server ghost

`CharacterStatePacket.FromState()` and `ToState()` must include `AttackSlot`, `ComboStage` and `FacingYaw`. Without them:\n- **Missing AttackSlot**: server ghost receives `State = Attacking` but `AttackSlot = 0` → animation lookup fails (`state.AttackSlot > 0` is false) → falls back to idle pose.\n- **Missing FacingYaw**: server ghost always faces world +Z (yaw=0) → movement direction diverges from local prediction → visible drift between blue and green hurtboxes.\n\n**Size evolution:** 32 → 34 → 38 → 39 → 40 bytes
| Field | Size | Offset |
|-------|------|--------|
| TickNumber | 4B | 0-3 |
| PX,PY,PZ,VX,VY,VZ | 24B | 4-27 |
| State, IsGrounded | 2B | 28-29 |
| StateTicks | 2B | 30-31 |
| AttackSlot | 1B | 32 |
| ComboStage | 1B | 33 |
| FacingYaw | 4B | 34-37 |
| MatchState | 1B | 38 |
| AnimIndex | 1B | 39 |\n\nAdd in 4 places:\n- `FromState()` → map `s.AttackSlot` / `s.ComboStage` / `s.FacingYaw` / `s.AnimIndex`\n- `ToState()` → map `AttackSlot` / `ComboStage` / `FacingYaw` / `AnimIndex`\n- `Serialize()` → write `buffer[32]` / `buffer[33]` / `buffer[34..37]` / `buffer[39]`\n- `Deserialize()` → read `buffer[32]` / `buffer[33]` / `buffer[34..37]` / `buffer[39]`\n\nAlso update `Size` constant: `4+4+4+4+4+4+4+1+1+2+1+1+1+4+1 = 40`.

## Platform-Aware Ground Collision

Simulation.cs uses `GetGroundSurfaceY()` instead of a single `FloorHeight` check.

### How it works

After gravity + position update (`s.PY += s.VY * TickDt`), the sim checks ALL platform surfaces in the arena:

```csharp
float capsuleHalf = def.CapsuleHeight * 0.5f;
float bestSurfaceY = GetGroundSurfaceY(s.PX, s.PZ, s.PY, capsuleHalf, arena);
float groundY = bestSurfaceY + capsuleHalf;
if (s.PY <= groundY + PlatformLandTolerance && s.PY >= groundY - PlatformSnapTolerance)
{
    s.IsGrounded = true;
    s.PY = groundY;
    s.VY = 0f;
}
```

### `GetGroundSurfaceY` algorithm

1. Start with `bestSurfaceY = arena.FloorHeight` (main floor fallback)
2. Iterate over `arena.Platforms[]`
3. For each platform, check XZ bounds: `|px - centerX| <= halfSizeX && |pz - centerZ| <= halfSizeZ`
4. Check vertical tolerance: capsule center is at or slightly below the standing height for that platform
5. Pick the **highest** matching surface (handles overlapping platforms like a ramp on lower floor)

### Constants

- `PlatformSnapTolerance = 0.5f` — prevents snapping UP to a platform from below
- `PlatformLandTolerance = 0.2f` — how far above the surface the character can be to still land

### Two call sites

- **SimulateTick()** step 7 — normal ground collision
- **ProcessKnockback()** — ground check during knockback trajectory

Both use the same `GetGroundSurfaceY` helper with the same tolerance constants.

### Data source

Platform definitions come from `.arena` binary files (baked from Godot CSGBox3D scenes) or from hardcoded `ArenaRegistry`. See the `sloparena-arena-system` skill for the baking pipeline and exact platform mappings.

### Pitfall: upward snap prevention

Without the `py >= standCenterY - snapTolerance` check, a character below a platform would snap UP to it (e.g., character at Y=3.0 would snap to a platform whose standing center Y is 6.5). The tolerance window prevents this correctly.

### Pitfall: platform priority

When a character is within XZ bounds of multiple platforms (e.g., standing on a ramp at Y=1 inside a lower floor at Y=0), the HIGHEST surface wins. This is correct for multi-level arenas.

When rollback creates a new `ServerSimulation`, only the player is re-registered. NPCs are NOT re-registered. The server does not send NPC states to the client, so the client cannot restore NPCs from server data. After rollback, NPCs in the local sim are LOST until the next full state update. This is a known limitation for localhost testing.

```csharp
// In MatchManager._Process rollback (current - only player, no NPCs):
_localSim = new ServerSimulation(_arenaDef);
_localSim.RegisterEntity(_playerEntityId, _charDef, serverState, _playerSkel);
// NPCs are NOT re-registered here
```

The skeleton argument (`_playerSkel`) must be passed to RegisterEntity during rollback, or the sim falls back to static capsules (5 per entity, r=0.35). The skeleton is loaded once in StartMatch and stored in `_playerSkel` as a class field.

### 11. SpellResolver static state is a thread-safety bug (FIXED June 2026)

`SpellResolver` was a `static class` with `static List<Hitbox> _hitboxes`. When `MultiMatchOrchestrator` spawns multiple `MatchInstance` threads, all matches share the same hitbox list — race condition corrupts hit detection.

**Fix:** Convert `SpellResolver` to instance class. Add `_spellResolver` field to `ServerSimulation` and `CombatComponent`. Each match/sim gets its own hitbox list. Expose via `ServerSimulation.Resolver` for debug visualization.

```csharp
// Before (race condition):
public static class SpellResolver
{
    private static readonly List<Hitbox> _hitboxes = new();
    public static void Spawn(Hitbox hb) { ... }
}

// After (thread-safe):
public class SpellResolver
{
    private readonly List<Hitbox> _hitboxes = new();
    public void Spawn(Hitbox hb) { ... }
}

// ServerSimulation:
private readonly SpellResolver _spellResolver = new();
// Use: _spellResolver.Spawn(hb), _spellResolver.Tick(entities)
```

All call sites updated: `ServerSimulation` (server), `CombatComponent` (client sandbox), `MatchManager.GetDebugData()` (debug viz).

### 13. Match lifecycle — countdown, death tracking, game over (ADDED June 2026)

The server now manages a full match lifecycle with `MatchState` enum (Shared/MatchState.cs):
- **Waiting**: waiting for second player to connect
- **Countdown**: 180 ticks (3s) countdown after P2 connects
- **Playing**: match is live, inputs processed, deaths tracked
- **Ended**: first to 3 deaths loses, 180-tick post-match display, then server stops

**Death tracking** uses `CharacterState.Deaths` (byte, server-authoritative). `ServerSimulation.Tick()` increments it on void death before respawning the entity. `MatchInstance.Tick()` checks after each `_sim.Tick()` — when either player reaches `MaxDeaths = 3`, the match ends.

**MatchState is broadcast** in `CharacterStatePacket` at byte offset 38. Set by the server in `SendState()` before serialization: `p1Packet.MatchState = _matchState;`. The client reads it from received packets for UI updates (countdown display, game over screen).

**Post-match:** after Ended state, `_postMatchTicks` counts down for 180 ticks (3s), then `_running = false` — server frees the port.

### 14. Opponent PlayerController rendering (ADDED June 2026)

For PvP, the client spawns an `Opponent` PlayerController (entity ID 2) alongside the local `Player` (entity ID 1):
- Opponent uses `SetNPC(true)` — no local input processing, FSM driven by server state
- Opponent is added to "enemies" group for TargetLockSystem
- Opponent state is applied from `_localSim.GetState(OpponentEntityId)` in `_PhysicsProcess` (predicted)
- Opponent state is also applied from `serverStates[OpponentEntityId]` in `_Process` (server-authoritative)
- Opponent is preserved during rollback: snapshot state before sim replacement, re-register in new sim
- Target ring and `SetTarget()` support entity ID 2 alongside NPC IDs (100-104)

### 15. Wire format must match on both sides (PITFALL)

The server and client MUST use the same wire format. The client (NetworkClient) expects `entityId(8) + tick(4) + payload`. If the server sends raw payload without the envelope, packets are silently rejected (too small). Always verify both send AND receive paths when changing packet formats.

**Verified format (June 2026):**
- Client → Server: `entityId(8) + tick(4) + InputState(14)` = 26 bytes
- Server → Client: `entityId(8) + tick(4) + CharacterStatePacket(40)` = 52 bytes per entity

### 16. Godot regenerates deleted .tscn files (PITFALL)

Godot automatically regenerates empty `.tscn` files when the editor is opened, even if they were deleted. `rabbit.tscn` in `assets/characters/bunny/` is a known case — it's a 3-line empty scene with no references. If you delete it, Godot will recreate it. Either leave it or add it to `.gitignore`. Do NOT keep re-deleting it — it's harmless and Godot will just bring it back.

### 12. Connection timeout — dead players must not hold matches forever (ADDED June 2026)

Without timeout detection, a disconnected player leaves the match running with zero-input forever. The server port is never freed.

**Implementation in MatchInstance:**
```csharp
private DateTime _lastP1Packet = DateTime.UtcNow;
private DateTime _lastP2Packet = DateTime.UtcNow;
private const double TimeoutSeconds = 5.0;

// Update on each received packet (ReceiveInputs):
if (isP1) _lastP1Packet = DateTime.UtcNow;
if (isP2) _lastP2Packet = DateTime.UtcNow;

// Also update on initial connection to avoid immediate timeout.

// Check in tick loop (before Tick()):
var now = DateTime.UtcNow;
if ((now - _lastP1Packet).TotalSeconds > TimeoutSeconds ||
    (now - _lastP2Packet).TotalSeconds > TimeoutSeconds)
{
    Console.WriteLine($"[Match] Player timed out — stopping match.");
    _running = false;
    continue;
}
```

### 8. ServerApp is a separate project — NOT in solution

ServerApp/ is a standalone .NET project NOT in SlopArena.sln. `dotnet build` skips it silently. Changes to ServerApp/Program.cs are compiled ONLY when you explicitly run:

```bash
dotnet build ServerApp/
```

Always rebuild BOTH:
```bash
cd ServerApp && dotnet build && cd .. && dotnet build
```

**Without this:** the server runs the OLD DLL with hardcoded CharacterClass.Manki. Client predicts Bunny stats but server uses Manki stats overshoot desync.

## Hurtbox Y Alignment — Bake-Time Normalization + Server Formula

### The problem

Baked bone positions are recorded in **rest-pose Hips-local space** (`rest_hips_inv * pose.origin`). With correct bake normalization (lowest idle bone → Y=0), the server formula should be `wy = py - capsuleHalf + by` so the foot hurtbox lands at the capsule bottom (= ground level).

### ⭐ Pitfall: Tick() and BuildEntitiesFromState() must use the SAME Y formula

`ServerSimulation.cs` has TWO places that compute hurtbox Y:

| Method | Used for | Formula |
|--------|----------|---------|
| `Tick()` (line ~250) | **Actual hurtbox detection** (SpellResolver) | `wy = py - CapsuleHeight*0.5f + by` |
| `BuildEntitiesFromState()` (line ~95) | Server ghost debug view | `wy = py - CapsuleHeight*0.5f + by` |

**If they disagree, the debug view shows correct positions but hit detection uses wrong ones** — making the bug invisible to F3 debug. Always verify both use the same formula after any change.

The old code had `Tick()` using `wy = py + by + soleY` (missing `-capsuleHalf`) while `BuildEntitiesFromState()` already had `wy = py - capsuleHalf + by`. This made all server hurtboxes float `capsuleHalf` meters above where they should be, while the F3 debug view looked correct.

### `ModelSoleOffset` must NOT appear in either method

`ModelSoleOffset` is a visual fine-tuning value for `PlayerModel.ComputeModelYOffset()` only. Applying it server-side is double-counting and will push hurtboxes underground for one character while fixing another.

### After bake normalization: character values

| Character | HurtboxBoneScale | idle lowest bone | ModelSoleOffset | Notes |
|-----------|-----------------|-----------------|-----------------|-------|
| Manki | 0.01 | ≈ 0.0 (RightToeBase) | ~0.04–0.08 (physical sole) | |
| Bunny | 0.022 | ≈ 0.0 (RightToe_End) | ~0.04–0.08 (physical sole) | digitigrade — toe end is ground contact |

Large `ModelSoleOffset` values (>0.2) are diagnostic — bake normalization was wrong.

### ⭐ Pitfall: visual model leans forward in run but hurtboxes lag behind

**Symptom:** During run animation, the model leans forward but the hurtboxes appear slightly behind. Not a game-breaking desync, but visible.

**Likely cause:** 1-frame offset between server animation frame counter and client visual playback. They start at different moments — server starts when entity is registered, client when FSM transitions to run.

**Diagnostic pattern** (add in TrainingMatch._PhysicsProcess):
```csharp
if (Engine.GetPhysicsFrames() % 60 == 0)
{
    var state = _localSim.GetState(1);
    if (state.VX*state.VX + state.VZ*state.VZ > 1f)
    {
        var skel = Player.PlayerModel.FindChild("Skeleton3D", true, false) as Skeleton3D;
        int headIdx = skel.FindBone("mixamorig_Head");
        Vector3 visHead = skel.GlobalTransform * skel.GetBoneGlobalPose(headIdx).Origin;
        var entities = _localSim.GetLastEntityData();
        GD.Print($"[RunDiag] head visual=({visHead.X:F2},{visHead.Y:F2},{visHead.Z:F2}) " +
                 $"server=({entities[0].PosX:F2},{entities[0].PosY:F2},{entities[0].PosZ:F2}) " +
                 $"yaw={state.FacingYaw*57.3f:F1}°");
    }
}
```

Requires `public Node3D? PlayerModel => _playerModel;` on PlayerController.

### See also
- `references/hurtbox-y-normalization.md` — full bake normalization diagnosis, pitfalls, Python bin-inspection script

## References

- `docs/netcode-architecture.md` — full design doc
- `references/server-audit-june2026.md` — two-server problem, integration fixes, dead code audit
- `references/attack-system-debugging-June2026.md` — attack pipeline debugging
- `references/facing-yaw-sync.md` — facing direction sync details
- `ServerApp/Program.cs` — prototype server (now superseded by Server/MatchInstance)
- `Scripts/Network/NetworkClient.cs` — client UDP send/receive
- `Scripts/World/MatchManager.cs` — client orchestration + rollback
- `Shared/ServerSimulation.cs` — core simulation (tick, hitbox spawning)
- `Shared/InputState.cs` — 14-byte input struct
- `Shared/CharacterState.cs` — full per-tick state
- `Shared/CharacterStatePacket.cs` — 40-byte serialized subset (includes AnimIndex at offset 39, MatchState at offset 38)
