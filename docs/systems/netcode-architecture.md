# SlopArena Netcode & Simulation Architecture

## 1. Philosophy

**Server-authoritative with client-side prediction.** Le serveur est l'autorité absolue sur le state du jeu. Le client simule localement pour éviter la latence, et se corrige quand le serveur répond.

Même archi que Rivals 2, GGST, SF6.

---

## 2. Components

```
┌──────────────────────────────────────────────────────────────────────┐
│                        MASTER SERVER (separate repo)                  │
│           ASP.NET Core + SignalR + PostgreSQL                         │
│                                                                       │
│  Server Browser │ Lobby Hub │ Char Select │ Match Start │ Results    │
│       │              │            │            │            │         │
└───────┼──────────────┼────────────┼────────────┼────────────┼────────┘
        │ /servers      │ SignalR    │            │ POST       │
        │               │ pushes     │            │ /match/start│
        ▼               ▼            ▼            ▼            ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    GAME SERVER (src/Server, .NET console)              │
│                                                                       │
│  ┌─────────────────────┐    ┌──────────────────────────────────────┐ │
│  │ MatchControlServer  │    │ MultiMatchOrchestrator                │ │
│  │ TCP :base_port      │───►│ port allocation: base → base+max-1   │ │
│  │ POST /match/start   │    │ tracks active MatchInstances          │ │
│  └─────────────────────┘    └───────────┬──────────────────────────┘ │
│                                         │ spawns                      │
│                    ┌────────────────────┼────────────────────┐       │
│                    ▼                    ▼                     ▼       │
│              ┌──────────┐        ┌──────────┐          ┌──────────┐  │
│              │ Match #1 │        │ Match #2 │   ...    │ Match #N │  │
│              │ UDP      │        │ UDP      │          │ UDP      │  │
│              │ :base+0  │        │ :base+1  │          │ :base+N  │  │
│              │ thread   │        │ thread   │          │ thread   │  │
│              └────┬─────┘        └────┬─────┘          └────┬─────┘  │
└───────────────────┼───────────────────┼──────────────────────┼──────┘
                    │ UDP               │ UDP                  │ UDP
                    ▼                   ▼                      ▼
┌──────────────────────────────────────────────────────────────────────┐
│                         UNITY CLIENTS                                 │
│                                                                       │
│  ┌──────────┐  ┌──────────────┐  ┌─────────────┐  ┌───────────────┐  │
│  │ Input    │  │ LocalSim     │  │ NetworkClient│  │ LobbyClient   │  │
│  │ (WASD)   │─►│ (predict)    │─►│ (UDP)       │  │ (SignalR)     │  │
│  └──────────┘  └──────┬───────┘  └──────┬──────┘  └───────┬───────┘  │
│                       │                 │                 │          │
│                 ┌─────▼─────┐    ┌──────▼──────┐          │          │
│                 │  RENDER   │◄───┤ State Buffer │          │          │
│                 │ (Unity)   │    │ [t-29..t]   │          │          │
│                 └──────────┘    │ 30-frame ring│          │          │
│                                 │ rollback if  │          │          │
│                                 │ 3D dist >0.5m│          │          │
│                                 └─────────────┘           │          │
│                                                           │          │
│                 Lobby/meta via SignalR ◄──────────────────┘          │
│                 Match sim via UDP ◄──────────────────────────────────┤
└──────────────────────────────────────────────────────────────────────┘
```

---

## 3. Data Flow

### 3a. Client _PhysicsProcess (60Hz) — Predict + Send

```
_PhysicsProcess():

  1. Player.GetCurrentInput()
     → BuildInputState()
       → lit clavier/souris + _pendingSlotPress
       → InputState { MoveX, MoveY, flags, ActiveSlot }

  2. Increment _sendTick
     → _sendTick++ (monotonically increasing per frame)

  3. Store input in 30-frame ring buffer
     → _inputBuffer[_sendTick % RollbackFrames] = input

  4. LocalSimulation.Tick(input)
     → ServerSimulation.Tick() — même code que le serveur
     → prédit la position/vitesse/action du tick suivant

  5. Store predicted state in ring buffer
     → _stateBuffer[_sendTick % RollbackFrames] = predicted

  6. Send input + tick to server (UDP, non-bloquant)
     → Net.SendInput(input, _sendTick)
     → Packet: entityId(8) + tick(4) + InputState(19) = 31B

  7. Render predicted state
     → PlayerRenderer.ApplyServerState(predicted)
     → FixedUpdate réagit aux changements d'ActionState
       (FSM transitions: Idle → Dashing, Idle → Attacking, etc.)
```

### 3b. Client Update — Reconcile with Server

```
Update(delta):

  1. Receive server states (non-bloquant)
     → Net.ReceiveStates()
     → Returns: Dictionary<entityId, (tick, CharacterState)>
     → Packet per entity: entityId(8) + tick(4) + CharacterStatePacket(48) = 60B

  2. For player's server state:
     a. Find predicted state for same tick
        → int idx = serverTick % RollbackFrames
        → CharacterState predicted = _stateBuffer[idx]

     b. Compare server vs predicted (3D distance)
        → float dx = predicted.PX - serverState.PX
        → float dy = predicted.PY - serverState.PY
        → float dz = predicted.PZ - serverState.PZ
        → float distSq = dx*dx + dy*dy + dz*dz
        → If distSq > 0.25f (0.5m threshold):
            → ROLLBACK triggered

     c. Rollback procedure:
        i.   Snapshot NPC states before replacing sim
        ii.  Reset local sim to server's confirmed state
             → new ServerSimulation(arena)
             → RegisterEntity with serverState as initial state
        iii. Re-register NPCs (prefer server-confirmed, fallback to snapshot)
        iv.  Re-simulate from serverTick+1 to currentTick
             → for tick t = serverTick+1 .. _sendTick:
                 pastInput = _inputBuffer[t % RollbackFrames]
                 _localSim.Tick({ playerEntityId, pastInput })
        v.   Apply corrected state
             → PlayerRenderer.ApplyServerState(corrected)
             → Update _stateBuffer[currentTick % RollbackFrames]

  3. For NPC server states:
     → Apply server state directly (always authoritative, no prediction for now)

  4. Update visuals (target ring follow, UI)
```

### 3c. MatchInstance Tick Loop (60Hz, per match, own thread)

```
Tick():

  1. ReceiveInputs() — drain UDP socket, match by entityId
     → Queue inputs per PlayerSlot

  2. Check timeout (5s silence → match stops)

  3. Flush input queues (take last valid packet per slot)
     → _serverTick = max(_serverTick, latestClientTick)

  4. ServerSimulation.Tick(inputs)
     → SimulateTick: mouvement, gravité, sol, combat, tout
     → Spawn hitboxes from attack events (HitboxEvent.TriggerTick)
     → SpellResolver.Tick: hitbox vs hurtbox collision, damage, knockback, hitstun

  5. Check deaths (first to MaxDeaths=3 loses → MatchState.Ended)

  6. SendState() — broadcast to all connected clients
     → For each client:
       → For each entity (all rostered players):
       → Packet: entityId(8) + tick(4) + CharacterStatePacket(48) = 60B
         → tick = _serverTick (echoed back)
       → Client filters by entityId
```

---

## 4. Packet Protocol

### 4a. Client → Server

```
Send packet: entityId(8) + tick(4) + InputState(19) = 31 bytes

[0..7]   entityId        (ulong)
[8..11]  tick            (uint)       ← local client frame counter
[12..30] InputState (19 bytes)
```

**InputState layout (19 bytes):**
| Offset | Type    | Field           | Notes                              |
|--------|---------|-----------------|------------------------------------|
| 0-3    | float   | MoveX           | Horizontal analog input            |
| 4-7    | float   | MoveY           | Vertical analog input              |
| 8      | byte    | flags           | bit0:Up, 1:Down, 2:Left, 3:Right, 4:Jump, 5:Dash, 6:Crouch, 7:IsAiming |
| 9      | byte    | ActiveSlot      | 0=none, 1=LMB, 2=RMB, 3=Q, 4=E, 5=R, 6=F |
| 10-11  | short   | FacingYaw       | Degrees × 100 (movement-facing)    |
| 12-13  | short   | AimYaw          | Degrees × 100 (combat-facing, reserved) |
| 14-15  | short   | AimPitch        | Degrees × 100 (camera vertical aim) |
| 16-17  | ushort  | AimDistance     | cm (0-6500 = 0-65m)                |
| 18     | byte    | TargetEntityId  | Client-selected target (0 = none)  |

Total: 31 bytes (8 + 4 + 19)

### 4b. Server → Client (per entity)

```
Receive packet per entity: entityId(8) + tick(4) + CharacterStatePacket(48) = 60 bytes

[0..7]   entityId          (ulong)
[8..11]  tick              (uint)       ← echoes client's tick number
[12..59] CharacterStatePacket (48 bytes)
```

**CharacterStatePacket layout (48 bytes):**
| Offset | Type    | Field               | Notes                              |
|--------|---------|---------------------|------------------------------------|
| 0-3    | uint    | TickNumber          | Echoed client tick (for matching)  |
| 4-7    | float   | PositionX           | World X                            |
| 8-11   | float   | PositionY           | World Y (up)                       |
| 12-15  | float   | PositionZ           | World Z (forward)                  |
| 16-19  | float   | VelocityX           | World velocity X                   |
| 20-23  | float   | VelocityY           | World velocity Y                   |
| 24-27  | float   | VelocityZ           | World velocity Z                   |
| 28     | byte    | CurrentActionState  | Idle/Dashing/Attacking/Hitstun     |
| 29     | byte    | IsGrounded          | 0 or 1                             |
| 30-31  | ushort  | StateDurationFrames | Remaining ticks in current state   |
| 32     | byte    | AttackSlot          | 0=none, 1-6=LMB/RMB/Q/E/R/F      |
| 33     | byte    | ComboStage          | 0-3 combo chain stage              |
| 34-37  | float   | FacingYaw           | Server-authoritative facing (radians) |
| 38     | byte    | MatchState          | Match lifecycle (Waiting/Countdown/Playing/Ended) |
| 39     | byte    | AnimIndex           | Animation index into ability's AnimationNames[] |
| 40-41  | ushort  | BuffRemainingTicks  | Buff timer (0 = no active buff)    |
| 42     | byte    | BuffActiveFlags     | BuffType bitfield                   |
| 43     | byte    | HitstunLevel        | 0=small, 1=medium, 2=hard          |
| 44-47  | float   | AimPitch            | Server-authoritative aim pitch (radians) |

Total: 60 bytes per entity (8 + 4 + 48)

**Le serveur envoie TOUS les états à chaque client.** Le client ignore ceux qui ne le concernent pas. Pas de overhead de routing.

**Tick echo:** Le serveur lit le tick de chaque client depuis le buffer d'input et l'écrit dans le(s) paquet(s) de réponse. Le client utilise ce tick pour retrouver l'état prédit correspondant dans son ring buffer.

---

## 5. CharacterState internals (Shared)

`CharacterState` (144 bytes in memory, 48 serialized) is the full per-tick state of one entity:

| Field               | Type    | Notes                                |
|---------------------|---------|--------------------------------------|
| PX, PY, PZ          | float   | World position                       |
| VX, VY, VZ          | float   | Velocity                             |
| State               | enum    | ActionState (Idle, Dashing, etc.)    |
| StateTicks          | ushort  | Remaining ticks in current state     |
| DamagePercent       | ushort  | 0-999, Smash-style                   |
| JumpsLeft           | byte    |                                      |
| AirDodgesLeft       | byte    |                                      |
| IsGrounded          | bool    |                                      |
| DashCooldownTicks   | ushort  |                                      |
| DashDurationTicks   | ushort  | Remaining dash frames                |
| DashDirX, DashDirZ  | float   | Dash direction vector                |
| InvincibilityTicks  | ushort  | Post-respawn/dash invincibility      |
| **AttackSlot**      | **byte**| **Which slot this attack uses (1-6)**|
| **AttackElapsedTicks**|**ushort**| **Frames since attack started**    |
| ComboStage          | byte    | 1-3 for chain combos                 |
| ComboTimerTicks     | ushort  | Chain window remaining               |
| AnimLockTicks       | ushort  | Self-lock from attack                |
| BufferedChain       | byte    | Buffered LMB chains (max 2)          |
| HeavyHoldTicks      | ushort  | RMB hold time                        |
| HeavyCharged        | bool    | Hold threshold reached               |
| ChargeTicks         | ushort  | Aimed charge progress                |
| KVX, KVY, KVZ       | float   | Knockback velocity (decays separate) |
| HitstunTicks        | ushort  | Frames frozen before knockback       |
| DIX, DIY            | float   | Directional influence input          |
| FacingYaw           | float   | Radians, +Z = 0                      |
| Cooldown0-5         | ushort  | Per-slot cooldowns (abilities)       |
| EntityId            | ulong   | 0 = unassigned                       |
| **TargetEntityId**  | **ulong**| **Soft-lock target (0 = none, set server-side per tick)** |
| ...                 |         |                                      |

Position, velocity, action state, grounded flag, state duration, attack slot, combo stage, facing yaw, match state, buff remaining ticks, and buff active flags are serialized. The remaining fields (jumps, dodges, DI, knockback, etc.) are computed locally.

---

## 6. Rollback System

### 6a. Ring Buffers

MatchManager maintains two 10-frame ring buffers:

```csharp
private const int RollbackFrames = 10;
private readonly InputState[] _inputBuffer = new InputState[RollbackFrames];
private readonly CharacterState[] _stateBuffer = new CharacterState[RollbackFrames];
```

- `_inputBuffer[t % 10]` — every input sent to the server
- `_stateBuffer[t % 10]` — every predicted state from local sim
- 10 frames = ~167ms of buffer, enough to cover network jitter on localhost and realistic LAN latency

### 6b. Rollback Trigger

When a server state arrives for the player:

```csharp
int idx = (int)(serverTick % RollbackFrames);
var predicted = _stateBuffer[idx];
float dy = predicted.PY - serverState.PY;
if (MathF.Abs(dy) > 0.01f)
{
    // ROLLBACK
}
```

Threshold is 0.01m (1cm) on Y axis — if the vertical position differs by more than a centimeter, the prediction was wrong. The comparison currently checks only PY; this can be extended to a full vector comparison or per-field checks.

### 6c. Rollback Procedure

1. **Reset** — Create a fresh `ServerSimulation`, register the server's confirmed `CharacterState` as the initial state.
2. **Re-simulate** — For each tick from `serverTick + 1` to `currentTick`, feed the corresponding input from `_inputBuffer` into `_localSim.Tick()`.
3. **Apply** — Read the corrected state, update `_stateBuffer[currentTick % RollbackFrames]`, and call `Player.ApplyServerState(corrected)`.

```csharp
// Reset local sim to the server's confirmed state
var safeState = serverState;
_localSim = new ServerSimulation(_arenaDef);
_localSim.RegisterEntity(_playerEntityId, _charDef, safeState);

// Re-simulate from serverTick+1 to currentTick
uint currentTick = _sendTick;
for (uint t = serverTick + 1; t <= currentTick; t++)
{
    var pastInput = _inputBuffer[t % RollbackFrames];
    _localSim.Tick(new Dictionary<ulong, InputState> { { _playerEntityId, pastInput } });
}

// Apply corrected state
var corrected = _localSim.GetState(_playerEntityId);
_stateBuffer[currentTick % RollbackFrames] = corrected;
Player.ApplyServerState(corrected);
```

### 6d. Sur localhost = zéro rollback

Quand le serveur tourne sur localhost :

```
Client envoie input → serveur reçoit immédiatement → tick → renvoie state
                                                      ↓
                                              Le temps d'arrivée est
                                              < 1 frame (<1ms UDP local)
                                              → toujours synchrone
```

Donc la prédiction locale matche TOUJOURS le serveur. L'archi rollback est là pour le jour où le serveur est distant.

---

## 7. ActiveSlot Pipeline

### 7a. Flow

```
1. Player presses LMB (slot 1)
2. PlayerController._UnhandledInput(Key MouseButton.Left)
   → _pendingSlotPress = 1 (stored, consumed next frame)

3. Next _PhysicsProcess:
   Player.BuildInputState()
   → new InputState { Attack = true, ActiveSlot = 1, ... }

4. MatchManager._PhysicsProcess:
   → _localSim.Tick(inputs) — local prediction
   → ServerSimulation sees ActiveSlot=1, Attack=true

5. ServerSimulation.Tick → Simulation.SimulateTick()
   → Edge-detect Attack flag (prevAttack[entity] vs current)
   → On rising edge: resolve ability from slot
     → slot 1 = def.LMB → ability definition
     → state.AttackSlot = 1, state.State = Attacking
     → state.AttackElapsedTicks = 0
   → On subsequent ticks (state is Attacking):
     → state.AttackElapsedTicks++
     → Check stage timings, chain windows, anim locks

6. Hitbox spawning (post-simulation):
   → In ServerSimulation.Tick(), after SimulateTick:
     → If state.State == Attacking && state.AttackSlot > 0:
       → Look up ability stage: slot → def.LMB → Stages[ComboStage]
       → For each HitboxEvent:
         → If AttackElapsedTicks == evt.TriggerTick:
           → SpellResolver.Spawn(hitbox at entity pos + offset)
```

### 7b. Slot Mapping

| ActiveSlot | Key  | Ability     |
|------------|------|-------------|
| 0          | —    | None        |
| 1          | LMB  | Light attack chain |
| 2          | RMB  | Heavy/charge attack |
| 3          | Q    | Ability slot 3 |
| 4          | E    | Ability slot 4 |
| 5          | R    | Ability slot 5 |
| 6          | F    | Ability slot 6 |

### 7c. HitboxEvent → SpellResolver.Spawn

Defined in `AttackData.cs`:

```csharp
public struct HitboxEvent
{
    public ushort TriggerTick;    // When to spawn (in the attack sequence)
    public ushort DurationTicks;  // How long the hitbox stays active
    public float Radius;
    public float OffX, OffY, OffZ;  // Local offset from entity center
    public float Damage;
    public float BaseKnockback, KnockbackGrowth, KnockbackUpward;
    public ushort StunTicks;
    public bool Interruptible;
}
```

Hitboxes are spawned at the precise trigger tick (e.g. frame 6 of an attack) and processed by `SpellResolver` each tick during their lifetime. They use pure math (sphere-sphere/capsule collision) — no engine physics queries.

---

## 8. Client FSM Transitions

The client's `Player._PhysicsProcess` does NOT independently drive gameplay state — it only reacts to what the simulation outputs:

```
Player._PhysicsProcess(delta):

  1. First: ApplyServerState(state) was already called by MatchManager
     → state.State, state.StateTicks, position, velocity are set

  2. FSM transitions (AnimationPlayer):
     → Detect ActionState changes from sim output
     → idle → dashing:  start dash animation
     → idle → attacking: start melee animation
     → attacking → idle: reset combo state
     → grounded → airborne: jump animation
     → hitstun → recover: recovery animation

  3. No input-driven state changes on client!
     → All state transitions are driven by ApplyServerState
     → Even on localhost, the sim is the authority
```

This ensures that even during rollback, the visual state is always driven by the corrected simulation output, not by stale local input processing.

---

## 9. Mode Debug (F3)

Les hurtboxes et hitboxes sont calculées côté serveur. Pour l'affichage F3, deux approches :

### 9a. Simple (maintenant)
Le mode debug réactive une simulation locale *en parallèle* juste pour le debug visuel. La vraie simulation reste sur le serveur.

### 9b. Propre (quand le protocole sera mature)
Le client envoie un flag `RequestDebug` (1 bit dans InputState.flags). Le serveur, s'il voit le flag, envoie en plus :
```
[0..7]   magic = 0x44454255  ("DEBU")
[8..11]  count (uint)
[...]    For each: position_start, position_end, radius, is_hitbox
```

Packet séparé du state normal, le client l'ignore si pas en debug.

---

## 10. Implementation Status

### Phase 1 — Local prediction ✅
- [x] MatchManager._PhysicsProcess: garder le NetworkClient pour l'envoi/réception
- [x] Ajouter une `ServerSimulation` locale dans MatchManager
- [x] Chaque frame: `localSim.Tick(input)` pour prédire
- [x] Appliquer l'état prédit à PlayerController
- [x] Quand le state serveur arrive: comparer, corriger si besoin
- [x] Buffer d'inputs (10-frame ring, _inputBuffer[])
- [x] Buffer de states prédits (10-frame ring, _stateBuffer[])
- [x] Tick monotonic counter (_sendTick)
- [x] Server echo du client tick dans la réponse

### Phase 2 — Combat côté serveur ✅
- [x] Serveur gère dash via InputState.Dash
- [x] Serveur gère attack via InputState.Attack + ActiveSlot
- [x] Serveur gère jump via InputState.Jump
- [x] Packet enrichi avec serialisation complète
- [x] ActiveSlot pipeline (slot press → ability resolution → hitbox spawn)
- [x] HitboxEvent → SpellResolver.Spawn flow

### Phase 3 — Rollback (quand le serveur sera distant) ✅
- [x] Client: buffer des inputs envoyés (10 dernières frames)
- [x] Client: buffer des states prédits (10 dernières frames)
- [x] Quand le state serveur arrive: mismatch > 0.01m → résimuler depuis le dernier état sûr
- [ ] Tests: delay simulé sur UDP pour stresser le rollback

### Phase 4 — Bots (threads séparés)
- [ ] Chaque bot = thread dans ServerApp qui génère InputState
- [ ] Thread lit les states du jeu, décide d'une action, génère input
- [ ] Envoi via une queue thread-safe
- [ ] Scale: 1 thread par bot (typiquement 4-8 max)

### Phase 5 — Déploiement serveur distant
- [ ] ServerApp build avec `dotnet publish -c Release`
- [ ] Déployé sur un VPS
- [ ] UDP hole-punching ou relay pour NAT traversal
- [ ] Monitoring (latence, packet loss, jitter)
