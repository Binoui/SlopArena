# ADR-0001: FallRamp progressive gravity system

**Status:** Superseded in part — 2026-08-13 by [ADR-0020 §3](0020-melee-based-movement.md) (gravity): the three-phase ramp (FloatWindow → FallRamp → FullGravity) is replaced by **float-window-only** gravity — the ramp machinery (`FallRampDuration` lerp) is deleted; the float window survives only for recovery/post-hit states (ADR-0020 §3, Option A). ADR-0002 (jump-arc anim) untouched.

Context: DKO-inspired platform fighter feel requires aerial recovery loops (chain attacks to stay floaty), smoother air combat, and progressive fall speed rather than the current binary on/off gravity with a constant-velocity fall threshold. Change affects `ApplyGravity()` in Simulation.cs and adds `AirTimeTicks`, `FloatWindowTicks`, `FallRampDuration` to CharacterState/CharacterDefinition.

Decision: Replace the current gravity model (binary AirFloatGravity vs Gravity with VY > -3 cutoff) with a three-phase progressive ramp: FloatWindow (reduced gravity while AirTime is fresh) → FallRamp (linear increase to full gravity) → FullGravity. AirTime resets on any action (attack, dash, jump), taking damage, or landing.

## Considered Options

- **Current system (keep):** Constant AirFloatGravity during attacks, VY > -3 cutout for free fall. Simple but no recovery loops, feels flat in the air.
- **Always-full-gravity:** Gravity always applies at full force. Simplest code, punishes air play heavily, no float window at all.
- **FallRamp (chosen):** Three-phase progressive gravity. More complex but enables the DKO-style aerial recovery rhythm — attack to reset AirTime, extend your float window, chain actions back to stage.

Manki: FloatWindowTicks=5, FallRampDuration=12. FightGuy: FloatWindowTicks=4, FallRampDuration=10. AirTime resets on any action (attack, dash, jump), taking damage, or landing. FloatWindow is intentionally short (~80ms) so jumps feel snappy — the float is only meaningful when chaining aerial attacks.

## Consequences

- Characters with lower `AirFloatGravity` or longer `FloatWindowTicks` have stronger recovery (design lever for character identity)
- The VY > -3 cutoff is removed — conflicts with progressive ramp
- ServerAbility.Tick() can still override VY per-tick (runs after gravity), so special abilities bypass the ramp
- Client prediction needs matching logic for AirTime tracking (already has ApplyGravity path via Simulation.SimulateTick)
