# ADR-0018: RMB Target-Lock Toggle — Persistent Soft Lock

**Status:** Accepted — 2026-08-12 (user decision; engine ticket #127)
**Deciders:** @Binoui

## Context

The engine already has a soft-lock system: `ProcessTargetLock` resolves a target every tick (client screen-center preference, nearest-enemy fallback within 20m), stores it in `CharacterState.TargetEntityId`, and **during attack stages with `UseTargetLock=true`** lerps facing toward it (`RotateTowardTarget`/`TrackingStrength`). Most normals already opt in. The client renders the target (`TargetIndicator`) and the resolver already re-picks on target death.

What does not exist is *persistent* facing tracking outside attacks. The 8-slot re-tier freed `RMB` (ADR-0017 reserves it for a future utility). A persistent lock toggle gives casual players auto-aiming attacks at the cost of manual attack direction — a clean on/off switch between two facing modes, and an onboarding ramp into the ADR-0017 manual-facing system (start locked, graduate to LMB snap + sticky air facing).

## Decision

1. **`RMB` = target-lock toggle.** Press once = lock on, press again = off. Toggle state is sim-authoritative (`CharacterState.LockOn`, set from a client edge bit).
2. **While locked, facing continuously tracks the locked target** — ground and air, overriding ADR-0017's sticky air facing. Movement stays camera-relative WASD; only facing is affected. Attacks auto-aim because facing is already on target (per-stage `UseTargetLock` becomes redundant while locked, not required).
3. **Target resolution reuses the existing resolver**: screen-center preference (player aims the camera at the enemy, hits RMB → locked), nearest-enemy fallback. Lock disengages when the target is out of **lock range (10m)** or dead (resolver re-picks/drops automatically). `LockOn` resets on death/respawn.
4. **Turn rate is gradual**, not instant: a global lock turn strength (~0.4, same lerp form as `TrackingStrength * TickDt`), tuned in playtest. Instant pivot reads as robotic.
5. **`LMB` (facing snap, ADR-0017) while locked exits the lock** — the manual-facing button is the natural "break free". LMB is otherwise unchanged.
6. **Trade-off is the feature**: while locked there are no back-airs, cross-ups, or spacing reads (facing never leaves the target). Competitive keyboard play simply does not toggle it — the ADR-0016 floor is untouched.

## Considered Options

- **Hold-to-lock (RMB held)** — rejected: toggle avoids grip fatigue and reads better as a persistent mode; the wire bit is the same cost.
- **Always-on persistent lock (no toggle)** — rejected: removes the manual-facing game entirely; the toggle is what makes the trade-off a choice.
- **LMB ignored while locked** — rejected: exiting with the manual button is the intuitive escape hatch and costs nothing.
- **Lock = movement-relative strafing** — rejected, out of scope: movement stays camera-relative; facing is the only locked axis.

## Consequences

- **Casual onboarding**: auto-aim attacks, warp cones always pass (facing is on target), no facing management. The manual system remains for players who want it.
- **No conflict with the other engine tickets**: #124 (IASA) and #125 (landing lag) are attack-timing seams, #126 (LMB snap / sticky air facing) is the unlocked-mode rule set — the lock overrides facing only.
- **Wire:** `InputState` gains a toggle-edge bit (flags2 has room, 20 B unchanged); `CharacterState` gains `LockOn`; `CharacterStatePacket` carries it for the client indicator (packet grows by 1 B flag).
- **Kits still using the RMB slot** (Manki/Kistu/Nilus until their normals passes, #121-123) lose the input when this lands — accepted, consistent with the 8-slot re-tier (same as LMB in #126).
- **Goldens:** new scenarios pin lock-on tracking, out-of-range disengage, LMB-exits-lock, death re-target; existing goldens unaffected (default off).
- **Turn strength + lock range are the feel-critical constants** — playtest targets.

## Implementation notes (ticket #127, 2026-08-12)

- Client: `InputController.Poll()` drops the `mouse.rightButton → AbilitySlots.Rmb` branch and sets the toggle-edge bit on press. The screen-center target selection already exists and keeps flowing.
- Sim: `ProcessTargetLock` gains a lock branch — when `LockOn` and the resolved target is valid and within lock range, lerp facing toward it every tick (not only inside attack stages). Out of range or missing target → clear `LockOn`.
- Constants: lock range (10m) and turn strength (~0.4) at the top of the lock branch, playtest-tuned.
- Playtest: toggle lock, fight; confirm auto-aim feel, turn rate, disengage by running, LMB-break.
