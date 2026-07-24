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
