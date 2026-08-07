# SlopArena

3D platform fighter (Smash/DKO-style) with server-authoritative 60Hz simulation.

## Language

**AirTime**:
An airborne tick counter that determines fall gravity phase. Incremented each tick while airborne. Reset behavior is conditional:
- **0** on: aerial attack, taking damage, landing (re-grants FloatWindow)
- **FloatWindowTicks + FallRampDuration** on: any jump (ground or double). JumpArc handles the ascent visually, so the float window is skipped — full gravity applies immediately after jump.
- **clamped to ≥ FloatWindowTicks** on: aerial dash (never resets the float window, but doesn't advance past it either)
Ground dash sets AirTime to 0.
_Avoid_: air timer, hang time, air duration

**FloatWindow**:
The initial period of AirTime during which reduced (`AirFloatGravity`) gravity applies, creating a floaty feel that enables aerial recovery chains. Per-character value.
_Avoid_: hover time, stall window, float duration

**FallRamp**:
The progressive acceleration of fall speed from FloatWindow gravity to full gravity. Gravity increases linearly over `FallRampDuration` ticks once `AirTime >= FloatWindowTicks`.
_Avoid_: gravity ramp, fall acceleration curve

**FallGravity**:
The full gravity constant applied after the FallRamp completes. Equivalent to `MovementStats.Gravity`.
_Avoid_: terminal velocity, max gravity

**FallRampDuration**:
The number of ticks over which gravity ramps from `AirFloatGravity` to `FallGravity`. Per-character tuning value.
_Avoid_: ramp time, acceleration window

**JumpArc**:
The complete animation clip played during a single jump (ground or double). Plays through both ascent and baked descent. The character transitions to the Fall animation only when the JumpArc clip finishes while still airborne — not at the physics peak. Enables a clean, full jump animation without an abrupt mid-air switch.
_Avoid_: jump animation, jump loop, air state

**JumpArcActive**:
A per-entity bool (client-side) tracking whether the JumpArc clip is currently playing. Cleared on landing, aerial attack, or hitstun.
_Avoid_: isAscending, wasAscending, jump state

**Warp**:
The auto-dash during attack initiation that moves the entity toward the target at SprintSpeed. A separate phase before the attack's startup — the attack begins only after warp completes (distance closed to AttackRange). Not a fixed-duration animation; duration depends on distance and SprintSpeed. `WarpSpeed` is a boolean (0 or 1) flag indicating warp is active — the actual velocity is `CharacterDefinition.Movement.SprintSpeed`.
_Avoid_: lunge, auto-approach, gap closer

**WarpRange**:
The maximum distance at which warp will trigger. If the closest enemy within a forward-facing cone is between AttackRange and WarpRange, warp initiates. Per-attack tuning value.
_Avoid_: warp distance, lunge range, approach range

**WarpCone**:
The forward-facing angle (centered on FacingYaw) within which warp can target an enemy. Currently 120° (60° left/right). Enemies outside the cone are ignored for warp, preventing warp to targets behind the character.
_Avoid_: warp angle, field of view, targeting cone
**Knockback**:
The launch velocity applied when hitting a target. Combination of base push and damage-scaling growth (higher % = further launch). Uses frontloaded exponential decay (λ = 1.8/s): the launch is fastest right after the hit and smoothly slows — most travel happens early, the victim drifts in the tail. Decaying all axes also flattens launch arcs. Profile table maps archetypes to angle/base/growth:
- **Light**: 15°, base=2, growth=1.5 — combo glue, slight pop
- **Medium**: 15°, base=8, growth=5 — knockdown, reset
- **Launcher**: 25°, base=8, growth=4 — pop-up, stays on screen
- **Kill**: 20°, base=18, growth=10 — blast zone send
- **Spike**: -45°, base=12, growth=4 — downward, grounded bounce
Any attack can also use a per-hit **Custom** profile with its own angle/base/growth overrides.
_Avoid_: push force, hit reaction, knockback velocity

**Hitstun**:
The victim-side no-input lock after a hit. The victim cannot act (inputs buffer instead) until HitstunTicks expires; control returns with whatever residual speed remains — the lock is the stun, not the flight. Duration is the hitbox's StunTicks capped by the knockback-derived `clamp(8 + magnitude/2, 8, 60)`. Burst (below) is the explicit exception to the lock.
_Avoid_: stun lock, flinch, hitstun lock

**Hitstop**:
The brief freeze of attacker and victim when a hit connects — per-pair, not global; the match clock keeps running. Knockback launches only when the freeze ends. The decision beat: the defender picks Combo Influence direction and whether to Burst while both are frozen.
_Avoid_: hitlag (Melee connotation), freeze, hit pause

**Duration Lock**:
A fixed-tick state during which a character cannot act. Two kinds: the attacker's attack commitment (AnimLockTicks, from startup through recovery) and the victim's Hitstun. Burst's offensive use cancels the attacker's lock.
_Avoid_: endlag, animation lock, commitment lock

**Combo Influence**:
The defender's launch-drift input — additive velocity applied to remaining horizontal knockback in the held direction, scaled to the launch magnitude. Captured during Hitstop + Hitstun, applied when the lock expires. Additive (Smash-4 vectoring model), not rotational (Smash DI) — 3D-native: push where you want to drift.
_Avoid_: DI (Smash rotation connotation), vectoring (Smash 4), smash DI

**Burst**:
The universal escape/extender on one long per-entity cooldown that persists through KO. Defensive: breaks Hitstun + knockback, small push on the attacker, then a recovery window — punishable if baited. Offensive: cancels your own Duration Lock and spawns a fixed-knockback hitbox (zero damage scaling) to extend a string. Cooldown visible to both players.
_Avoid_: trinket (WoW connotation), get-out-of-jail, escape tool

**DashInvincibility**:
The invincibility frames granted at dash start. The dashing entity cannot take damage or be hit for the full dash duration. Currently shared across all characters (`DashInvincibilityTicks = DashDurationTicks`). Creates the core dash mindgame: burn your dash to dodge an attack, or save it to avoid being baited.
_Avoid_: i-frames, dodge window, invuln

**FloatWindowReset**:
The restoration of FloatWindow gravity by setting AirTime to 0 mid-air. Triggered by: aerial attack, taking damage, or landing. This is the core aerial recovery mechanic — chaining attacks resets the float window, letting the character stay floaty and continue aerial combos. Without a reset, the character progresses through FallRamp into full gravity.
_Avoid_: air reset, float restore, hover refresh

## PvP / Multiplayer

**ServerBrowser**:
A listing of active game servers maintained by the master server. Players browse the list (name, region, player count) and join one directly. No matchmaking queue — the player chooses which server to connect to. Like Counter-Strike community servers.
_Avoid_: matchmaking, queue, server list (too generic)

**LobbyRoom**:
A pre-match waiting state managed by the master server via SignalR. Players who have joined a game server wait in the lobby room, see the player list, and the host presses Start to begin character select. Not the game server's concern — the game server only receives "start match" commands.
_Avoid_: waiting room, pre-game, staging

**GuestAuth**:
Anonymous authentication via `POST /auth/guest` on the master server. Returns a JWT + temporary SteamId (Guid). No Steam SDK, no credentials. Placeholder for future Steam authentication — the JWT issuance and all downstream code stays unchanged when Steam auth lands.
_Avoid_: dev login, anonymous auth, temp account

**StockMode**:
The win condition for PvP matches. Each player starts with N stocks (default 3). Getting KO'd (void death or blast zone) costs one stock. A player with 0 stocks is eliminated. Last player with stocks remaining wins. Scales naturally from 2 to 4+ players.
_Avoid_: lives mode, stock battle, elimination mode

**HostAndPlay**:
The embedded host model where a player starts the game server from the Unity client (subprocess), registers it with the master server, and plays on the same machine (connecting to localhost). The demo targets this at technical players only: the host machine must be reachable from outside (port forwarding or LAN), which non-technical players won't do. The server binary and registration flow are identical to a dedicated server.
_Avoid_: listen server, client-hosted, peer-to-peer host

**OfficialServer**:
An operator-run, always-on GameServer instance listed in the ServerBrowser (the demo runs these on the home mini PC). Players join it but never host it; it is the default online path for non-technical users because it removes per-player NAT/port-forwarding. The opposite of a HostAndPlay server. `isOfficial` in the registration payload flags the server, though nothing currently filters on it.
_Avoid_: hosted server, our server, dedicated (ambiguous with server binary)

**MatchFlow**:
The lifecycle of a PvP match: Server Browser → Lobby Room → Character Select → Countdown → Fight → Results → Lobby Room. The master server (SignalR) manages lobby/char-select/results; the game server (UDP) manages countdown/fight only.
_Avoid_: game flow, match lifecycle, session flow

## Prediction & Rollback

**ConfirmedTick**:
The highest match tick for which the client holds a full server-authoritative snapshot for every non-self entity. The base state PredictedTrack rebuilds from; it advances as state packets arrive, one tick at a time. Does not apply to LocalTrack — the self entity is never rebuilt from a snapshot.
_Avoid_: ack tick, sync tick, last confirmed

**RollbackWindow**:
The span of ticks between ConfirmedTick and the currently rendered tick, replayed on PredictedTrack entities whenever the confirmed base advances or a mismatch is corrected. Applies only to opponents currently in a Predictable ActionState — RawTrack entities have no window (they render the latest packet directly), and LocalTrack has no window (it never rebuilds).
_Avoid_: rewind window, prediction buffer, lag window

**InputRelay**:
The server broadcasting each entity's consumed InputState — or an explicit no-input marker when the server had nothing to consume for it that tick (empty queue → drop → `default(InputState)`) — alongside its state packet, so every client can replay opponents' exact inputs *and omissions* during re-simulation. Feeds PredictedTrack's replay; LocalTrack uses the player's own true input buffer instead.
_Avoid_: input forwarding, input piggyback, input echo

**LocalTrack**:
The self entity's `ServerSimulation`, run continuously on the client from match start — never rebuilt from a received snapshot, fed the player's own true InputState every tick. Corrected only by snapping the wire-serialized fields when a received packet disagrees; fields absent from the wire (attack timers, knockback, ability-instance state) never diverge because this track is never reconstructed from a lossy snapshot.
_Avoid_: self-prediction, client-side prediction (ambiguous with PredictedTrack), own-entity sim

**PredictedTrack**:
An opponent entity currently in a Predictable ActionState. Rebuilt from ConfirmedTick's snapshot and replayed forward tick-by-tick using InputRelay data (or the no-input marker) up to the current local tick. Diverges only at the frontier (ticks past the last received relay), corrected by snap on the next packet.
_Avoid_: opponent prediction, ghost sim, replayed entity

**RawTrack**:
An opponent entity currently in a Complex ActionState. No local re-simulation — rendered directly from the latest received packet, identical to pre-rollback (Phase 1) behavior, scoped to just this entity for just this window. Switches to PredictedTrack the tick the entity returns to a Predictable ActionState.
_Avoid_: unpredicted entity, fallback display, snap-only entity

**Predictable ActionState**:
An `ActionState` whose per-tick behavior depends only on fields carried by the confirmed-base sync: position/velocity, the generic state timer, and the movement-resource fields added for rollback (`AirTimeTicks`, dash timers/direction, jump/dodge counters, etc.). Currently `Idle`, `Dashing`, `JumpSquat`, `AirDodging`. PredictedTrack re-sim is byte-identical for these. (`Sliding` exists in the enum but is unused by any current code — not a member of either partition.)
_Avoid_: safe state, simple state, movement state

**Complex ActionState**:
An `ActionState` whose behavior depends on fields no sync packet carries — the per-instance `ServerAbility` layer (private fields like `NilusVoidRift`'s cached aim/seed state) and/or `SpellResolver`'s live hitbox/projectile list, plus knockback/hitstun/DI fields. Currently `Attacking`, `Hitstun`, `Warping`. Never re-simulated on PredictedTrack — entities in these states run RawTrack instead.
_Avoid_: unsafe state, hard state, ability state


## Game Server (src/Server)

**GameServer**:
The dedicated .NET console process that runs match simulations. Registers with the master server, receives match-start commands, and runs 2-4 player matches on dedicated UDP ports. Lives in `src/Server/`. This is what the master server's ServerBrowser lists; clients connect to it by IP+port for the fight.
_Avoid_: server (ambiguous — see disambiguation below), ServerApp (old name), match server

**MatchControlServer**:
The HTTP control plane on the GameServer. Listens on TCP at the registered base port and exposes `POST /match/start` for the master server. Parses the roster, asks the orchestrator for a port, and replies with it. UDP matches bind base+offset, so TCP control and UDP simulation coexist on the same port number. This is the seam that keeps the GameServer stateless between matches (ADR-0008).
_Avoid_: control endpoint, match API, HTTP server

**MultiMatchOrchestrator**:
The component inside the GameServer that manages port allocation and match lifecycle across concurrent matches. Assigns each new match to the next free UDP port, tracks active MatchInstances, and reclaims ports on match end. The single owner of the match collection — nothing else spins up or tears down a match directly.
_Avoid_: match manager (collides with client-side MatchManager), server manager, match pool

**MatchInstance**:
One running match — 2-4 rostered players, one dedicated UDP port, one thread, full 60Hz ServerSimulation. Spawned by the orchestrator on match start with the roster's character classes + entity IDs; disposed on match end (winner detected or all opponents gone). Countdown starts once every rostered player has connected.
_Avoid_: match (too generic), game session, server instance

**Roster**:
The ordered player list the master server sends at match start. Each entry pairs a player's SteamId with their locked-in CharacterClass and an assigned EntityId (1..N by lobby join order). Drives entity spawning on the GameServer — replaces the old hardcoded-Manki path.
_Avoid_: player list, team, lineup

**PortAllocation**:
The scheme where each match binds a dedicated UDP port from `base_port` to `base_port + max_concurrent_matches - 1`. One match per port. The TCP MatchControlServer listens on base_port itself; UDP matches use base+offset, so the two never collide on the same number.
_Avoid_: port pool, port map, port range (too vague)

## Disambiguation: "server"

The word "server" is overloaded in SlopArena. Three distinct things share it:
- **Master server** — the separate-repo ASP.NET Core app (SignalR/REST, PostgreSQL) that handles matchmaking, lobby, char-select, and results. Repo: `SlopArena-MasterServer`. Never runs simulation.
- **Game server** (GameServer above) — the .NET console process in this repo (`src/Server/`) that runs match simulation over UDP. Registers with the master server; receives match-start commands via MatchControlServer.
- **ServerSimulation** — the pure C# tick loop in `src/Shared/` (`Simulation.cs`, `CombatMath.cs`). Runs identically on client (prediction) and GameServer (authority). Not a process — a class.

When any of these is meant, use the full term. Bare "server" is ambiguous and should be challenged.
