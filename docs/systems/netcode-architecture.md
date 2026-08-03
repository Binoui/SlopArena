# SlopArena Netcode & Simulation Architecture

## 1. Philosophy

**Server-authoritative.** The server is the absolute authority over game state. The client renders the latest server state directly — there is no local simulation, prediction, or rollback in Phase 1 (see §6).

Same architecture as Rivals 2, GGST, SF6.

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
│  │ Input    │  │ ISimulation  │  │ NetworkClient│  │ LobbyClient   │  │
│  │ (WASD)   │─►│ Bridge       │─►│ (UDP)       │  │ (SignalR)     │  │
│  └──────────┘  └──────────────┘  └──────┬──────┘  └───────┬───────┘  │
│                                         │ ▲                │          │
│                                   ┌─────▼─┴─────┐          │          │
│                                   │  RENDER     │          │          │
│                                   │ (Unity)     │          │          │
│                                   │ latest      │          │          │
│                                   │ server state│          │          │
│                                   │ (1-tick lag)│          │          │
│                                   └─────────────┘          │          │
│                                                             │          │
│                 Lobby/meta via SignalR ◄────────────────────┘          │
│                 Match sim via UDP ◄───────────────────────────────────┤
└──────────────────────────────────────────────────────────────────────┘
```

---

## 3. Data Flow

### 3a. Client FixedUpdate (60Hz) — Send

```
PvPMatch.FixedUpdate() / TrainingMatch.OnMatchFixedUpdate():

  1. InputController.Poll() — read Unity InputSystem
     → BuildInputState()
       → keyboard/mouse + ActiveSlot
       → InputState { MoveX, MoveY, flags, ActiveSlot, FacingYaw, AimYaw, AimPitch, AimDistance, TargetEntityId }

  2. NetworkSimulationBridge.Tick(inputs)
     → _tick++ (monotonically increasing per frame)
     → NetworkClient.SendInput(input, _tick)
       → Packet: entityId(8) + tick(4) + InputState(19) = 31B

  3. No local simulation, no prediction ring buffer
     → render the latest server state via PlayerRenderer.ApplyServerState()
       → one-tick display latency is intentional (Phase 1)
```

### 3b. Client FixedUpdate — Render

```
PvPMatch.FixedUpdate() / TrainingMatch.OnMatchFixedUpdate():

  1. Receive server states (non-blocking)
     → NetworkClient.ReceiveStates()
     → Returns: Dictionary<entityId, (tick, CharacterState)>
     → Packet per entity: entityId(8) + tick(4) + CharacterStatePacket(63) = 75B

  2. Store into the bridge
     → NetworkSimulationBridge._latestStates[kv.Key] = kv.Value

  3. Render latest server state
     → PlayerRenderer.ApplyServerState(state)
     → NPC states are rendered directly from server state
       (no prediction — always authoritative)

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
     → SimulateTick: movement, gravity, ground, combat — everything
     → Spawn hitboxes from attack events (HitboxEvent.TriggerTick)
     → SpellResolver.Tick: hitbox vs hurtbox collision, damage, knockback, hitstun

  5. Check deaths (first to maxStocks=3 deaths loses → MatchState.Ended)
     → ServerSimulation.CheckVoidDeaths: KO costs a stock, respawn with brief
       invincibility; 0 stocks → eliminated (frozen spectator, untargetable)
     → StockMatchRule.Evaluate: last player standing wins; simultaneous
       last-stock trade → most stocks wins, equal deaths → shared victory (issue #37)

  6. SendState() — broadcast to all connected clients
     → For each client:
       → For each entity (all rostered players):
       → Packet: entityId(8) + tick(4) + CharacterStatePacket(63) + hasInput(1) + InputState(19) = up to 95B
         → tick = _serverTick (echoed back)
         → hasInput/InputState = the input the server consumed for that entity
           that tick, or the no-input marker (issue #80 — input relay)
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
Receive packet per entity: entityId(8) + tick(4) + CharacterStatePacket(95) + hasInput(1) + InputState(19) = up to 127 bytes

[0..7]    entityId          (ulong)
[8..11]   tick              (uint)       ← echoes client's tick number
[12..106] CharacterStatePacket (95 bytes) — widened per D10/ADR-0011, see §4b table below
[107]     hasInput          (byte)       ← 1 = relayed InputState follows; 0 = no input consumed this tick
[108..126] InputState       (19 bytes)   ← present iff hasInput == 1 (issue #80 — input relay)
```

**The relay section** (issue #80, ADR-0010): the server appends the exact `InputState` it consumed for that entity that tick, so clients can replay opponents' inputs — and exact omissions — during rollback re-simulation. `hasInput = 0` means the server's queue for that entity was empty that tick (or the entity is eliminated/disconnected): clients must *omit* the entity from their re-sim inputs, reproducing the server's `default(InputState)` path exactly. The flag is always present: a no-input packet is 108 bytes, a relayed packet 127 bytes. Encoded by `ServerEntityPacket` (`src/Shared/ServerEntityPacket.cs`).

**CharacterStatePacket layout (95 bytes):**
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
| 48     | byte    | Deaths              | Stock counter: stocks left = maxStocks - Deaths (issue #37) |
| 49-50  | ushort  | DamagePercent       | Smash-style damage %, HUD display (issue #38) |
| 51-62  | ushort×6| Cooldown0..5        | Per-slot cooldown ticks, local HUD fills (issue #38) |
| 63-64  | ushort  | AirTimeTicks        | Fall-ramp gravity timer (D10/ADR-0011) |
| 65-66  | ushort  | DashDurationTicks   | Remaining dash ticks (D10)         |
| 67-70  | float   | DashDirX            | Dash direction X (D10)              |
| 71-74  | float   | DashDirZ            | Dash direction Z (D10)              |
| 75-76  | ushort  | DashCooldownTicks   | Dash cooldown remaining (D10)      |
| 77     | byte    | AirDodgesLeft       | Remaining air dodges (D10)         |
| 78     | byte    | JumpsLeft           | Remaining jumps (D10)              |
| 79-80  | ushort  | InvincibilityTicks  | Post-respawn/dash invincibility (D10) |
| 81-82  | ushort  | TurnaroundTicks     | Turnaround lag remaining (D10)     |
| 83-84  | ushort  | DirHoldTicks        | Ticks holding same direction (D10) |
| 85     | byte    | IsSprinting         | Sprint/dash-dance flag (D10)       |
| 86-89  | float   | LastDirX            | Last input direction X (D10)       |
| 90-93  | float   | LastDirZ            | Last input direction Z (D10)       |
| 94     | byte    | WasAirborneDuringKnockback | Landing/tech context flag (D10) |

Total: 107 bytes base (8 + 4 + 95), up to 127 with the relay section (108 no-input marker / 127 relayed — issue #80, widened per D10/ADR-0011)

**The server sends ALL states to every client.** Clients ignore the ones that don't concern them. No routing overhead.

**Tick echo:** The server reads each client's tick from the input queue and writes it into the response packet(s). The echoed tick is informational for Phase 1 — the client has no prediction ring buffer to match against (see §6).

---

## 5. CharacterState internals (Shared)

`CharacterState` (144 bytes in memory, 95 serialized) is the full per-tick state of one entity:

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

Position, velocity, action state, grounded flag, state duration, attack slot, combo stage, facing yaw, match state, buff remaining ticks, buff active flags, and the D10 movement-resource fields (air time, dash duration/direction/cooldown, air dodges/jumps left, invincibility, turnaround, dir-hold, sprinting, last direction, post-knockback airborne flag — ADR-0011, added so PredictedTrack's rebuild-and-replay is byte-identical for Predictable ActionStates) are serialized. The ability-instance-dependent fields (knockback velocity, hitstun ticks, DI, attack-elapsed/combo-timer/anim-lock/charge ticks, buffered chain) stay local-only — Complex ActionStates (Attacking/Hitstun/Warping) are never re-simulated from a snapshot (see §6).

---

## 6. Prediction & Rollback

The client predicts locally via a three-track model (ADR-0011, `docs/plans/2026-08-02-rollback-netcode.md`, implementation in `src/Shared/Rollback/`):

- **LocalTrack** — the self entity's `ServerSimulation` runs continuously from match start, fed the player's true input every tick, never rebuilt from a snapshot. Corrected by patching the wire-serialized fields onto its own history when the server disagrees, replayed forward only across a Predictable-state suffix.
- **PredictedTrack** — opponents currently in a Predictable `ActionState` (`Idle`/`Dashing`/`JumpSquat`/`AirDodging`) are rebuilt from the confirmed base and replayed forward via the input relay (§4b), holding the last known input at the frontier.
- **RawTrack** — opponents currently in a Complex `ActionState` (`Attacking`/`Hitstun`/`Warping`) render directly from the latest received packet, unchanged from the original Phase 1 behavior for that entity — the ability-instance layer (`ServerAbility` subclasses' private fields) and `SpellResolver`'s hitbox/projectile list are never serialized, so these states are never re-simulated client-side.

See ADR-0011 for the full rationale, including why this is narrower than ADR-0010's original "predict all entities" ambition.

---

## 7. ActiveSlot Pipeline

### 7a. Flow

```
1. Player presses LMB (slot 1)
   → InputController.Poll() reads Unity InputSystem (keyboard/mouse)

2. BuildInputState() sets ActiveSlot = 1 on the InputState

3. InputState sent via the bridge
   → NetworkSimulationBridge.Tick(inputs) → NetworkClient.SendInput(input, _tick)
   → ServerSimulation edge-detects the slot press (prevAttack / state.AttackSlot)

4. ServerSimulation.Tick → Simulation.SimulateTick()
   → Edge-detect Attack flag (prevAttack[entity] vs current)
   → On rising edge: resolve ability from slot
     → slot 1 = def.LMB → ability definition
     → state.AttackSlot = 1, state.State = Attacking
     → state.AttackElapsedTicks = 0
   → On subsequent ticks (state is Attacking):
     → state.AttackElapsedTicks++
     → Check stage timings, chain windows, anim locks

5. Hitbox spawning (post-simulation):
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

## 8. Client Animation State

The client does NOT independently drive gameplay state — it only reacts to what the simulation outputs:

```
PvPMatch.FixedUpdate() / TrainingMatch.OnMatchFixedUpdate():

  1. First: ApplyServerState(state) was already called by the bridge
     → state.State, state.StateTicks, position, velocity are set

  2. PlayerRenderer drives Animancer clip playback from the sim state
     → ActionState changes map to clips (idle/run/jump/fall/dash/hitstun/attack)
     → Clip speed modulated from server timing (GetAnimSpeedFromDuration)
     → Clips are played via _animancer.Play()

  3. No input-driven state changes on client!
     → All state transitions are driven by the server state
     → Even in training mode, the local sim is the authority
```

This ensures the visual state is always driven by the simulation output, not by stale local input processing.

---

## 9. Debug Mode (F3)

Hurtboxes and hitboxes are computed server-side. For the F3 display, two approaches:

### 9a. Simple (now)
Debug mode runs a local simulation *in parallel* purely for visual debugging. The real simulation stays on the server.

### 9b. Clean (once the protocol matures)
The client sends a `RequestDebug` flag (1 bit in InputState.flags). The server, if it sees the flag, additionally sends:
```
[0..7]   magic = 0x44454255  ("DEBU")
[8..11]  count (uint)
[...]    For each: position_start, position_end, radius, is_hitbox
```

Separate packet from the normal state; the client ignores it unless in debug mode.

---

## 10. Implementation Status

### Phase 1 — Local prediction ❌ Not implemented
Deferred to PvP roadmap v2 Phase 7. The client currently renders raw server state (see §6).
- [ ] PvPMatch.FixedUpdate: keep the NetworkClient for send/receive
- [ ] Add a local `ServerSimulation` to the match
- [ ] Each frame: `localSim.Tick(input)` to predict
- [ ] Apply the predicted state to PlayerRenderer
- [ ] When the server state arrives: compare, correct if needed
- [ ] Input buffer (10-frame ring, `_inputBuffer[]`)
- [ ] Predicted-state buffer (10-frame ring, `_stateBuffer[]`)
- [ ] Monotonic tick counter (`_sendTick`)
- [ ] Server echoes the client tick in the response

### Phase 2 — Server-side combat ✅
- [x] Server handles dash via InputState.Dash
- [x] Server handles attack via InputState.ActiveSlot
- [x] Server handles jump via InputState.Jump
- [x] Full packet serialization (49-byte CharacterStatePacket)
- [x] ActiveSlot pipeline (slot press → ability resolution → hitbox spawn)
- [x] HitboxEvent → SpellResolver.Spawn flow

### Phase 3 — Rollback ❌ Not implemented
Deferred to PvP roadmap v2 Phase 7.
- [ ] Client: buffer of sent inputs (last 10 frames)
- [ ] Client: buffer of predicted states (last 10 frames)
- [ ] When the server state arrives: mismatch > threshold → re-simulate from the last confirmed state
- [ ] Tests: simulated UDP delay to stress rollback

### Phase 4 — Bots (separate threads)
- [ ] Each bot = a thread in ServerApp generating InputState
- [ ] Thread reads the game states, decides an action, generates input
- [ ] Send via a thread-safe queue
- [ ] Scale: 1 thread per bot (typically 4-8 max)

### Phase 5 — Remote server deployment
- [ ] ServerApp build with `dotnet publish -c Release`
- [ ] Deployed on a VPS
- [ ] UDP hole-punching or relay for NAT traversal
- [ ] Monitoring (latency, packet loss, jitter)
