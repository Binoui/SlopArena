# Combat Systems

> Universal combat mechanics for SlopArena — attack patterns, aerial rules, targeting, and aiming.
> These apply to **every** character. Per-character kit design is in `character-kit-design-principles.md`.

---

## 1. Basic Attacks

### LMB — Light Attack Combo

- Generally a **3–4 hit combo** (varies per character)
- Combo uses a **queue-based input buffer** (max 2 buffered inputs, like souls-like FSM) for responsive chaining
- Chain window opens when the animation lock expires — input during the buffer zone is stored and consumed when ready
- Adds a natural knockdown or knockback on the final hit, creating a reset
- Range: ~10 units
- Aim assist: most attacks have **directional snap** to help connect in 3D space

### RMB — Heavy Attack

- Can be **charged or uncharged** depending on the character
  - Uncharged: ~10 damage
  - Charged: ~15 damage
- Range: ~10 units
- Slower startup than LMB, stronger knockback
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

- Shows a **bell-shaped arc indicator** on the ground
- Player selects a landing point freely (like a bow shot in a TPS)
- Good for area-denial and lobbed projectiles
- The indicator is free-moving, not tied to the character's position

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
