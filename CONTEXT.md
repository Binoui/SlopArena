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
The launch velocity applied when hitting a target. Combination of base push and damage-scaling growth (higher % = further launch). Uses linear deceleration for a fast initial snap that heavily slows down approaching max range. Profile table maps archetypes to angle/base/growth:
- **Light**: 15°, base=2, growth=1.5 — combo glue, slight pop
- **Medium**: 15°, base=8, growth=5 — knockdown, reset
- **Launcher**: 25°, base=8, growth=4 — pop-up, stays on screen
- **Kill**: 20°, base=18, growth=10 — blast zone send
- **Spike**: -45°, base=12, growth=4 — downward, grounded bounce
Any attack can also use a per-hit **Custom** profile with its own angle/base/growth overrides.
_Avoid_: push force, hit reaction, knockback velocity

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
The embedded host model where a player starts the game server from the Unity client (subprocess), registers it with the master server, and plays on the same machine (connecting to localhost). Placeholder for future dedicated servers — the server binary and registration flow are identical.
_Avoid_: listen server, client-hosted, peer-to-peer host

**MatchFlow**:
The lifecycle of a PvP match: Server Browser → Lobby Room → Character Select → Countdown → Fight → Results → Lobby Room. The master server (SignalR) manages lobby/char-select/results; the game server (UDP) manages countdown/fight only.
_Avoid_: game flow, match lifecycle, session flow
