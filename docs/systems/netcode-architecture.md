# SlopArena Netcode & Simulation Architecture

## 1. Philosophy

**Server-authoritative Shared simulation with client prediction and reconciliation.**
The GameServer is authoritative. `ServerSimulation` also runs on client tracks so the
client can predict and reconcile without owning gameplay results. Unity presents the
track-selected simulation state and semantic events.

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
│                                   │  track-selected  │          │          │
│                                   │  simulation state│          │          │
│                                   └─────────────┘          │          │
│                                                             │          │
│                 Lobby/meta via SignalR ◄────────────────────┘          │
│                 Match sim via UDP ◄───────────────────────────────────┤
└──────────────────────────────────────────────────────────────────────┘
```

---

## 3. Data Flow

### 3a. PvP flow

`PvPMatch` starts from the admitted match catalog and owns the
[`RollbackSimulationBridge`](../../client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs).
Its fixed update:

1. Builds `InputState` from the Unity input adapter and the canonical active slot.
2. Calls `RollbackSimulationBridge.Tick`.
3. The bridge sends local input through `NetworkClient`, advances the Shared local track,
   drains entity packets, match-result packets, and presentation-event queues, and exposes
   selected state and events for rendering.
4. The bridge routes the self packet to reconciliation and opponent packets to
   `RollbackSimulator`, which chooses `PredictedTrack` or `RawTrack` by action state.
5. `PvPMatch` applies bridge-selected state and presentation events to `PlayerRenderer`
   and other presentation systems.

See [`PvPMatch.cs`](../../client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs),
[`RollbackSimulationBridge.cs`](../../client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs),
and [`RollbackSimulator.cs`](../../src/Shared/Rollback/RollbackSimulator.cs).

### 3b. Training flow

Training uses `LocalSimulationBridge` with the same Shared simulation authority and
cooked/admitted content boundary. It builds local input, advances the local simulation,
and exposes state and presentation events to the renderers without a network transport.
Training does not use `NetworkSimulationBridge`.

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
      → Packet: entityId(8) + tick(4) + CharacterStatePacket(109) + hasInput(1) + InputState(20) = up to 142B
         → tick = _serverTick (echoed back)
         → hasInput/InputState = the input the server consumed for that entity
           that tick, or the no-input marker (issue #80 — input relay)
       → Client filters by entityId
```

---

## 4. Packet Protocol

### 4a. Client → Server

```
Send packet: entityId(8) + tick(4) + InputState(20) = 32 bytes

[0..7]   entityId        (ulong)
[8..11]  tick            (uint)       ← local client frame counter
[12..31] InputState (20 bytes)
```

**InputState layout (20 bytes):**
| Offset | Type    | Field           | Notes                              |
|--------|---------|-----------------|------------------------------------|
| 0-3    | float   | MoveX           | Horizontal analog input            |
| 4-7    | float   | MoveY           | Vertical analog input              |
| 8      | byte    | flags           | bit0:Up, 1:Down, 2:Left, 3:Right, 4:Jump, 5:Dash, 6:Burst (ADR-0014; formerly Crouch), 7:IsAiming |
| 9      | byte    | ActiveSlot      | 0=none, 1=LMB, 2=RMB, 3=key"1", 4=E, 5=R, 6=F, 7-10=keys"2"-"5", 11=A (ADR-0016) |
| 10-11  | short   | FacingYaw       | Degrees × 100 (movement-facing)    |
| 12-13  | short   | AimYaw          | Degrees × 100 (combat-facing, reserved) |
| 14-15  | short   | AimPitch        | Degrees × 100 (camera vertical aim) |
| 16-17  | ushort  | AimDistance     | cm (0-6500 = 0-65m)                |
| 18     | byte    | TargetEntityId  | Client-selected target (0 = none)  |
| 19     | byte    | flags2          | bit0: JumpHeld (ADR-0016 short hop, issue #116) |

Total: 32 bytes (8 + 4 + 20)

### 4b. Server → Client (per entity)

```
Receive packet per entity: entityId(8) + tick(4) + CharacterStatePacket(109) + hasInput(1) + InputState(20) = up to 142 bytes

[0..7]    entityId          (ulong)
[8..11]   tick              (uint)       ← echoes client's tick number
[12..120] CharacterStatePacket (109 bytes) — fixed state payload; see §4b table below
[121]     hasInput          (byte)       ← 1 = relayed InputState follows; 0 = no input consumed this tick
[122..141] InputState       (20 bytes)   ← present iff hasInput == 1 (issue #80 — input relay)
```

**The relay section** (issue #80, ADR-0010): the server appends the exact `InputState` it consumed for that entity that tick, so clients can replay opponents' inputs — and exact omissions — during rollback re-simulation. `hasInput = 0` means the server's queue for that entity was empty that tick (or the entity is eliminated/disconnected): clients must *omit* the entity from their re-sim inputs, reproducing the server's `default(InputState)` path exactly. The flag is always present: a no-input packet is 122 bytes, a relayed packet 142 bytes. Encoded by `ServerEntityPacket` (`src/Shared/ServerEntityPacket.cs`).

**CharacterStatePacket layout (109 bytes):**
| Offset | Type    | Field                       | Notes                              |
|--------|---------|-----------------------------|------------------------------------|
| 0-3    | uint    | TickNumber                  | Echoed client tick (for matching)  |
| 4-7    | float   | PositionX                   | World X                            |
| 8-11   | float   | PositionY                   | World Y (up)                       |
| 12-15  | float   | PositionZ                   | World Z (forward)                  |
| 16-19  | float   | VelocityX                   | World velocity X                   |
| 20-23  | float   | VelocityY                   | World velocity Y                   |
| 24-27  | float   | VelocityZ                   | World velocity Z                   |
| 28     | byte    | CurrentActionState          | Idle/Dashing/Attacking/Hitstun     |
| 29     | byte    | IsGrounded                  | 0 or 1                             |
| 30-31  | ushort  | StateDurationFrames         | Remaining ticks in current state   |
| 32     | byte    | AttackSlot                  | 0=none, 1-11 (ADR-0016 slot layout)|
| 33     | byte    | ComboStage                  | 0-3 combo chain stage              |
| 34-37  | float   | FacingYaw                   | Server-authoritative facing (radians) |
| 38     | byte    | MatchState                  | Match lifecycle (Waiting/Countdown/Playing/Ended) |
| 39     | byte    | AnimIndex                   | Animation index into ability's AnimationNames[] |
| 40     | byte    | HitstunLevel                 | 0=small, 1=medium, 2=hard          |
| 41-44  | float   | AimPitch                    | Server-authoritative aim pitch (radians) |
| 45     | byte    | Deaths                       | Stock counter: stocks left = maxStocks - Deaths (issue #37) |
| 46-47  | ushort  | DamagePercent                | Smash-style damage %, HUD display (issue #38) |
| 48-69  | ushort×11| Cooldown0..10               | Per-slot cooldown ticks (ADR-0016: 11 slots), local HUD fills (issue #38) |
| 70-71  | ushort  | AirTimeTicks                | Fall-ramp gravity timer (D10/ADR-0011) |
| 72-73  | ushort  | DashDurationTicks            | Remaining dash ticks (D10)         |
| 74-77  | float   | DashDirX                    | Dash direction X (D10)             |
| 78-81  | float   | DashDirZ                    | Dash direction Z (D10)             |
| 82-83  | ushort  | DashCooldownTicks            | Dash cooldown remaining (D10)      |
| 84     | byte    | AirDodgesLeft                | Remaining air dodges (D10)         |
| 85     | byte    | JumpsLeft                    | Remaining jumps (D10)              |
| 86-87  | ushort  | InvincibilityTicks           | Post-respawn/dash invincibility (D10) |
| 88-89  | ushort  | RushTicks                    | Rush window remaining (ADR-0020)   |
| 90-93  | float   | LastDirX                    | Last input direction X (D10)       |
| 94-97  | float   | LastDirZ                    | Last input direction Z (D10)       |
| 98     | byte    | WasAirborneDuringKnockback   | Landing/tech context flag (D10)    |
| 99-100 | ushort  | HitstopTicks                 | Remaining hitstop freeze ticks (ADR-0012) |
| 101-102| ushort  | BurstCooldownTicks           | Burst cooldown (ADR-0014)          |
| 103-104| ushort  | BurstRecoveryTicks           | Burst recovery lock (ADR-0014)     |
| 105    | byte    | JumpHeldTicks                | Consecutive jump-held ticks — short-hop replay (ADR-0016) |
| 106    | byte    | LockOn                      | Persistent target-lock flag (ADR-0018) |
| 107-108| ushort  | LedgeRegrabLockTicks         | Walk-off self-grab suppression — on-wire so rollback reproduces a walk-off |

**Packet sizes:** `CharacterStatePacket` is 109 bytes. `ServerEntityPacket` is 121 bytes before the relay section, 122 bytes with the no-input marker, and 142 bytes with relayed input.

**The server sends ALL states to every client.** Clients ignore the ones that don't concern them. No routing overhead.

**Tick echo:** The server echoes the consumed client tick so `RollbackSimulationBridge`
can match the self response to local history for reconciliation. Opponent packets are
ingested by `RollbackSimulator` into predicted or raw tracks; the tick is not merely
informational.

---

## 5. CharacterState and packet roles (Shared)

`CharacterState` is the mutable per-entity Shared simulation state. It is used directly
by `ServerSimulation` and the rollback tracks; it is not a hand-maintained wire-size
contract.

`CharacterStatePacket` is the explicit serialized state snapshot used for reconciliation
and opponent ingestion. `ServerEntityPacket` wraps an entity identity, tick, state packet,
and optional relayed input. Their layouts and codecs are owned by the source files:

- [`CharacterState.cs`](../../src/Shared/CharacterState.cs)
- [`CharacterStatePacket.cs`](../../src/Shared/CharacterStatePacket.cs)
- [`ServerEntityPacket.cs`](../../src/Shared/ServerEntityPacket.cs)

Snapshots serialize the fields needed to rebuild predictable movement state. Ability
instance fields such as active hitbox/projectile lists and private lifecycle state are
not fully reconstructible; complex action states therefore use received state rather than
client re-simulation.

---

## 6. Prediction & Rollback

The client uses the three-track model implemented in
[`RollbackSimulator`](../../src/Shared/Rollback/RollbackSimulator.cs):

- **LocalTrack** continuously simulates the self entity with the player's input and
  reconciles corrections from the server.
- **PredictedTrack** replays predictable opponents from confirmed state and relayed input.
- **RawTrack** renders complex or unknown opponents from their latest received state when
  their private ability state cannot be reconstructed.

This is narrower than predicting every remote ability: predictable opponents can be
replayed, while complex or unknown opponents use received state.

---

## 7. ActiveSlot Pipeline

The gameplay path is:

```text
InputState.ActiveSlot
  → canonical ground/air slot
  → admitted character definition
  → cooked timeline or existing trusted/legacy lifecycle
  → Shared simulation state and presentation events
  → bridge-selected presentation
```

Physical controls are input adapters. They select the canonical slot; they are not a
second persisted move mapping. The admitted definition comes from the immutable Match
Content Catalog. New package content executes through cooked timelines; trusted temporary
capabilities and legacy Nilus implementations retain their existing lifecycle seam.

The Shared simulation owns timing, hitbox/projectile resolution, damage, Knockback,
Hitstun, interruption, and emitted events. Unity does not resolve collisions or decide
gameplay results. See [`ServerSimulation.cs`](../../src/Shared/ServerSimulation.cs),
[`CharacterPackageCompiler.cs`](../../src/Shared/CharacterPackageCompiler.cs), and
[`hitbox-system.md`](hitbox-system.md).

---

## 8. Client presentation state

The client does not independently drive gameplay state. `PlayerRenderer` and related
visual systems consume the bridge-selected simulation output, including local prediction,
reconciled self state, predicted opponents, raw opponents, and semantic presentation events:

```text
PvPMatch / Training
  → bridge-selected Shared state and events
  → PlayerRenderer / Animancer / UI / VFX
```

Animation, VFX, and audio remain presentation-only. They do not infer gameplay from raw
input and do not feed results back into Shared simulation.

---

## 9. Debug visualization

Use the existing [Hitbox System](hitbox-system.md) guidance for visualization. Debug
drawings may display geometry derived from Shared state or received events, but
visualization does not own collision, hit results, damage, or match authority. No new
debug wire protocol is defined by this document.

---

## 10. Implementation references and remaining product work

The implemented boundaries are:

- Shared deterministic simulation: [`src/Shared/ServerSimulation.cs`](../../src/Shared/ServerSimulation.cs);
- three-track client rollback: [`src/Shared/Rollback/`](../../src/Shared/Rollback/) and
  [`RollbackSimulationBridge.cs`](../../client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs);
- content admission: [`MatchContentCatalog.cs`](../../src/Shared/MatchContentCatalog.cs) and
  the GameServer match-control/catalog providers under `src/Server/`;
- transport and packet handling: the Unity `NetworkClient` and Shared packet types.

The [playable friends demo reset](../plans/2026-09-05-playable-demo-reset.md) tracks the
remaining product work, including roster-complete publishing and remote join-to-rematch
acceptance. This guide does not claim unexercised remote play as verified.
