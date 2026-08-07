---
name: sloparena-netcode
description: SlopArena server-authoritative netcode — UDP localhost, tick system, ISimulationBridge flow, wire formats (31B in / 60B per entity out), Phase 1: no prediction/rollback
triggers:
  - sloparena netcode
  - server-authoritative
  - rollback
  - udp client server
  - client-side prediction
  - match manager
  - apply server state
---

# SlopArena Netcode

## 1. Three-server model

- **Master server** — separate repo (`SlopArena-MasterServer`), ASP.NET Core + SignalR + PostgreSQL. Server browser, lobby, char select, match start, results. **Never simulates.**
- **Game server** — this repo's `src/Server/` (.NET console). Registers with the master, receives match-start commands, runs 2-4 player matches over UDP.
- **ServerSimulation** — `src/Shared/ServerSimulation.cs`, pure C# tick loop. **Server-authoritative only** — there is no client simulation in Phase 1.

## 2. Wire formats

Verified against `src/Shared/` and `src/Server/MatchInstance.cs`:

- `InputState.Size = 19` (`src/Shared/InputState.cs`)
- **Client → server: `entityId(8) + tick(4) + InputState(19)` = 31 bytes** (`MatchInstance.ReceiveInputs` comment)
- `CharacterStatePacket.Size = 97` (`src/Shared/CharacterStatePacket.cs`; 63 base + 32 D10 movement-resource fields + 2 hitstop/ADR-0012)
- **Server → client, per entity: `entityId(8) + tick(4) + CharacterStatePacket(97) + hasInput(1) + InputState(19)` = up to 97 bytes** — 78B no-input marker / 97B with relayed input (`ServerEntityPacket`, issue #80)

Input relay (issue #80): the server appends the exact `InputState` it consumed for that entity that tick, or the explicit no-input marker when its queue was empty / the entity is eliminated or disconnected. `hasInput = 0` → client omits the entity from re-sim inputs (server's `default(InputState)` path). Clients decode via `ServerEntityPacket.Deserialize` (`NetworkClient.ReceiveLoop`); the relayed inputs are consumed by the rollback bridge.

InputState layout (19 bytes): MoveX(4) + MoveY(4) + flags(1) + ActiveSlot(1) + FacingYaw(2) + AimYaw(2) + AimPitch(2) + AimDistance(2) + TargetEntityId(1).

| Offset | Type   | Field          | Notes                                  |
|--------|--------|----------------|----------------------------------------|
| 0-3    | float  | MoveX          | Horizontal analog input                |
| 4-7    | float  | MoveY          | Vertical analog input                  |
| 8      | byte   | flags          | bit0:Up,1:Down,2:Left,3:Right,4:Jump,5:Dash,6:Crouch,7:IsAiming |
| 9      | byte   | ActiveSlot     | 0=none, 1=LMB, 2=RMB, 3=Q, 4=E, 5=R, 6=F |
| 10-11  | short  | FacingYaw      | Degrees × 100 (movement-facing)        |
| 12-13  | short  | AimYaw         | Degrees × 100 (combat-facing, reserved)|
| 14-15  | short  | AimPitch       | Degrees × 100 (camera vertical aim)    |
| 16-17  | ushort | AimDistance    | cm (0-6500 = 0-65m)                    |
| 18     | byte   | TargetEntityId | Client-selected target (0 = none)      |

CharacterStatePacket layout (63 bytes): TickNumber(4) + Position(12) + Velocity(12) + CurrentActionState(1) + IsGrounded(1) + StateDurationFrames(2) + AttackSlot(1) + ComboStage(1) + AnimIndex(1) + FacingYaw(4) + MatchState(1) + BuffRemainingTicks(2) + BuffActiveFlags(1) + HitstunLevel(1) + AimPitch(4) + Deaths(1) + DamagePercent(2) + Cooldown0..5(12).

The server sends ALL entity states to every client; each client filters by entityId. The tick field echoes the client's tick (informational in Phase 1). When adding a packet field, update the `Size` constant AND all four serialization methods (`FromState`/`ToState`/`Serialize`/`Deserialize`).

## 3. Client flow

Callers: `TrainingMatch.OnMatchFixedUpdate()` / `PvPMatch.FixedUpdate()` (`client/Unity/Assets/Scripts/Runtime/World/`).

**Send (60Hz):**
1. `InputController.Poll()` reads Unity InputSystem → `BuildInputState()` → `InputState`.
2. `ISimulationBridge.Tick(inputs)` — training: `LocalSimulationBridge`; PvP: `NetworkSimulationBridge`.
3. `NetworkSimulationBridge.Tick` does `_tick++`, `NetworkClient.SendInput(input, _tick)`, then drains `NetworkClient.ReceiveStates()` into `_latestStates`.
4. No local simulation — render the latest server state: `PlayerRenderer.ApplyServerState(state)`. One-tick display latency is intentional (Phase 1).

NPC states render directly from server state — always authoritative, no prediction.

## 4. Server flow

`MatchInstance.Tick()` runs at 60Hz on the match's own thread (`src/Server/MatchInstance.cs`):

1. `ReceiveInputs()` — drain UDP socket, match by entityId, queue inputs per `PlayerSlot`.
2. Timeout check — 5s silence from any connected player → match stops, port freed.
3. Flush input queues — take the last valid packet per slot (`InputBufferWindow=6` in sim handles buffering); `_serverTick = max(_serverTick, latestClientTick)`.
4. `ServerSimulation.Tick(inputs)` — movement, gravity, ground, combat, hitbox spawn (`HitboxEvent.TriggerTick`), `SpellResolver` collision — everything.
5. Death check — the match rule (issue #37): `StockMatchRule` = KO costs a stock, respawn with brief invincibility; 0 stocks → eliminated (frozen spectator, untargetable); last standing wins, tie → shared victory. `NoWinMatchRule` (training) = no elimination, no match end. `rule.Evaluate` → `MatchState.Ended`.
6. `SendState()` — broadcast every entity's packet to all connected clients.

Multi-match: `MultiMatchOrchestrator` allocates ports `base → base+max-1`, one `MatchInstance` per port. `MatchControlServer` handles `POST /match/start` from the master. `GameServerRegistration` registers at startup, heartbeats every 10s, reports results for MMR.

Client-side classes: `NetworkClient` (UDP send/receive with tick tracking), `LobbyClient` (SignalR for lobby/meta), `MatchBase` (abstract — arena load, renderer pool, camera, HUD, sim tick, ApplyServerState).

## 5. Prediction & rollback — NOT implemented (Phase 1)

The client does NOT predict locally or roll back. `NetworkSimulationBridge`'s doc comment:

> No local simulation — one-tick display latency is intentional (Phase 1).

`NetworkSimulationBridge.Resolver => null` — the server owns hitbox collision. There are no input/state ring buffers, no server-vs-predicted mismatch comparison, no re-simulation anywhere in `client/`.

Prediction + rollback are deferred to Phase 7 of `docs/plans/2026-08-01-pvp-roadmap-v2.md` (marked ❌ Absent). On localhost the round-trip is under one tick, so raw state display is effectively synchronous; prediction is deferred until the server runs remotely.

(This skill is the right one to load for rollback questions — the answer is "not implemented yet".)

## 6. Pitfalls

- **Unity `.meta` files must be committed** — Unity regenerates GUIDs otherwise, breaking references.
- **5s connection timeout** — a silent player's match is stopped and its port freed; don't leak matches.
- **Packet size changes** — update `Size` + all four serialization methods, or clients/server desync silently.
- **AimYaw is reserved** — the sim does not use combat-facing aim yet; do not wire it up without discussing (systemic change).
