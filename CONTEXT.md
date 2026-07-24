# SlopArena

3D platform fighter (Smash/DKO-style) with server-authoritative 60Hz simulation.

## Language

**AirTime**:
The number of ticks since the character last performed an actionable event (attack, dash, jump), took damage, or landed on ground. Determines which phase of fall gravity the character is in.
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
The auto-dash during attack initiation that moves the entity toward the target at SprintSpeed. A separate phase before the attack's startup — the attack begins only after warp completes (distance closed to AttackRange). Not a fixed-duration animation; duration depends on distance and SprintSpeed.
_Avoid_: lunge, auto-approach, gap closer

**WarpRange**:
The maximum distance at which warp will trigger. If the closest enemy within a forward-facing cone is between AttackRange and WarpRange, warp initiates. Per-attack tuning value.
_Avoid_: warp distance, lunge range, approach range

**WarpCone**:
The forward-facing angle (centered on FacingYaw) within which warp can target an enemy. Currently 120° (60° left/right). Enemies outside the cone are ignored for warp, preventing warp to targets behind the character.
_Avoid_: warp angle, field of view, targeting cone
