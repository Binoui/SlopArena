# Hitstun + DI System

Smash Bros-style hitstun with Directional Influence (DI) for competitive depth.

## How It Works

### Hit Sequence
```
Attack lands → Hitstun (8-60 frames) → Knockback (influenced by DI)
```

**1. Hitstun Phase** (ActionState.Hitstun)
- Victim is **frozen in place** (VX = VZ = 0)
- Duration: 8-60 frames based on knockback strength
- **Gate:** Controlled by `StunTicks > 0` from the hitbox data (not an arbitrary KB threshold)
  - `StunTicks = 0`: No hitstun (true weak jab -- instant knockback recovery)
  - `StunTicks > 0`: Hitstun triggers if knockback magnitude > 0.5 (floor to prevent glitchy near-zero push)
- **Maximum** hitstun is `min(computedFromKB, StunTicks)` -- StunTicks acts as a per-move cap
- **Examples at default settings:**
  - Base 1.5 + Growth 2.5 (combo jab): ~8-10 frames
  - Base 7 + Growth 18 (launcher at 100%): ~25-40 frames
  - Kill moves at high %: up to 60 frames (1 second)
- **Visual:** White flash on victim

**2. DI Window**
- During hitstun, player can **hold a direction** (WASD)
- The **held direction at the END** of hitstun modifies knockback
- NOT accumulated (no button mashing needed)
- Just hold the stick where you want to go

**3. Knockback Application**
- Effective horizontal: `effectiveHorizontal = baseKB + growthKB * (dmg% * 0.01)`
- Effective upward: `effectiveUpward = kbUpward * (1 + dmg% * 0.01)`
- DI influence applied: `KBX += DIX * 3.5`, `KBZ += DIY * 3.5`
- Victim enters knockback state

## DI Strategy

### Survival DI
**Goal:** Live longer at high %

- **Knocked horizontally:** Hold UP (increase vertical angle)
- **Knocked diagonally up:** Hold AWAY from stage center
- **Knocked straight up:** Hold horizontal (spread out trajectory)

**Why it works:** DI perpendicular to knockback maximizes distance from blast zone.

### Combo DI
**Goal:** Escape combos at low %

- **Upward launcher:** Hold DOWN (reduce height, faster landing)
- **Horizontal hit:** Hold UP (pop out of combo range)
- **Multi-hit moves:** Alternate DI each hit (SDI-like escape)

### No DI
- **At ledge, high %:** Sometimes best to NOT DI (opponent expects it)
- **Tech situations:** Save inputs for tech timing

## Technical Details

### Code Flow

**CharacterState.cs:**
```csharp
public ushort HitstunTicks;  // Remaining freeze frames
public float DIX, DIY;       // Held direction (updated each frame)
```

**Simulation.cs ProcessHitstun():**
```csharp
// Each frame during hitstun:
s.VX = 0; s.VZ = 0;          // Freeze victim
s.DIX = input.MoveX;         // Store held direction
s.DIY = input.MoveY;

// Last frame of hitstun:
s.KVX += s.DIX * 3.5f;       // Apply DI to knockback
s.KVZ += s.DIY * 3.5f;
s.State = ActionState.Idle;  // Exit to knockback
```

**Simulation.cs ApplyKnockback():**
```csharp
// New Smash-style formula: effectiveHorizontal = baseKB + growthKB * (dmg% * 0.01)
float scaling = 1f + (s.DamagePercent * 0.01f);
float effectiveHorizontal = baseKB + growthKB * (s.DamagePercent * 0.01f);
float effectiveUpward = kbUpward * scaling;

s.KVX = dirX * effectiveHorizontal;
s.KVY = effectiveUpward;
s.KVZ = dirZ * effectiveHorizontal;

float kbMagnitude = sqrt(KVX² + KVY² + KVZ²);

// Gate: hitstun triggers on StunTicks > 0 (not an arbitrary KB threshold)
// The 0.5f floor prevents glitchy near-zero push with hitstun
if (stunTicks > 0 && kbMagnitude > 0.5f)
{
    ushort hitstunFromKB = (ushort)clamp(8 + (int)(kbMagnitude * 0.5f), 8, 60);
    ushort hitstunFinal = min(hitstunFromKB, stunTicks); // cap at stage's StunTicks
    s.HitstunTicks = hitstunFinal;
    s.State = ActionState.Hitstun;  // Enter freeze
}
else
{
    s.State = ActionState.Idle;    // Skip hitstun
}
```

### Constants (Tweakable)

**Simulation.cs:**
```csharp
const float DIStrength = 3.5f;           // DI multiplier (line 157)
const float MinHitstun = 8f;             // Min frames (line 475)
const float MaxHitstun = 60f;            // Max frames (1 second, was 25)
const float HitstunScaling = 0.5f;       // KB → frames conversion (line 475)
```

> **Hitstun gate removed:** `HitstunThreshold = 3f` is gone. Hitstun now gates on `StunTicks > 0` from the hitbox data (see Hitstun Phase above). Set `StunTicks = 0` on a hitbox to skip hitstun entirely.

**To increase DI influence:** Raise `DIStrength` (default 3.5)
**To make hitstun longer:** Raise `HitstunScaling` or `MaxHitstun`
**To make weak hits have hitstun:** Increase `StunTicks` on the hitbox data
## Balancing

### Current Values (v1.1)
- **DIStrength:** 3.5 (strong influence, rewards good DI)
- **Hitstun range:** 8-60 frames (0.13-1.0 seconds at 60Hz)
- **Hitstun gate:** `StunTicks > 0` from hitbox data (designer-controlled per move)
- **Knockback decay:** 2% per tick (was 13.3% -- flight lasts ~1s instead of 0.25s)
- **Manki LMB example (stage 1/2/3):**
  - S1: Base 1.5 + Growth 2.5, Up 1, Stun 10 -- combo starter, gentle at all %
  - S2: Base 3 + Growth 5, Up 1.5, Stun 14 -- moderate pop, links into S3
  - S3: Base 7 + Growth 18, Up 6, Stun 22 -- launcher, ~37 m/s at 150%

### Competitive Considerations

**If DI feels too weak:**
- Increase `DIStrength` to 4.5-5.0
- Players can't escape kill moves

**If DI feels too strong:**
- Decrease `DIStrength` to 2.5-3.0
- Players survive everything

**If hitstun feels unresponsive:**
- Decrease `MinHitstun` to 5-6 frames
- Faster reaction window

**If combos are too easy:**
- Increase `MinHitstun` to 10-12 frames
- Longer DI window = easier escapes

## Visual Feedback

**During Hitstun:**
- White flash on victim (PlayerController.cs line 528-543)
- Victim frozen in place
- **Duration indicator:** Flash brightness = remaining hitstun

**Future Enhancements:**
- Input display during hitstun (show held direction)
- DI trajectory preview (arrow showing modified angle)
- Hitstun sound effect (metal clang)
## Testing

**Test scenarios:**

1. **Weak jab spam (StunTicks = 0):**
   - Should NOT freeze victim (instant knockback recovery)
   - Fast, responsive combos

2. **Strong smash attack at 100%:**
   - 15-20 frame freeze
   - Victim can DI perpendicular to survive

3. **Mid % combo:**
   - 8-12 frame hitstun
   - Attacker can react, victim can DI out

4. **DI survival test:**
   - Hit at 150% near ledge
   - Good DI = survive, no DI = death

## Comparison to Smash Bros

**Similarities:**
- ✅ Hitstun scales with knockback
- ✅ DI modifies trajectory
- ✅ Perpendicular DI is optimal
- ✅ Visual freeze on strong hits

**Differences:**
- ❌ No SDI (Smash DI) - single DI input only
- ❌ No ASDI (Automatic SDI) - input must be during hitstun
- ❌ Simpler formula (Smash has complex frame data per move)

**Design choice:** Keep it simple but deep. One DI input per hit is easier to learn but still rewards skill.

## FAQ

**Q: Can I mash during hitstun to get out faster?**
A: No. Hitstun duration is fixed. Use the time to input DI.

**Q: Does DI work on grabs/throws?**
A: Not yet implemented. Future feature.

**Q: Can I DI while in knockback?**
A: No. DI only works during the hitstun freeze window.

**Q: What if I input DI too early (before hit)?**
A: Doesn't matter. System reads held direction at END of hitstun.

**Q: Does DI affect damage taken?**
A: No. DI only affects knockback trajectory, not damage.


## Animation Tiers

Hitstun has 3 animation tiers based on the damage of the attack that hits, providing visual feedback proportional to impact strength:

| Tier | Damage Range | `HitstunLevel` | Anim Clip       | Trigger          |
|------|-------------|----------------|-----------------|------------------|
| Light  | < 5        | 0              | `hit_light`     | `HitstunSmall`   |
| Medium | 5–14       | 1              | `hit_medium`    | `HitstunMedium`  |
| Hard   | ≥ 15       | 2              | `hit_hard`      | `HitstunHard`    |

### How It Works

1. **Server-side computation:** When a hit lands in `ServerSimulation.ResolveHits()`, `HitstunLevel` is computed from the raw damage of the hitbox event (before any damage modifiers):
   ```csharp
   targetState.HitstunLevel = finalDamage < 5f ? (byte)0 :
                               finalDamage < 15f ? (byte)1 : (byte)2;
   ```
2. **Set once:** The level is set at hit time, not re-derived from `DamagePercent`. This prevents flicker if another hit lands during hitstun.
3. **Serialized:** Packed into `CharacterStatePacket` at byte offset 43 (Size = 44). The client receives it every tick during hitstun.
4. **Client renderer:** `PlayerRenderer` maps `HitstunLevel` to the corresponding animator trigger:
   - Level 0 → `HitstunSmall`, Level 1 → `HitstunMedium`, Level 2 → `HitstunHard`
5. **Server bone resolution:** Both bone-animation spots in `ServerSimulation` use the same switch to pick the clip name for hurtbox alignment.

### CharacterDefinition Defaults

```csharp
public string HitSmallAnim = "hit_light";   // Clip name in GLB
public string HitMediumAnim = "hit_medium";
public string HitHardAnim = "hit_hard";
```

These map to the actual FBX clip names. Per-character overrides go in `CharacterDefinition.ClipOverrides`.

### Animator Layer

The animator controller has 3 independent AnyState transitions, one per tier. All are `AutoExit = true`.
```
AnyState ──HitstunSmall──→ HitstunSmallState (hit_light)  AutoExit
AnyState ──HitstunMedium─→ HitstunMediumState (hit_medium) AutoExit
AnyState ──HitstunHard───→ HitstunHardState (hit_hard)     AutoExit
```

### Configuration Assets

`CharacterAnimationConfig` ScriptableObject exposes:
- `HitSmall` — clip for light hits (< 5 damage)
- `HitMedium` — clip for medium hits (5–14 damage)
- `HitHard` — clip for hard hits (≥ 15 damage)

The animator generator (`SlopArenaAnimatorGenerator.AssignClip`) maps these names:
- `hit_light`, `hit_small` → `HitSmall` (aliases)
- `hit_medium` → `HitMedium`
- `hit_hard`, `hit_large` → `HitHard` (backward alias for `hit_large`)

### Testing

See `tests/Shared.Tests/HitstunAnimationTierTests.cs` for:
- Boundary tests for damage→level computation
- End-to-end LMB S1 pipeline (damage=4 → level 0), Q projectile (damage≥6 → level 1)

## References

- **CharacterState.cs:** Line 47-48 (HitstunTicks, DIX, DIY), Line 102 (HitstunLevel)
- **ActionState.cs:** Line 7 (Hitstun enum)
- **Simulation.cs:** Line 61-67 (ProcessHitstun call), Line 141-176 (ProcessHitstun function), Line 462-495 (ApplyKnockback with hitstun)
- **CharacterStatePacket.cs:** Offset 43 (HitstunLevel), Size 44
- **PlayerController.cs:** Line 526-547 (Visual white flash)
- **PlayerRenderer.cs:** Lines 288-297 (HitstunLevel switch → trigger)
- **HitstunAnimationTierTests.cs:** Full unit + integration coverage
