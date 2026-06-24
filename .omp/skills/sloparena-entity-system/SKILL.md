---
name: sloparena-entity-system
description: SlopArena entity architecture — NPCs, hitboxes, entity IDs, processing order, bone attachments, and per-character components
---

# SlopArena Entity System

Architectural patterns for SlopArena's entity/hitbox/NPC system. Covers identity management, hit detection pipeline, NPC input flow, processing order, and character-specific component extraction.

**Current architecture (June 2026):** Server-authoritative with a **separate .NET console process** (`ServerApp/`) communicating via UDP localhost. The Godot client runs its **own local `ServerSimulation`** for instant feedback (local prediction), then syncs with the external server. `SimulateTick` now handles **all actions** from `InputState` (movement, jump, dash, attack). `LocalServerBridge` (in-process) has been replaced by `NetworkClient` (UDP send/receive) + a local `ServerSimulation` for prediction. See `docs/netcode-architecture.md`, `docs/hitbox-system.md`, `docs/attack-hitbox-system.md` for full architecture docs.

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

## Tick Buffer + Rollback (June 2026)

The client maintains a ring buffer of the last 30 `InputState`s and the last 30 predicted `CharacterState`s (in PvPMatch). When a server state arrives tagged with a tick number, the client compares and optionally rolls back.

### Architecture

```
Client                          Server
  sendTick++                      │
  _inputBuffer[tick%30] = input   │
  localSim.Tick(input)            │
  _stateBuffer[tick%30] = prédit  │
  Net.SendInput(input, tick) ────►│
                                  │  sim.Tick(inputs)
  ◄── Net.ReceiveStates() ───────│  (echoes client tick in response)
  │                               │
  └─ Compare stateBuffer[server.tick] vs server.state
     si gap > 0.01m:
       1. state = server.state (authority)
       2. Re-create localSim
       3. Re-sim tick+1 → currentTick with buffered inputs
       4. Apply corrected state
```

### Packet Format with Tick

**Client → Server** (21 bytes):
```
[0..7]   entityId  (ulong)
[8..11]  tick      (uint, client's send tick)
[12..20] InputState (9 bytes: MoveX + MoveY + flags byte)
```

**Server → Client** (44 bytes per entity):
```
[0..7]   entityId  (ulong)
[8..11]  tick      (uint, echoed from client's input)
[12..43] CharacterStatePacket (32 bytes: position + velocity + action state + grounded + state ticks)
```

### PvPMatch._PhysicsProcess (local prediction + send)

```csharp
public override void _PhysicsProcess(double delta)
{
    // 1. Build & buffer input
    var input = Player.GetCurrentInput();
    _sendTick++;
    _inputBuffer[_sendTick % RollbackFrames] = input;

    // 2. Local prediction
    _localSim.Tick(new Dictionary<ulong, InputState> { { 1, input } });

    // 3. Buffer predicted state
    _stateBuffer[_sendTick % RollbackFrames] = _localSim.GetState(1);

    // 4. Send input + tick to server
    Net.SendInput(input, _sendTick);

    // 5. Render predicted state
    Player.ApplyServerState(predicted);
}
```

### PvPMatch._Process (reconciliation)

```csharp
public override void _Process(double delta)
{
    var serverStates = Net.ReceiveStates();

    if (serverStates.TryGetValue(1, out var server) && server.tick > _lastConfirmedTick)
    {
        _lastConfirmedTick = server.tick;
        var predicted = _stateBuffer[server.tick % RollbackFrames];

        float dy = predicted.PY - server.state.PY;
        if (MathF.Abs(dy) > 0.01f)
        {
            // ── ROLLBACK ──
            _localSim = new ServerSimulation(_arenaDef);
            _localSim.RegisterEntity(1, _charDef, server.state);
            for (uint t = server.tick + 1; t <= _sendTick; t++)
                _localSim.Tick(new Dictionary<ulong, InputState> { { 1, _inputBuffer[t % RollbackFrames] } });
            Player.ApplyServerState(_localSim.GetState(1));
        }
    }

    // NPCs: apply server state directly (authority)
    for (int i = 0; i < NpcCount; i++)
        if (serverStates.TryGetValue((ulong)(100 + i), out var npc))
            NPCs[i].ApplyServerState(npc.state);
}
```

**On localhost:** the server responds in <1ms → predicted state always matches server state → zero rollback. The architecture is ready for when the server is remote.

### RollbackFrames

```
private const int RollbackFrames = 30;  // ring buffer size (PvPMatch)
```

In `TrainingMatch`, there is no rollback — the sim runs locally and states are authoritative.

## ⭐ Workflow Rule — Prefer Proper Architecture Over Quick Fixes

Binoui does NOT want hacky workarounds or "quick fixes" (hardcoded offsets, ApplyServerState preservation hacks, temporary velocity negations). When he says "fais la vraie solution", he expects the correct architectural fix — even if it takes more code or touches more files. Signal: frustration with hardcoded values ("ça me dérange cette valeur en dur") or workaround comments ("TEMP"). If you find yourself writing a workaround, stop, explain the architecture gap, and propose the proper fix.

## Entity ID Management

Each entity (player or NPC) needs a **unique, consistent entity ID** from spawn to combat resolution.

### The Rule

The entity ID lives in **`CombatComponent._entityId`**, set via `SetupCombat()`. It is the `OwnerId` on every hitbox this entity spawns. **Do NOT** put it in `MovementComponent` or `CharacterState` — MovementComponent handles movement only.

### Pipeline

```
Main.cs spawn → SetupCombat(simulation, arenaDef, entityId, spellVFX)
                  ↓
            CombatComponent.Setup(owner, simulation, entityId, ...)
                  ↓
            hb.OwnerId = _entityId  (on every spawned hitbox)
```

### IDs Convention

| Entity | ID range |
|--------|----------|
| Player | `1` |
| NPCs | `100-104` (100 + index) |

### Why Not in MovementComponent
`CharacterState.EntityId` was used in the old `ResolveAbilityStages` for an invincibility check, but that function has been removed. The real identity lives in `CombatComponent`.

## Self-Collision Prevention

`SpellResolver.Tick()` filters self-hits at line 99:

```csharp
if (!entity.Active || entity.Id == hb.OwnerId) continue;
```

This works **only if** every entity's CombatComponent has its real entity ID. The classic bug: hardcoding `1` in `SetupCombat()` makes every NPC's hitbox carry `OwnerId=1`, but they're registered in the simulation as `100-104` → self-filter never triggers.

### Fix Checklist

- [ ] `SetupCombat()` accepts `ulong entityId` parameter
- [ ] `Main.cs` passes correct IDs (player=1, NPCs=100+i)
- [ ] `CombatComponent.Setup()` receives and stores the real ID

### NPC Architecture

### ⭐ Pitfall — NPCs Missing SetBakedData (Floating Model/Hurtboxes)

NPCs created in `TrainingMatch.SpawnNPCs()` and similar match code are **not** given baked skeleton data:

```csharp
// TrainingMatch.cs — Player gets baked data, NPC does NOT:
Player.SetBakedData(_playerBakedData);   // ✅
npc.Position = ...;                        // ❌ No SetBakedData() call
```

When `_bakedData` is null, `PlayerModel.ComputeModelYOffset()` falls back to `ModelYOffset = 0` (default), meaning the visual model sits at capsule center instead of being pushed down to align feet with capsule bottom. Symptoms:

- **Hurtboxes float ~0.5m above the character's feet** (model at Y=0 when it should be at a negative offset)
- **Symptom is character-dependent** — Manki's default `ModelYOffset=0` happens to be close enough to the auto-computed value (feet near Y=0 in mesh), so it looks fine. Bunny's auto-computed offset is very different from 0, making the float visible.
- **NPC baked data must match the NPC's character class**, not the player's. If NPC class differs from player class, using player's baked data gives wrong skeleton proportions.

**Fix pattern** — load baked data per-NPC class:

```csharp
// In SpawnNPCs():
var npcClass = i % 2 == 0 ? CharacterClass.Manki : CharacterClass.Bunny;
npc.SetClass(npcClass);
var npcDef = CharacterRegistry.Get(npcClass);
var npcBaked = LoadBakedDataFromDef(npcDef);
npc.SetBakedData(npcBaked);
npc.Position = ...;
```

**Debugging tip**: When model/hurtboxes float, add `GD.Print($"[ModelY] fallback={_charDef.ModelYOffset}")` in `ComputeModelYOffset()`. If you see `Auto: ...` logged → baked data present. If only `Fallback: ModelYOffset=0` → baked data missing.

### Structure

NPCs are `PlayerController` instances with `_isNPC = true`. They have all the same components as the player: MovementComponent, CombatComponent, AnimationController, BoneHurtboxSetup.

```
PlayerController (_isNPC = true)
  ├── BotController (child, ProcessPriority = -1)
  │     └── Injects InputState → InputController.InjectAI()
  ├── MovementComponent
  ├── CombatComponent
  └── BoneHurtboxSetup
```

### Input Pipeline

```
BotController._PhysicsProcess()
  → Build InputState (world-space direction)
  → _npc.InjectInput(input)
      → _inputCtrl.InjectAI(input)
      → _inputCtrl sets _aiControlled = true

PlayerController._PhysicsProcess() (NPC branch)
  → BuildInputState()
      → _inputCtrl.GetMovement() → AI MoveX/MoveY
      → _inputCtrl.JumpJustPressed, DashJustPressed, IsAttackPressed()
  → _movementComponent.Tick(npcInput)  -- COMMENTED OUT: sim handles movement
  → GlobalRotation update from velocity

**Note:** `_movementComponent.Tick()` is disabled for NPCs. Movement simulation is handled entirely by `ServerSimulation` in the match's `_PhysicsProcess`. The NPC's `_PhysicsProcess` only builds input state and updates visual rotation.
```

### Critical: Processing Order

**Problem**: BotController is a child of PlayerController. Godot processes parent `_PhysicsProcess` before children. So AI input injected by BotController arrives AFTER PlayerController already read it → stale input.

**Fix**: Set `ProcessPriority = -1` on BotController in its `Setup()` method. Lower values process first.

```csharp
public void Setup(PlayerController npc, PlayerController target)
{
    ProcessPriority = -1; // Process BEFORE parent
    // ...
}
```

### NPC Attack

BotController calls `_npc.UseAbility(slot)` which **after the ability refactor**
sets `_npc._pendingSlotPress = (byte)(slot + 1)`. The sim picks it up via
`BuildInputState` → `ActiveSlot` on the next frame, same as human player input.
The NPC branch of `BuildInputState` MUST read `_pendingSlotPress` (it currently
doesn't — added in the plan).


**Removed (June 2026):** `ExecuteSlot` + `ExecuteAttackStage` were deleted. The old `UseAbility` → `ExecuteSlot()` direct path is gone. All abilities now route through `_pendingSlotPress` → `BuildInputState` → `input.ActiveSlot` → `ServerSimulation` → `HitboxEvent`. See `docs/systems/ability-architecture.md` for details.


The NPC rotation (`GlobalRotation`) is updated from velocity in the NPC branch of `_PhysicsProcess`:

```csharp
Vector3 npcHVel = new Vector3(Velocity.X, 0f, Velocity.Z);
if (npcHVel.LengthSquared() > 0.01f)
    GlobalRotation = new Vector3(0f, Mathf.Atan2(npcHVel.X, npcHVel.Z), 0f);
```

This was missing before — NPCs never turned to face their movement direction.

## Server Simulation Architecture (June 2026 — UDP Separate Process)

**Architecture shift:** The game now runs a **pure C# ServerSimulation** in a **separate .NET console process** (`ServerApp/`), communicating via UDP localhost. The Godot client is a stateless renderer. This is the SAME architecture as online multiplayer — the only difference is the UDP address (localhost vs remote).

### Architecture Diagram

```
Godot Client (renderer only)          ServerApp (console .NET)
═══════════════════════════            ══════════════════════════
Main._Ready                            Program.Main()
  │                                      │
  ├─ StartLocalServer() ──── spawn ───►  UDP listen :9876
  │                                      │
PvPMatch._PhysicsProcess (60Hz)
  │                                      │
  1. BuildInputState()                   │
  2. Net.SendAndReceive(input) ──send──►  │
  │                                      ├─ ServerSimulation.Tick(inputs)
  │                                   ◄──├─ CharacterState[] per entity
  3. ApplyServerState(state)             │
  4. ApplyServerState(npcStates)         │
  5. (repeat next frame)                 │
```

### Key Files

| File | Role |
|------|------|
| `ServerApp/Program.cs` | UDP server process: listen, 60Hz tick, broadcast |
| `ServerApp/ServerApp.csproj` | Console project referencing `Shared/` |
| `Scripts/Network/NetworkClient.cs` | Godot UdpClient wrapper: send input / receive states |
| `Scripts/World/PvPMatch.cs` | Game loop (PvP): build input → Net.SendAndReceive → ApplyServerState |
| `Scripts/World/Main.cs` | Orchestrator: starts server process via `System.Diagnostics.Process` |
| `Shared/InputState.cs` | Pure C# serializable input struct (Write/Deserialize) |
| `Shared/CharacterStatePacket.cs` | Wire format for CharacterState (FromState/ToState) |

### Starting the Server

In `Main.cs._Ready`:

```csharp
private void StartLocalServer()
{
    var projectDir = ProjectSettings.GlobalizePath("res://");
    var serverDir = Path.GetFullPath(Path.Combine(projectDir, "ServerApp"));
    var dllPath = Path.Combine(serverDir, "bin", "Debug", "net8.0", "ServerApp.dll");
    if (File.Exists(dllPath))
    {
        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dllPath}\"",
                WorkingDirectory = serverDir,
                CreateNoWindow = true,
                UseShellExecute = false,
            }
        };
        _serverProcess.Start();
    }
}
```

Clean up on exit:
```csharp
public override void _ExitTree()
{
    _serverProcess?.Kill();
    _serverProcess?.Dispose();
}
```

### ServerApp (Program.cs)

The standalone server:

```csharp
static void Main()
{
    var udp = new UdpClient(9876);
    var sim = new ServerSimulation(arena);
    var charDef = CharacterRegistry.Get(CharacterClass.Manki);
    sim.RegisterEntity(1, charDef, initialState);

    var inputBuffer = new Dictionary<ulong, InputState>();
    var clients = new Dictionary<ulong, IPEndPoint>();

    _ = Task.Run(() => ReceiveLoop(udp, clients, inputBuffer));

    var tickInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
    var nextTick = DateTime.UtcNow + tickInterval;

    while (true)
    {
        if (DateTime.UtcNow >= nextTick)
        {
            nextTick += tickInterval;
            sim.Tick(inputBuffer);
            inputBuffer.Clear();

            foreach (var kvp in clients)
            {
                foreach (var state in sim.GetAllStates())
                {
                    var packet = CharacterStatePacket.FromState(state.Value);
                    byte[] buf = new byte[CharacterStatePacket.Size + 8];
                    BitConverter.TryWriteBytes(buf.AsSpan(0, 8), state.Key);
                    packet.Serialize(buf.AsSpan(8));
                    udp.Send(buf, buf.Length, kvp.Value);
                }
            }
        }
        else Thread.Sleep(1);
    }
}

static void ReceiveLoop(UdpClient udp, ..., Dictionary<ulong, InputState> inputBuffer)
{
    while (true)
    {
        IPEndPoint? remote = null;
        byte[] data = udp.Receive(ref remote);
        ulong entityId = BitConverter.ToUInt64(data, 0);
        if (!clients.ContainsKey(entityId)) clients[entityId] = remote;
        inputBuffer[entityId] = InputState.Deserialize(data.AsSpan(8));
    }
}
```

### Wire Protocol (June 2026 — with tick + ActiveSlot)

**Client → Server** (22 bytes):\n- 8 bytes: entityId (ulong, little-endian)\n- 4 bytes: tick (uint, client's send tick for rollback matching)\n- 4 bytes: MoveX (float)\n- 4 bytes: MoveY (float)\n- 1 byte: flags (Up/Down/Left/Right/Jump/Dash/Crouch/Attack)\n- 1 byte: ActiveSlot (0=none, 1=LMB, 2=RMB, 3=Q, 4=E, 5=R, 6=F)\n\n**Total: 22 bytes** (was 17 before ActiveSlot, corrected from 21 in June 2026)

**Server → Client** (44 bytes per entity):
- 8 bytes: entityId (ulong, little-endian)
- 4 bytes: tick (uint, echoed from client's send tick)
- 4+4+4 bytes: PositionX/Y/Z (float)
- 4+4+4 bytes: VelocityX/Y/Z (float)
- 1 byte: ActionState (enum)
- 1 byte: IsGrounded (bool)
- 2 bytes: StateDurationFrames (ushort)

### PvPMatch Game Loop (Client) — Local Prediction + Server Sync

The client runs its own `ServerSimulation` locally for instant feedback. The server (external process) is the authority.

```csharp
public override void _PhysicsProcess(double delta)
{
    // 1. Build input from keyboard/mouse
    var input = Player.GetCurrentInput();

    // 2. Local simulation (predicts immediately)
    _localSim.Tick(new Dictionary<ulong, InputState> { { 1, input } });
    var predicted = _localSim.GetState(1);
    Player.ApplyServerState(predicted);

    // 3. Send input to server
    Net.SendInput(input);
}

public override void _Process(double delta)
{
    // 4. Receive server states (non-blocking)
    var serverStates = Net.ReceiveStates();

    // 5. Compare predicted vs server — correct if mismatch
    if (serverStates.TryGetValue(1, out var serverState))
    {
        float dy = localState.PY - serverState.PY;
        if (dy > 0.05f)  // rollback threshold
            Player.ApplyServerState(serverState);
    }
}
```

**On localhost:** the server responds in <1ms (same machine). The predicted state always matches the server state → zero rollback. The architecture is ready for when the server is remote.
```

### NetworkClient (Godot UdpClient Wrapper)

```csharp
public partial class NetworkClient : Node
{
    private UdpClient? _udp;
    private IPEndPoint _serverEp = new(IPAddress.Loopback, 9876);
    private ulong _entityId;
    public Dictionary<ulong, CharacterState> Opponents { get; } = new();

    public void Connect(ulong entityId)
    {
        _udp = new UdpClient();
        _udp.Connect(_serverEp);
    }

    // Non-blocking send (called from _PhysicsProcess)
    public void SendInput(InputState input) { ... }

    // Non-blocking receive (called from _Process)
    public Dictionary<ulong, CharacterState> ReceiveStates() { ... }

    // Legacy: send + receive in one call
    public CharacterState? SendAndReceive(InputState input) { ... }
}
```

### Build & Run

```bash
# Build both projects (required before running the game)
dotnet build                    # Godot project (builds Shared + Scripts)
dotnet build ServerApp/         # Standalone server

# The game starts ServerApp automatically on launch via Process.Start
```

### InputState Serialization (Shared/)

### InputState Serialization (Shared/)

```csharp
// Size: 10 bytes (2 floats + 1 byte flags + 1 byte ActiveSlot)
public struct InputState
{
    public float MoveX, MoveY;          // normalized movement direction
    public bool Up, Down, Left, Right, Jump, Dash, Crouch, Attack;
    public byte ActiveSlot;              // 0=none, 1=LMB, 2=RMB, 3=Q, 4=E, 5=R, 6=F

    public const int Size = 8 + 1 + 1;
    public void Write(Span<byte> buf);           // little-endian
    public static InputState Deserialize(...);
}
```

**ActiveSlot** is how ALL abilities tell the sim what attack to execute. The client sets `_pendingSlotPress = X` in `_UnhandledInput`, and `BuildInputState()` consumes it into `input.ActiveSlot`. The sim reads it and picks the correct `AbilityData`. This replaces the old `ExecuteSlot` → `SetPendingResolve` pipeline for ALL slots.

**Wire protocol update:** packet is now 21 bytes (was 17): entityId(8) + tick(4) + MoveX(4) + MoveY(4) + flags(1) + ActiveSlot(1).

### CharacterStatePacket (Shared/)

```csharp
// Size: 36 bytes (position + velocity + action state + grounded + state ticks)
public struct CharacterStatePacket
{
    public uint TickNumber;
    public float PositionX, PositionY, PositionZ;
    public float VelocityX, VelocityY, VelocityZ;
    public byte CurrentActionState;
    public bool IsGrounded;
    public ushort StateDurationFrames;

    public void Serialize(Span<byte> buffer);               // to wire
    public static CharacterStatePacket Deserialize(...);    // from wire
    public static CharacterStatePacket FromState(CharacterState s, uint tick = 0);
    public CharacterState ToState();
}
```

### ⭐ Pitfall: Server DLL Path in StartLocalServer

The server DLL path must match the actual project layout:

```csharp
// CORRECT (ServerApp is INSIDE the SlopArena project):
var serverDir = Path.Combine(projectDir, "ServerApp");  // NO ".."

// WRONG (goes up one level):
var serverDir = Path.Combine(projectDir, "..", "ServerApp");  // finds wrong dir
```

`ProjectSettings.GlobalizePath("res://")` returns the **project root** (where `project.godot` is). ServerApp is at `projectRoot/ServerApp/`, not at `projectRoot/../ServerApp/`.

### ⭐ Pitfall: NetworkClient.SendAndReceive Blocks Briefly

The `_udp.Receive()` inside `SendAndReceive` blocks until data arrives. Since the server runs at 60Hz and the client sends at 60Hz, the receive should return immediately. But if a frame is dropped, the client blocks for up to 16ms. Consider adding a receive timeout (1ms) and returning the last known state if nothing arrives:

```csharp
_udp.Client.ReceiveTimeout = 1;
try { recvBuf = _udp.Receive(ref remoteEp); }
catch { return _lastState; }  // use last-known-good state
```

### InputState Size Mismatch Warnings

The `InputState.Deserialize` expects exactly `InputState.Size` bytes (9). If the server sends a wrong-sized packet, it'll silently corrupt data. Validate length:

```csharp
if (data.Length < InputState.Size + 8) continue;  // 8 for entityId
```

Same for `CharacterStatePacket.Deserialize`:
```csharp
if (recvBuf.Length < CharacterStatePacket.Size + 8) continue;
```

### Future: Multiplayer

- Each additional player connects to the same server:port
- Different `entityId` (2, 3, 4...)
- Server registers them on first packet
- Server broadcasts ALL states to ALL clients
- Client filters: `if (eid == mine) myState = ... else opponents[eid] = ...`

### Future: BotNPCs as Threads

Each bot runs on its own thread (or Task), reading the server state and generating InputState:

```csharp
Task.Run(() => BotThread(serverState, botInputQueue));
// Server polls InputState from the queue as if it came from a network client
```

See `docs/netcode-architecture.md` for the full architecture doc (packet protocol, rollback design, implementation phases).

See `docs/hitbox-system.md` for the English hitbox architecture doc (revised June 2026).

See `docs/attack-hitbox-system.md` for the French attack system design doc (historical — same concepts, French prose).

See `references/attack-hitbox-system.md` for the attack system reference (HitboxEvent struct, example Manki stages, sim tick processing).

## LocalServerBridge (Godot Bridge for ServerSimulation)

`Scripts/Server/LocalServerBridge.cs` wraps the pure C# `ServerSimulation` in a Godot `Node`, handling event routing (damage/status FX) and debug hurtbox visualization. It is still actively used by `CombatComponent`, `StatusComponent`, and the debug pipeline.

**Note on match types:** `TrainingMatch` uses `ServerSimulation` directly (no bridge), while `LocalServerBridge` is available for cases needing Godot-side event wiring. The UDP separate-process path (`PvPMatch` + `NetworkClient` + `ServerApp`) bypasses `LocalServerBridge` entirely — it communicates over the wire.

Architecture:
```
TrainingMatch           PvPMatch
    │                       │
    ├─ ServerSimulation     ├─ NetworkClient (UDP)
    │  (direct)             │     → remote ServerApp
    │                       │     → local ServerSimulation (prediction)
    ├─ ApplyServerState     ├─ ApplyServerState (reconciled)
    ├─ (no bridge)          ├─ (no bridge)
```

LocalServerBridge is still used for event wiring when creating entities through the bridge path:
```csharp
// LocalServerBridge → events → CombatComponent:
Bridge.Tick(inputs);  // runs ServerSimulation.Tick() + fires events
```

### `OwnerEntityId` in HitResult

`SpellResolver.HitResult` now includes `OwnerEntityId` (set from `hb.OwnerId` inside `SpellResolver.Tick()`). This enables the bridge to fire `OnDealDamage` events for the attacker:

```csharp
public struct HitResult
{
    public ulong TargetEntityId;
    public ulong OwnerEntityId;  // ← added for hitbox owner tracking
    ...
}
```

### Platform-Aware Ground Collision (June 2026 — REMOVED from Simulation.cs)

The old hardcoded `Simulation.FloorHeight = 0f` constant has been **removed**. Ground collision is now platform-aware:

- See `sloparena-netcode` → "Platform-Aware Ground Collision" for how `GetGroundSurfaceY()` works in `Simulation.cs`
- See `sloparena-arena-system` for the arena baking pipeline, `PlatformDef` format, and `.arena` binary files
- `ArenaDefinition.FloorHeight` is the main floor fallback (lowest walkable surface)

**⭐ Critical Pitfall — FSM stuck in "air" because `Player.IsOnFloor()` requires downward velocity:**
When the server sets `CharacterState.IsGrounded = true` and `VY = 0`, Godot's `MoveAndSlide()` does NOT detect floor contact because the character isn't moving toward the floor. `CharacterBody3D.IsOnFloor()` returns `false` when VY = 0. ALL FSM states that used `Player.IsOnFloor()` need to use `Movement.IsGrounded` instead:

| State | File | Change |
|-------|------|--------|
| AirState | `AirState.cs:58` | `if (Player.IsOnFloor() && vy <= 0f)` → `if (Movement.IsGrounded && vy <= 0f)` |
| IdleState | `IdleState.cs:33` | `if (!Player.IsOnFloor() ...)` → `if (!Movement.IsGrounded ...)` |
| IdleState | `IdleState.cs:41` | `if (... && Player.IsOnFloor())` → `if (... && Movement.IsGrounded)` |
| RunState | `RunState.cs:32` | `if (!Player.IsOnFloor() ...)` → `if (!Movement.IsGrounded ...)` |
| AttackState | `AttackState.cs:119` | `if (Player.IsOnFloor())` → `if (Movement.IsGrounded)` |
| DashState | `DashState.cs:47` | `if (Player.IsOnFloor())` → `if (Movement.IsGrounded)` |
| HitReactionState | `HitReactionState.cs:54` | `if (Player.IsOnFloor())` → `if (Movement.IsGrounded)` |

Also in `PlayerController`, all `IsOnFloor()` calls should use `_movementComponent.IsGrounded` instead (lines 493, 528, 570, 584, 1203):

**The `Movement.IsGrounded` property** reads `Movement.State.IsGrounded`, which is set by the server's `SimulateTick` each frame. This is the authoritative grounded state.

## HurtboxCapsule (Local-Space Offsets)

Hurtboxes are no longer computed from `Skeleton3D.GetBoneGlobalPose()`. Instead, they're defined as **fixed offsets in the character's local space** in `CharacterDefinition.HurtboxCapsules`.

### Definition

```csharp
// Shared/HurtboxCapsule.cs (pure C#)
public struct HurtboxCapsule
{
    public float Sx, Sy, Sz;   // capsule start (local space, meters)
    public float Ex, Ey, Ez;   // capsule end
    public float Radius;
}
```

### CharacterDefinition Integration

```csharp
public HurtboxCapsule[] HurtboxCapsules;

// Example (Manki — 6 capsules covering body volume, start ≠ end):
HurtboxCapsules = new HurtboxCapsule[]
{
    // Torso: hips → upper chest
    new(0f, 0.2f, 0f, 0f, 1.0f, 0f, 0.35f),
    // Head: sphere (start == end, degenerate capsule)
    new(0f, 1.4f, 0f, 0f, 1.4f, 0f, 0.25f),
    // Right arm: shoulder → hand
    new(0.35f, 0.9f, 0f, 0.7f, 0.7f, 0.2f, 0.14f),
    // Left arm: shoulder → hand
    new(-0.35f, 0.9f, 0f, -0.7f, 0.7f, 0.2f, 0.14f),
    // Right leg: hip → foot
    new(0.15f, 0f, 0f, 0.15f, -0.9f, 0f, 0.18f),
    // Left leg: hip → foot
    new(-0.15f, 0f, 0f, -0.15f, -0.9f, 0f, 0.18f),
};
```

### World-Space Computation

The server computes world-space positions from `CharacterState` + definitions each tick:

```csharp
float cos = MathF.Cos(state.FacingYaw);
float sin = MathF.Sin(state.FacingYaw);

foreach (var cap in def.HurtboxCapsules)
{
    float sx = state.PX + cap.Sx * cos - cap.Sz * sin;
    float sy = state.PY + cap.Sy;
    float sz = state.PZ + cap.Sx * sin + cap.Sz * cos;
    // ...same for end points...
    entityList.Add(new EntityData { PosX = sx, PosY = sy, PosZ = sz, ... });
}
```

### ✅ FIXED — Attack, Dash, Jump via HitboxEvent (June 2026)

The preservation hacks for `AnimLockTicks`, `DashDurationTicks`, and `ActionState` in `ApplyServerState` have been **removed** (June 2026). `SimulateTick` now processes these actions directly from `InputState` using the **HitboxEvent** system.

**HitboxEvent replaces StartupTicks + SelfLockTicks.** Each `AttackStage` has:
- `DurationTicks`: total animation lock duration
- `HitboxEvent[]`: hitboxes that spawn at precise `TriggerTick`
- `ChainWindowTicks`: frames to buffer next input for combo

```csharp
// Shared/AttackData.cs
public struct HitboxEvent
{
    public ushort TriggerTick;     // frame from attack start
    public ushort DurationTicks;   // how long the hitbox stays
    public float Radius;
    public float OffX, OffY, OffZ; // offset from attacker center (rotated by yaw)
    public float Damage;
    public float KnockbackForce, KnockbackUpward;
    public ushort StunTicks;
    public bool Interruptible;     // clear on hitstun (if false: projectile persists)
}

public struct AttackStage
{
    public ushort DurationTicks;           // total lock duration
    public HitboxEvent[] HitboxEvents;     // events during this stage
    public float LungeForce;               // forward burst during attack (kept for movement)
    public ushort ChainWindowTicks;        // 0 = no combo
    public float AttackRange, WarpRange;
    public bool UseTargetLock, RotateTowardTarget;
    public float TrackingStrength;
}
```

**⭐ Critical Pitfall — `_hasPendingResolve` / `SetPendingResolve` was REMOVED (June 2026).**  
This was a legacy client-side mechanism from before the sim handled hitbox events. The sim now manages all attack timing via `AnimLockTicks` (set to `DurationTicks`) and `HitboxEvent.TriggerTick`. The client no longer uses `_hasPendingResolve` — it only reacts to `State == ActionState.Attacking` to transition the FSM.

All removed: `SetPendingResolve()`, `TickStartup()`, `ClearPendingResolve()`, `_hasPendingResolve`, `_pendingStages`, `_startupTicksRemaining` from `AttackState`. `AttackState` is now ~40 lines.

See `docs/hitbox-system.md` for the hitbox architecture (revised June 2026).

See also `docs/attack-hitbox-system.md` for the French attack system design doc (historical — same concepts, French prose).

**Attack lifecycle** (sim is authority):\n\n1. `_pendingSlotPress` → `BuildInputState` sets `input.ActiveSlot` (1=LMB, 2=RMB, ...6=F)\n2. If `input.ActiveSlot > 0 && State == Idle` → `State = Attacking`, `AnimLockTicks = DurationTicks`, `AttackElapsedTicks = 0`, `AttackSlot = ActiveSlot`\n3. Each tick: `TickTimers` decrements `AnimLockTicks`, increments `AttackElapsedTicks`\n4. When `AttackElapsedTicks == HitboxEvent.TriggerTick`: sim spawns hitbox via `SpellResolver.Spawn()`\n5. When `AnimLockTicks == 0`: `ProcessAttack` checks `BufferedSlot` and `input.ActiveSlot` (passed by **ref** so it can clear it and prevent re-buffering). If either matches `AttackSlot` and a next stage exists → auto-advance. Otherwise → `State = Idle`, full reset.\n6. **Input buffering**: any `ActiveSlot` input arriving during a lock is checked:\n   - Same slot during ongoing attack → ALWAYS buffered in `BufferedSlot`\n   - Other slot, within `InputBufferWindow` (6 ticks) of unlock → buffered\n   - Outside window or cooldown active → ignored\n   - `BufferedSlot` is consumed by `ProcessAttack` (auto-combo) or by step 4.5 (general buffer consumption when lock expires into Idle)\n7. On hitstun: remaining hitbox events don't fire. `BufferedSlot` is NOT cleared (player can buffer during hitstun).

**The client's FSM reacts** to the sim's `ActionState` (no longer handles attack input directly):
```csharp
// PlayerController._PhysicsProcess (reacts to sim, cleaned up after ability refactor):
var simState = _movementComponent.State.State;
if (simState == ActionState.Attacking && !_fsm.IsInState("attack"))
{
    byte slot = _movementComponent.State.AttackSlot;
    var ability = _charDef.GetSlotAbility(slot > 0 ? slot - 1 : 0, !_movementComponent.IsGrounded);
    string animName = ability.GetAnimationName(_movementComponent.State.ComboStage);
    var attackState = _fsm.GetAttackState();
    if (attackState != null)
    {
        attackState.NextAnimName = animName;
        _fsm.TransitionTo("attack");
    }
    // SpecialEffectKeys now fire from Ability.Tick() via TriggerEffects(), not here.
}
```
{
    dashState.SetDirection(_moveDirection.X, _moveDirection.Z);
    _fsm.TransitionTo("dash");
}
```

**AttackState is now ~40 lines** — no `SetPendingResolve`, `TickStartup`, `ClearPendingResolve`, or `_hasPendingResolve`. Just checks `Movement.State.AnimLockTicks > 0` to block transitions.

**Combo chaining** (all in sim):
- General input buffer (`BufferedSlot`, `InputBufferWindow=6`): any `ActiveSlot` input arriving within 6 frames of unlock (`AnimLockTicks` or `HitstunTicks`) is buffered. Same-slot input during an active attack is ALWAYS buffered (no 6-frame limit for combo chains).
- `ProcessAttack` checks `BufferedSlot == AttackSlot && next stage exists` → consumes buffer and auto-advances.
- `StartAttackFromSlot()` handles both fresh attacks (new slot) and combo chains (same slot).
- No `ComboTimerTicks`, no `BufferedChain` on the client — all combo timing is in the sim via the general `BufferedSlot` system.
- Last stage: full reset (`AttackSlot=0`, `ComboStage=0`, `AttackElapsedTicks=0`)

**Client hitbox spawning is removed** — the sim spawns ALL hitboxes via HitboxEvent for LMB, RMB, Q, E, R, F. `AbilityRegistry` is still used for client-side VFX/special effects (particles, sounds) via `TriggerEffects()`.

**Dash lifecycle** (all handled by sim):
1. `input.Dash` + cooldown check → `StartDash` → `State = Dashing`, `DashDurationTicks = stats.DashCooldownTicks`, `VX = DashSpeed`, `InvincibilityTicks = 60`
2. Each tick: `ProcessDash` handles movement, `TickTimers` decrements duration
3. When `DashDurationTicks == 0`: state timer transitions to `Idle` via `StateTicks`

**Jump lifecycle** (all handled by sim):
1. `input.Jump` + `IsGrounded` → `ApplyJump` → `VY = JumpForce`, `JumpsLeft--`, `IsGrounded = false`
2. Gravity resumes on the next tick

**⭐ Critical Pitfall — LMB Attack Stuck (FSM `_hasPendingResolve` never cleared):**
### REMOVED — SetPendingResolve / _hasPendingResolve (June 2026)

These were fully removed from AttackState. The sim manages all attack timing.

### REMOVED — ClearPendingResolve workaround (June 2026)

AttackState no longer uses these. See the attack lifecycle above.
Without this fix, the attack anim plays but `AttackState.OnProcess` never transitions back to idle/run.

**ApplyServerState is now clean — NO preservation hacks:**
```csharp
public void ApplyServerState(CharacterState state)
{
    _movementComponent.State = state;
    GlobalPosition = new Vector3(state.PX, state.PY, state.PZ);
    Velocity = new Vector3(state.VX, state.VY, state.VZ);
    GlobalRotation = new Vector3(0f, state.FacingYaw, 0f);
    MoveAndSlide();
}
```

The sim is the authority on **everything**. The client just renders. `_movementComponent.Tick()` is disabled.

**Important implication:** `ActionState` enum has NO `Running` value, only: `Idle`, `Dashing`, `Hitstun`, `Sliding`, `Attacking`, `AirDodging`. Movement is handled by `ProcessNormalMovement` under `Idle` state. Don't try to use `ActionState.Running` — check `VX*VX + VZ*VZ > threshold` for running detection.

### Attack System References

See `docs/hitbox-system.md` for the hitbox architecture (HitboxEvent, sim-authoritative spawning, SpellResolver collision engine).

See `docs/attack-hitbox-system.md` for the attack system design (English — HitboxEvent struct, Manki stages example, sim tick processing).

See `docs/netcode-architecture.md` for the netcode architecture (tick/rollback, packet protocol, client-server flow).

The server loads the GLB skeleton and samples animations to position bone-attached hurtboxes precisely.

**Loading** (in `TrainingMatch.Start` / `PvPMatch.Start`):
```csharp
byte[]? glbData = LoadGlbBytes("res://assets/characters/manki/manki.glb");
Bridge.RegisterEntity(1, playerDef, playerState, glbData);
```

**Helper** (in match classes):
```csharp
private static byte[]? LoadGlbBytes(string resPath)
{
    using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
    return file?.GetBuffer((long)file.GetLength());
}
```

**Per-tick sampling** (in `ServerSimulation.Tick`):
```csharp
// Animation name from state
string targetAnim = state.State switch { ... };

// Track transitions — reset time on anim change
var anState = _animState[id];
if (anState.anim != targetAnim) anState = (targetAnim, 0);
anState.time += Simulation.TickDt;
_animState[id] = anState;

// Sample and compute
int animIdx = FindAnimIndex(skel, anState.anim);
if (animIdx >= 0)
{
    skel.SampleAnimation(animIdx, anState.time);
    skel.ComputeWorldTransforms();
}
```

Per-entity animation state:
```csharp
private readonly Dictionary<ulong, (string anim, float time)> _animState = new();
```

### Why This Matters

- **Server-friendly:** No skeleton, no bone names, no Godot — just pure math with position + yaw
- **Deterministic:** Same inputs + same defs = same hit detection, always
- **Cheap:** 6 capsules × N entities, computed once per tick
- **Client can still use bone capsules for visual:** `BoneHurtboxSetup` still runs for debug visualization, but the server ignores it

### Removal of `LocalSimulation.Entities` (Old API)

The old `_simulation.Entities` dictionary (which stored `List<(Vector3, Vector3, float)>` from `GetBoneGlobalPose`) has been **removed**. `LocalSimulation` was replaced by `LocalServerBridge`. The server computes hurtbox world positions from `CharacterState` + `CharacterDefinition.HurtboxCapsules` each tick — no skeleton, no bone names, no Godot dependency.

Callers that previously did `_simulation.Entities[entityId] = GetHurtboxShapes()` now call `simulation.UpdateEntityState(id, x, y, z, yaw, def)` (old API) or rely on the bridge's internal state computation (new API).

### Auto-Calculating Model Y-Offset

See `references/model-y-offset-auto-calc.md` for how the GLB model Y-offset is automatically computed from skeleton foot bone positions, eliminating hardcoded per-character offsets.

### ServerSkeleton — Pure C# GLB Parser

`Shared/ServerSkeleton.cs` loads a GLB file and computes bone world transforms **without Godot**. Uses `System.Text.Json` (available in .NET 6+). This enables the server to have bone-accurate hurtboxes that follow animations.

```csharp
// Load from raw GLB bytes (parses JSON + BIN chunks):
var skel = ServerSkeleton.LoadFromGlb(glbBytes);

// Sample an animation at a given time (SLERP for quaternions):
skel.SampleAnimation(animIndex, timeSeconds);
// Compute hierarchical bone transforms (parent → child):
skel.ComputeWorldTransforms();
// Get a bone's world position:
skel.GetBoneWorldPosition("mixamorig:RightHand", out float x, out float y, out float z);
```

#### Internal Structure

The skeleton stores:
- `GlbNode[]` — bone hierarchy (name, parent, children, rest pose translation/rotation/scale)
- `AnimationData[]` — named animations with per-bone keyframe tracks (translation, rotation, scale)
- Per-frame caches: `_localPos`, `_localRot`, `_localScale`, `_worldPos`, `_worldRot`

#### GLB Parsing

```csharp
public static ServerSkeleton LoadFromGlb(byte[] glbData)
```

Reads the GLB binary format:
1. Header (magic 0x46546C67, version 2)
2. JSON chunk (nodes, skins, animations, accessors, bufferViews)
3. BIN chunk (raw keyframe data as float arrays)

Parses:
- **Nodes**: names, parent/children hierarchy, rest transforms (translation/rotation/scale)
- **Parent construction**: from children arrays — each child's parent is set by scanning all nodes and matching children lists
- **Root bone**: from the skin's `skeleton` or first `joints` entry
- **Animations**: per-channel keyframes from accessors (input = time, output = value), supports VEC3/VEC4/SCALAR types
- **ByteStride handling**: accounts for interleaved buffer views

#### Animation Sampling

```csharp
public void SampleAnimation(int animIndex, float timeSec)
```

1. **Reset all bones to rest pose** — each frame starts from the bind pose
2. **Find the two keyframes** bracketing the current time
3. **Interpolate** between them based on the fraction:
   - `translation` / `scale`: linear interpolation (Lerp)
   - `rotation`: spherical linear interpolation (SLERP) with dot product sign correction

#### World Transform Computation

```csharp
public void ComputeWorldTransforms()
```

Top-down hierarchical computation:
- **Root**: world = local
- **Children**: world_pos = parent_world_pos + rotate(parent_world_rot, child_local_pos)
  - world_rot = quaternion_multiply(parent_world_rot, child_local_rot)
- Uses the `RotateVector3` helper which applies a quaternion rotation to a 3D vector

#### Bone Lookup

```csharp
public bool GetBoneWorldPosition(string name, out float px, out float py, out float pz)
```

Matches by exact name OR by suffix after `":"` (e.g., "mixamorig:RightHand" matches "RightHand"). Returns the computed world position after `ComputeWorldTransforms()`.

**Init time only** — load once per entity, then `SampleAnimation` + `ComputeWorldTransforms` each tick.

#### Getting GLB Bytes in Godot

```csharp
private static byte[]? LoadGlbBytes(string resPath)
{
    using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
    if (file == null) return null;
    return file.GetBuffer((long)file.GetLength());
}
```

### Animation Sampling on Server (ActionState → Animation)

In `ServerSimulation.Tick()`, for each entity with a skeleton, the server:

1. **Maps ActionState + movement velocity to animation name**:
   ```csharp
   string targetAnim;
   if (state.State == ActionState.Dashing)
       targetAnim = "dash";
   else if (state.State == ActionState.Attacking)
       targetAnim = "melee";       // simplified
   else if (state.State == ActionState.Hitstun)
       targetAnim = "small_hit";   // simplified
   else if (!state.IsGrounded)
       targetAnim = state.VY > 0 ? "jump" : "fall";
   else if (state.VX * state.VX + state.VZ * state.VZ > 1f)
       targetAnim = "run";
   else
       targetAnim = "idle";
   ```

2. **Tracks animation transitions** — if the animation changes, reset time to 0
3. **Advances animation time** by `TickDt` (1/60s) each tick
4. **Looks up the animation index** via `FindAnimIndex(skel, animName)`
5. **Samples the skeleton** and computes world transforms:
   ```csharp
   skel.SampleAnimation(animIdx, anState.time);
   skel.ComputeWorldTransforms();
   ```

Animation transition state is per-entity:
```csharp
private readonly Dictionary<ulong, (string anim, float time)> _animState;
```

**Known limitation**: The mapping is simplified. For complex scenarios (specific attack animations like "jab" vs "rmb_attack", or hitstun intensity), the mapping needs to consider ability data and knockback magnitude.

**Future improvement**: Store the current animation name + time in `CharacterState` so the server and client stay synchronized, or derive it from the FSM's state + animation position.

### Bone-Attached Hurtboxes (HurtboxBoneDef)

`Shared/HurtboxBoneDef.cs` defines a sphere hurtbox attached to a specific skeleton bone:

```csharp
public struct HurtboxBoneDef
{
    public string BoneName;  // e.g. "mixamorig:Spine2"
    public float OffX, OffY, OffZ;  // local-space offset from bone
    public float Radius;
}
```

Replace fixed `HurtboxCapsules` with `HurtboxBoneDefs` in `CharacterDefinition`:

```csharp
HurtboxBoneDefs = new HurtboxBoneDef[]
{
    new("mixamorig:Head", 0, 0, 0, 0.25f),
    new("mixamorig:Spine2", 0, 0, 0, 0.3f),
    new("mixamorig:Hips", 0, 0, 0, 0.3f),
    new("mixamorig:RightHand", 0, 0, 0, 0.14f),
    new("mixamorig:LeftHand", 0, 0, 0, 0.14f),
    new("mixamorig:RightFoot", 0, 0, 0, 0.18f),
    new("mixamorig:LeftFoot", 0, 0, 0, 0.18f),
};
```

#### ServerSimulation Integration

When a skeleton is available (falls back to fixed `HurtboxCapsules` otherwise):

```csharp
if (_skeletons.TryGetValue(id, out var skel) && def.HurtboxBoneDefs?.Length > 0)
{
    skel.ComputeWorldTransforms();
    foreach (var hbd in def.HurtboxBoneDefs)
    {
        if (!skel.GetBoneWorldPosition(hbd.BoneName, out float bx, out float by, out float bz))
            continue;
        // Bone → world: rotate by FacingYaw, add character position, add local offset
        float wx = px + (bx * cos - bz * sin) + hbd.OffX;
        float wy = py + by + hbd.OffY;
        float wz = pz + (bx * sin + bz * cos) + hbd.OffZ;
        entityList.Add(new SpellResolver.EntityData
        {
            Id = id,
            PosX = wx, PosY = wy, PosZ = wz,
            Radius = hbd.Radius,
            Shape = HitboxShape.Sphere,
            Active = true,
        });
    }
}
```

#### Registration Flow (Match → Bridge → Server)

```csharp
// Match class (TrainingMatch.Start / PvPMatch.Start):
byte[]? glbData = LoadGlbBytes("res://assets/characters/manki/manki.glb");
Bridge.RegisterEntity(1, playerDef, playerState, glbData);

// LocalServerBridge.RegisterEntity:
ServerSkeleton? skel = null;
if (glbData != null)
{
    try { skel = ServerSkeleton.LoadFromGlb(glbData); }
    catch (Exception ex) { GD.PrintErr($"[Bridge] Failed to load skeleton: {ex.Message}"); }
}
_server.RegisterEntity(id, def, initialState, skel);

// ServerSimulation.RegisterEntity:
_defs[id] = def;
_states[id] = initialState;
if (skeleton != null) _skeletons[id] = skeleton;
_animState[id] = ("idle", 0);
```

#### GLB File Info for Manki

- 27 nodes (skeleton: 25 mixamorig bones + Armature + retopo_target mesh)
- 19 animations: idle, run, dash, jump, fall, land, jab, melee, flying_kick, kick, leg_sweep, small_hit, medium_hit, hard_hit, backflip, death, rmb_attack, rmb_loop, throw
- 1 skin with 25 joints (all mixamorig: bones)
- Armature node may have a 90° X rotation (Blender → glTF axis conversion) — the `ServerSkeleton` uses the node's REST POSE transforms directly, which includes this rotation baked in

### Per-Arena FloorHeight

`ArenaDefinition.FloorHeight` replaces the hardcoded `Simulation.FloorHeight = 0f` constant. Each arena defines its own floor surface Y in `Shared/ArenaDefinition.cs`:

```csharp
public float FloorHeight;  // top surface Y of the floor collision
```

Values:
- The Split: `FloorHeight = -1f`
- The Pit: `FloorHeight = -1f`
- Crossroads: `FloorHeight = -1f`

To find the correct value, check the arena's `.tscn` — look for `CSGBox3D` nodes with `use_collision = true`. The top surface Y = `transform.position_Y + (size_Y / 2)`.

### ⭐ Workflow Rule — Explain Before Editing (Reinforced)

This session (June 12 2026) produced the strongest signal yet: **explain BEFORE every code change**. When the user says "je préfère que tu m'expliques un peu plus ce que tu changes" or "encore une fois, explique moi chaque changement que tu fais" — stop, describe the plan, wait for "vas y". This applies even mid-sprint: if the task shifts from coding to design, pause and discuss.

See also `docs/attack-hitbox-system.md` and `docs/netcode-architecture.md` for the latest architecture docs.

## Hitbox Pipeline (Sim-Authoritative — June 2026)

Hitboxes are now spawned by the **sim** via `HitboxEvent`, not by client code. The old `ResolveAbilityStages` and client-side `SpellResolver.Spawn()` calls have been **removed**. `AbilityRegistry` is still used for client-side VFX/special effects (particles, sounds) via `SpecialEffectKeys` and `TriggerEffects()`.

### Sim-Only Spawning (LMB and all ActiveSlot abilities)

Each `AttackStage` carries `HitboxEvent[]`. When `ServerSimulation.Tick()` runs:

```csharp
for each entity in Attacking state:
    var ability = state.AttackSlot switch { 1=>LMB, 2=>RMB, ... };
    var stage = ability.Stages[state.ComboStage];
    for each HitboxEvent evt in stage.HitboxEvents:
        if state.AttackElapsedTicks == evt.TriggerTick:
            float hx = px + evt.OffX*cos - evt.OffZ*sin;
            float hy = py + evt.OffY;
            float hz = pz + evt.OffX*sin + evt.OffZ*cos;
            SpellResolver.Spawn(new Hitbox {
                X=hx, Y=hy, Z=hz, Radius=evt.Radius,
                Damage=evt.Damage, DurationTicks=evt.DurationTicks, ...
            });
```

Hitbox position is computed as: `entityPos + rotate(OffX, OffY, OffZ) by facing yaw`.

### SpellResolver.Tick() — Collision Engine

Runs after hitbox spawning in the same `ServerSimulation.Tick()`:

```csharp
var hits = SpellResolver.Tick(entityList);
foreach (var hit in hits)
    ApplyKnockback(hit);  // sim applies to target CharacterState
```

HitResult struct:
```csharp
public struct HitResult
{
    public ulong TargetEntityId;
    public ulong OwnerEntityId;  // from hb.OwnerId
    public float Damage;
    public float KnockbackX, KnockbackY, KnockbackZ;
    public ushort StunTicks;
}
```

### Client-Side Effects (SpecialEffectKeys)

`AbilityRegistry.SpecialEffectKeys` trigger client-side VFX (particles, sounds) —
NOT hitbox spawning. All hitboxes are spawned sim-side via `HitboxEvent` in
`ServerSimulation.Tick()`. Special effects fire from `Ability.Tick()` via
`TriggerEffects(player)` before the ability sets `ActiveSlot` and deactivates.
No more effects triggered from `PlayerController._PhysicsProcess` on FSM transition.

### Hurtboxes (Bone Capsules)

`BoneHurtboxSetup` creates 6 capsule hurtboxes from skeleton bones (head, torso, 2 arms, 2 legs). Updated every frame via `GetWorldCapsules()`:

```csharp
Vector3 BoneWorldPos(Skeleton3D skel, int boneIdx)
{
    return skel.GlobalTransform * skel.GetBoneGlobalPose(boneIdx).Origin;
}
```

## Bone Attachment Pattern (Per-Character Props)

**Do NOT** put character-specific bone-attached meshes in `PlayerController`. Extract them to a dedicated component:

```
Scripts/Characters/Manki/MankiWeaponAttach.cs
```

The component:
1. Receives the `Skeleton3D` reference via `Setup(skeleton)`
2. Finds the target bone(s) by name
3. Creates mesh children under a container `Node3D`
4. Updates `GlobalPosition` from `GetBoneGlobalPose` each frame in its own `_Process`
5. Manages visibility based on FSM state (show during charge/attack, hide otherwise)

```csharp
public partial class MankiWeaponAttach : Node
{
    private Skeleton3D? _skeleton;
    private int _handBoneIdx = -1;
    private Node3D? _handPropsNode;
    private StateMachine? _fsm;

    public void Setup(Skeleton3D skeleton)
    {
        _skeleton = skeleton;
        // Find FSM in parent → PlayerModel → FSM
        var parent = GetParentOrNull<Node3D>();
        if (parent != null)
        {
            var model = parent.GetNodeOrNull<Node3D>("PlayerModel");
            if (model != null)
                _fsm = model.GetNodeOrNull<StateMachine>("FSM");
        }
        // Find bone, build meshes...
    }

    public override void _Process(double delta)
    {
        // Show/hide based on FSM state
        bool visible = false;
        if (_fsm != null)
        {
            string state = _fsm.CurrentStateName;
            visible = state == "aimed_charge" || state == "attack";
        }
        _handPropsNode.Visible = visible;

        // Update position only when visible (cheaper)
        if (visible && _skeleton != null)
        {
            Vector3 pos = _skeleton.GlobalTransform
                         * _skeleton.GetBoneGlobalPose(_handBoneIdx).Origin;
            _handPropsNode.GlobalPosition = pos;
        }
    }
}
```

### FSM State Tracking for Visibility

Preferred over timer-based show/hide: props automatically appear when the FSM enters `aimed_charge` or `attack`, and disappear when it leaves. No manual `Show()`/`Hide()` calls needed.

### Multiple Bones

For props on both hands (aerosol in right, lighter in left), use separate `Node3D` containers and bone indices, updated in the same `_Process`:

## Hitbox Scaling with Charge Level

For abilities with `AimedCharge`, hitbox range can scale with charge progress via `CharacterState.ChargeTicks`.

### CharacterState Field

```csharp
// Shared/CharacterState.cs
public ushort ChargeTicks;  // written by AimedChargeState each frame
```

### Writing Charge Progress

```csharp
// AimedChargeState.OnProcess()
ref var state = ref Player.GetState();
state.ChargeTicks = (ushort)_chargeTicks;
```

### Reading in Hitbox Creation

```csharp
// Charge scaling (used by sim's hitbox spawning):
float maxRange = ability.AimedCharge!.Value.ConeRange;
ushort ct = _movementComponent.State.ChargeTicks;
ushort maxCt = ability.AimedCharge!.Value.MaxChargeTicks;
float t = maxCt > 0 ? Math.Clamp((float)ct / maxCt, 0f, 1f) : 0f;
float coneRange = 10f + (t * (maxRange - 10f));  // base 10m → max Range
```

### Exposing CharacterState from PlayerController

```csharp
public ref CharacterState GetState() => ref _movementComponent.State;
```

## Match Types — TrainingMatch and PvPMatch

The match lifecycle is split into two classes:

| File | Role |
|------|------|
| `Scripts/World/Main.cs` | Screen navigation: menus, class select, arena select |
| `Scripts/World/TrainingMatch.cs` | Local training vs NPC dummy — pure `ServerSimulation`, no network |
| `Scripts/World/PvPMatch.cs` | Online PvP — `NetworkClient` UDP + local `ServerSimulation` (prediction + rollback) |

### TrainingMatch Game Loop

```csharp
public override void _PhysicsProcess(double delta)
{
    var input = Player.GetCurrentInput();

    // Sync warp state from MovementComponent to local sim
    var playerState = Player.GetMovementState();
    var simState = _localSim.GetState(1);
    simState.WarpTargetX = playerState.WarpTargetX;
    simState.WarpTargetZ = playerState.WarpTargetZ;
    simState.WarpSpeed = playerState.WarpSpeed;
    simState.WarpAttackRange = playerState.WarpAttackRange;
    _localSim.SetState(1, simState);

    _localSim.Tick(new Dictionary<ulong, InputState> { { 1, input } });

    Player.ApplyServerState(_localSim.GetState(1));
    for (int i = 0; i < NpcCount; i++)
        NPCs[i].ApplyServerState(_localSim.GetState((ulong)(100 + i)));
}
```

### PvPMatch Game Loop — Prediction + Rollback

`_PhysicsProcess`: collect input, buffer, local sim predict, send to server, apply predicted:
```csharp
public override void _PhysicsProcess(double delta)
{
    var input = Player.GetCurrentInput();
    _sendTick++;
    _inputBuffer[_sendTick % RollbackFrames] = input;
    _localSim.Tick(new Dictionary<ulong, InputState> { { PlayerEntityId, input } });
    var predicted = _localSim.GetState(PlayerEntityId);
    _stateBuffer[_sendTick % RollbackFrames] = predicted;
    Net.SendInput(input, _sendTick);
    Player.ApplyServerState(predicted);
}
```

`_Process`: reconcile with server states:
```csharp
public override void _Process(double delta)
{
    var serverStates = Net.ReceiveStates();
    foreach (var kvp in serverStates)
        _serverConfirmedStates[kvp.Key] = kvp.Value.state;

    if (serverStates.TryGetValue(PlayerEntityId, out var server) && server.tick > _lastConfirmedTick)
    {
        _lastConfirmedTick = server.tick;
        int idx = (int)(server.tick % RollbackFrames);
        var predicted = _stateBuffer[idx];
        float distSq = DistanceSquared(predicted, server.state);
        if (distSq > 0.25f)
        {
            // Rollback: re-create sim, re-sim from server state with buffered inputs
            _localSim = new ServerSimulation(_arenaDef);
            _localSim.RegisterEntity(PlayerEntityId, _charDef, server.state, _playerBakedData);
            _localSim.RegisterEntity(OpponentEntityId, _charDef, oppState, _playerBakedData);
            for (uint t = server.tick + 1; t <= _sendTick; t++)
                _localSim.Tick(new Dictionary<ulong, InputState> { { PlayerEntityId, _inputBuffer[t % RollbackFrames] } });
            Player.ApplyServerState(_localSim.GetState(PlayerEntityId));
        }
    }

    // Opponent: server authority
    if (serverStates.TryGetValue(OpponentEntityId, out var oppServer))
        Opponent.ApplyServerState(oppServer.state);
}
```

_public API (both match types):_
```csharp
match.GetTarget()        // ulong
match.HasTarget()        // bool
match.Player             // PlayerController
match.NPCs / Opponent   // PlayerController[]
```

The game loop runs at a fixed 60Hz via `_PhysicsProcess`, not _Process. This matches the server's fixed timestep and prevents jitter from variable-rate _Process updates.

Key change from old LocalSimulation approach: the simulation no longer reads from `_simulation.Entities` (which was manually updated with `GetBoneGlobalPose` positions). Instead, `ServerSimulation.Tick()` computes world-space EntityData from `CharacterState.PX/PY/PZ` + `CharacterDefinition.HurtboxCapsules` offsets + `FacingYaw`. No skeleton access needed.


## Forward Direction Convention

**Critical**: Mixamo models face **+Z** (GLB/glTF convention). Godot uses **-Z** as forward. This mismatch MUST be fixed in the **GLB asset** (rotate model 180° in Blender, rebake animations), not in code.

### Symptoms of the Mismatch
- `GetPlayerForward()` returns `-Transform.Basis.Z` = -Z (Godot convention)
- Model visually faces +Z (Mixamo convention)
- Hitboxes, VFX, and attack directions are 180° off from visual model direction
- Temporary workaround: negate `hitDir` in hitbox spawning and `GetOwnerForward()` in ability effects

### Temporary Code Fix (until GLB is fixed)
```csharp
// TEMP: negate direction — GLB model faces +Z (Mixamo), not Godot -Z
Vector3 tipPos = handPos + (-hitDir * coneRange);

// In MankiAbilities:
Vector3 forward = -combat.GetOwnerForward();
```

When GLB is fixed, remove the negation and revert to `hitDir` / `GetOwnerForward()`.
