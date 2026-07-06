# Combat Systems

> Universal combat mechanics for SlopArena — attack patterns, aerial rules, targeting, and aiming.
> These apply to **every** character. Per-character kit design is in `character-kit-design-principles.md`.

---

## 1. Basic Attacks

### LMB — Light Attack Combo

- Generally a **3–4 hit combo** (varies per character)
- Combo uses per-stage **DurationTicks** (total animation lock). Input during the attack is buffered via `BufferedSlot` and auto-consumed when the current stage ends.
- The same-slot buffer works during the **entire attack**, not just a post-attack window. Additional clicks are captured by the `InputBufferWindow` general buffer (6 frames).
- Attack data (damage, knockback, radius, stun) lives in each **HitboxEvent** within the stage, not on AttackStage itself
- Adds a natural knockdown or knockback on the final hit, creating a reset
- Range: ~10 units
- Aim assist: most attacks have **directional snap** to help connect in 3D space


### RMB — Heavy Attack (Charge Variant)

- Uses a **two-phase hold-to-charge** lifecycle via `ServerAbility`:
  - **Phase 1 (charge hold)**: Player holds RMB, character enters charge pose animation.
    Server accumulates `_chargeTicks` each tick while `input.IsAiming=true`.
  - **Phase 2 (release-to-attack)**: On button release (`input.IsAiming=false`), fires the attack.
    If charge time >= threshold (45 ticks = 0.75s), fire **charged** variant (more damage, larger range/radius).
    Otherwise, fire **normal** variant.
- NPC path: instant skip — fires normal immediately (no charge delay)
- Auto-release: forced charged at max hold (120 ticks = 2s)
- Cooldown: 30 ticks, starts after attack phase ends
- Range: ~6 units (normal), ~8 units (charged)
- Damage: ~8 (normal), ~14 (charged)
- Aim assist: yes

### Air LMB

- **1 or 2 hits** depending on the character
- Some characters can **dash cancel** out of Air LMB or use it as a movement tool
- Can only be used **once per flight** (cannot chain air attacks infinitely)

### Air RMB

- **Single hit**
- Stronger knockback than Air LMB
- Can only be used **once per flight**

---


## 2. Aerial Ability Usage

### One Use Per Flight

Every ability slot can be used in the air at most **once per flight**. Once you land or touch the ground, your air-use counters reset.

In practice:
- **1 Air LMB** per flight
- **1 Air RMB** per flight
- **1 Air Dash** (mobility spell) per flight
- **1 Air Q / E / R / F** each per flight

This prevents infinite air camping and forces the player to touch ground for a resource reset.

### Not All Spells Work in the Air

- Having **all** your spells usable in the air is a significant advantage that must be balanced
- A character with strong air game should be weaker on the ground, or vice versa
- Spells usable in the air are typically weaker or have longer cooldowns as a trade-off

---

## 3. Movement Abilities

Movement abilities (dash, leap, teleport, sprint) can vary on these axes:

| Property | Description |
|----------|-------------|
| **Invincibility** | Some dashes grant i-frames, some don't |
| **Hitbox** | Some movement abilities deal damage (Phoenix Dive, Sonic Spin), some are pure repositioning |
| **Air charge reset** | Some movement abilities refund an air-use slot (e.g., a dash that resets Air LMB charge) |

---

## 4. Projectile Aiming Types

3D aiming is hard. SlopArena supports multiple targeting methods:

### Type A — Aim at Crosshair (Retical Lock)

- Projectile fires toward the player's camera crosshair
- Standard for most projectiles
- Simple, intuitive, works in both ground and air

### Type B — Ground Indicator (Free-aim Arc)

+ Shows a **bell-shaped arc indicator** on the ground
+ Player selects a landing point freely (like a bow shot in a TPS)
+ Good for area-denial and lobbed projectiles
+ The indicator is free-moving, not tied to the character's position

#### Manki Q Implementation

Manki's "Round Bomb" uses Type B aiming with a three-phase animation pipeline:

1. **spell_q_start** (AnimIndex=0) — 8-tick minimum start animation
2. **spell_q_loop** (AnimIndex=1) — loops while Q is held, indicator active
3. **spell_q_end** (AnimIndex=2) — throw animation, projectile spawns at tick 10

**Flow**:
+ Press Q → ability activates via ServerAbility (MankiRoundBomb)
+ Client shows orange ground ring + parabolic arc via AimIndicator component
+ Mouse cursor unlocked during aiming (Cursor.lockState = None), drives ring position
+ Raycast from mouse to Y=0 ground plane (with height-filtered physics raycast)
+ On Q release: `input.IsAiming` becomes false → server transitions to throw phase
+ Aim distance + yaw cached in private fields (`_cachedAimDistance`, `_cachedAimYaw`) at transition
+ Projectile spawns using cached values (prevents overwrite by SimulateTick)
+ Parabolic trajectory computed via `CombatMath.ComputeProjectileLaunch()`

**Client components**:
+ `AimIndicator.cs` — MonoBehaviour on TrainingMatch, creates procedural ground ring + LineRenderer arc
+ `AimIndicator.SetAbilityParams()` — receives gravity/launchAngle/launchOffsetY from MankiData spec
+ `CameraMount.SetCameraYawDeg()` / `SetCameraPitchDeg()` — camera locked during aiming
+ `InputController.IsQKeyHeld` — tracks held state (set in Poll(), checked in FixedUpdate)

**Cooldown**:
+ Server-side: PreTickAbilities checks `GetCooldown(state, 3)` → `Cooldown2`
+ Client-side: SimulateTick checks cooldown before entering Attacking state
+ `_states[id] = state` persists cooldown struct write-back (CharacterState is a value type)
+ Cooldown decremented in TickTimers (lines 352-357)
### Type C — Character-Relative Zone Indicator

- An indicator zone appears **linked to the character's position**
- The player moves the indicator **horizontally** with the camera before confirming
- Common for ground-targeted AOEs (rings, circles, lines)
- Stays at a fixed distance from the character; only the horizontal angle changes with camera

---

## 5. Aiming While Airborne

### Momentum Stop

When a spell requires aiming (Type B or Type C), it's good practice to **stop the character's airborne momentum** while aiming.

This lets the player aim precisely without drift — it also effectively "stalls" the character in the air, creating a floaty hover state. This means:

- **Aimable spells usable in the air also double as air stall abilities**
- Player stops drifting → aims → fires → momentum resumes
- Gives the character an optional hover / float for spacing

---

## 6. Hitbox Philosophy

### Be Generous

Landing attacks in a 3D environment is inherently harder than in 2D. Hitboxes should be **generous** — wider, taller, or longer than the visual suggests.

This is a deliberate design choice:
- Missed attacks feel frustrating in 3D
- Generous hitboxes create satisfying "that should have hit" moments turning into actual connections
- It compensates for depth perception issues on a flat screen

### Aim Assist

Most attacks have **directional snap** — a small angle correction that nudges the attack toward the nearest enemy in range.
- LMB and RMB: ~5–10° snap toward nearest valid target
- Projectiles: some are pure skillshots, some have homing properties
- The snap is applied at **cast time only** — tracked projectiles follow their target

---

## 7. Finisher Spells

### Wind-up Before Hitbox

Finisher spells (high-damage, telegraphed abilities) should have a **noticeable wind-up animation** before the hitbox activates.

This gives the opponent time to **dash in reaction**, creating the core **dash mindgame**:

> "My opponent used their dash → I can safely land my big finisher"
> "My opponent is holding their dash for my finisher → I can fake it or close the gap first"

This is the primary risk-reward loop of finisher abilities:
1. Finisher has long wind-up (0.3–0.6s visual tell)
2. Opponent can dash to dodge during the wind-up
3. If they already burned their dash, they must eat the hit
4. If they didn't, the attacker must bait the dash before committing

### Why This Works

- Creates visible, readable counterplay
- Rewards the defender for **watching the opponent**, not just their own character
- Rewards the attacker for **tracking cooldowns** (did they use their dash?)
- Adds tension without requiring complex inputs

---

## 8. Cross-References

- `character-kit-design-principles.md` — per-character kit design, keybindings, archetypes
- `conventions.md` — animation naming, bone naming, file structure
- `research/dko-mechanics.md` — full DKO systems reference (clash, interrupt, super armor, etc.)

---

## 9. Soft-Lock Targeting System

SlopArena uses a **soft-lock targeting system** that auto-selects the nearest enemy close to screen center. The target is used for warp, camera tracking, and can be queried by abilities.

### 9a. Target Acquisition

**Client-side** — `TrainingMatch.PickScreenTarget()` runs every fixed-update tick:
1. Projects all enemy renderers to screen space via `UnityEngine.Camera.WorldToScreenPoint()`
2. Filters out targets behind the camera (`screenPos.z < 0`)
3. Selects the enemy closest to screen center within 20m world distance
4. Result: entity ID (byte, 0 = none) passed into `InputState.TargetEntityId`

**Server-side** — `ServerSimulation.ProcessTargetLock()` runs during each tick:
1. If `InputState.TargetEntityId > 0` (client-provided screen-center target): uses that ID directly, verifying it's within search range
2. Otherwise: falls back to `FindClosestEnemy()` brute-force nearest scan
3. Stores result in `CharacterState.TargetEntityId` for abilities to query
4. Clears to 0 at the start of each entity's processing (no stale targets)

### 9b. Packet Protocol

`InputState` grew from 16 → 17 bytes:

| Offset | Type   | Field           | Notes                              |
|--------|--------|-----------------|------------------------------------|
| 0-3    | float  | MoveX           | Horizontal analog input            |
| 4-7    | float  | MoveY           | Vertical analog input              |
| 8      | byte   | flags           | 8 boolean buttons (bitfield)       |
| 9      | byte   | ActiveSlot      | 1-6 for ability slots              |
| 10-11  | short  | FacingYaw       | Degrees × 100                      |
| 12-13  | short  | AimYaw          | Aim yaw (overrides FacingYaw)      |
| 14-15  | ushort | AimDistance     | Aim distance in cm (0-6500 = 0-65m)|
| 16     | byte   | TargetEntityId  | Client-selected target (0 = none)  |

Deserialize is backward-compatible: checks `buf.Length >= 17` before reading offset 16.

### 9c. Server-Side Behavior

- **Warp**: if entity is within `WarpRange` but outside `AttackRange`, auto-dashes toward target (`WarpSpeed = 0.3` = 30% of *remaining* distance per tick, computed as `V = dx * WarpSpeed / TickDt` for exponential convergence)
- **Persistence**: `CharacterState.TargetEntityId` is set each tick while attacking with `UseTargetLock` enabled; zero otherwise

### 9d. Client-Side Visualization

| Component | File | Behavior |
|-----------|------|----------|
| TargetIndicator | `Combat/TargetIndicator.cs` | Red semi-transparent ring under targeted enemy. Created as flat cylinder (Sprite/Default shader), follows target position every frame. Hidden when target = 0. |
| Camera rotation | `TrainingMatch.Update()` | Calls `CameraMount.LerpTowardDirection()` at 90°/s to smoothly rotate camera yaw toward target. Camera stays behind character (CinemachineOrbitalFollow handles that); yaw adjusts to keep target on screen. |

### 9e. Edge Cases

- **No enemies in range**: TargetEntityId = 0 → no ring, no camera rotation, no warp. All existing behavior unchanged.
- **Target dies/respawns**: ProcessTargetLock clears/recomputes each tick, so a respawned entity is immediately re-targetable.
- **Multiple targets at equal distance**: screen-center proximity breaks ties (client) / first-found wins (server fallback).
- **Older clients**: Deserialize ignores missing TargetEntityId byte (checks length) — defaults to 0, server falls back to nearest scan.
