# CLAUDE.md — SlopArena Project

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them — don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it — don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## 5. Netcode-Ready Code Rules

SlopArena uses a server-authoritative 60Hz UDP model with client-side prediction + reconciliation.
Every line of gameplay code must be written to work deterministically on both client and server.

### 5a. Shared/ is Sacred

`Shared/` is a pure C# library with **zero** Godot dependencies. This is enforced by `SlopArena.Shared.csproj` which does not reference any Godot packages. Rules:
- No `Godot.` imports in any Shared/ file — ever
- All math uses `System.MathF`, `System.Math` (not Godot's `Mathf`)
- All data types use plain C# structs (not Godot's `Vector3`, `Transform3D`, etc.)
- All hit detection, knockback, physics simulation goes in Shared/
- If a function computes a gameplay result (damage, knockback, position), it takes primitives and returns primitives

### 5b. Tick-Based, Not Delta-Based

All cooldowns, stun durations, cast times, and state durations are measured in **ticks** (1 tick = 16.6ms at 60Hz):
```csharp
// GOOD: tick-based
ushort dashCooldownTicks = 90; // 1.5 seconds
if (dashCooldownTicks > 0) dashCooldownTicks--;

// BAD: float seconds * delta (framerate-dependent)
float dashCooldown = 1.5f;
dashCooldown -= delta;
```

Use `ushort` for tick counts (max 65535 ticks = ~18 minutes, enough for any cooldown).

### 5c. No Godot Physics Queries on Server

Server-side hit detection NEVER uses `GetWorld3D().DirectSpaceState.IntersectRay()` or any Godot physics node.
All hit detection is done via `CombatMath.cs` and `SpellResolver.cs` in Shared/ using pure math.

The client CAN use Godot physics for rendering/prediction, but the server is the authority with pure math.

### 5d. Deterministic Float Math

- Floats are OK (this is not lockstep, it's server-authoritative CSP)
- Avoid comparing floats with `==` — use `<= 0` or `> radius * radius` patterns
- `Math.Clamp()` to avoid NaN from acos/asin with near-1.0 inputs

### 5e. Packet Serialization

- All network packets use `System.Buffers.Binary.BinaryPrimitives` for explicit little-endian serialization
- Packet size is a compile-time constant (`Size`)
- No Godot types in packet structs — serialize as primitives (float, int, byte, ushort)
- Client sends `ClientInputPacket` (14 bytes), server responds with `CharacterStatePacket` (31 bytes)

### 5f. Don't Mix Rendering with Logic

- Visual effects (particles, sounds, animations) are client-only
- Game state (position, health, cooldowns, action states) is computed in Shared/ and authoritative on server
- Client predicts forward from last server state, server reconciles

### 5g. Architecture Summary

```
┌─────────────────────┐      UDP       ┌─────────────────────┐
│   Godot Client       │ ◄──────────►   │   .NET Server       │
│   Scripts/           │                │   Server/            │
│   ┌───────────────┐  │  InputPacket  │   ┌───────────────┐  │
│   │ Prediction    │  │   Character   │   │ SimulateTick  │  │
│   │ + Rendering   │  │   StatePacket │   │ (authority)   │  │
│   └───────┬───────┘  │                │   └───────┬───────┘  │
│           │          │                │           │          │
│   ┌───────▼───────┐  │                │   ┌───────▼───────┐  │
│   │   Shared/     │  │                │   │   Shared/     │  │
│   │ (pure C#)     │  │                │   │ (pure C#)     │  │
│   │ Simulation.cs │  │                │   │ Simulation.cs │  │
│   │ CombatMath.cs │  │                │   │ CombatMath.cs │  │
│   └───────────────┘  │                │   └───────────────┘  │
└─────────────────────┘                  └─────────────────────┘
         │                                        │
         │ CharacterDefinition.cs (data-driven)   │
         │ CharacterState.cs (per-tick state)     │
         │ AttackData.cs (AbilityData/Stage)      │
         └────────────────────────────────────────┘
```

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, netcode integration goes smoothly because gameplay logic was written tick-based from the start, and clarifying questions come before implementation rather than after mistakes.
