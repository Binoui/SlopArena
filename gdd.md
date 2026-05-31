# SlopArena — Game Design Document

## 1. Core Concept

SlopArena is a **third-person arena brawler** where 2-8 players fight in compact, low-verticality arenas using pre-assembled character kits. The game emphasizes positioning, resource management (cooldowns), and mechanical execution over aim precision or gear progression.

Each character has a defined role and a fixed kit of 6 abilities:
- **Light Attack** — 3-hit melee chain with a finisher
- **Heavy Attack** — Higher-damage special attack with longer cooldown
- **Ability 1, 2, 3** — Signature abilities forming the core playstyle
- **Ultimate** — High-impact ability with a long cooldown

---

## 2. Design Pillars

### 2.1 PvP-First, Zero Friction

- No PvE, no matchmaking queues, no battle passes
- Drop-in/drop-out FFA deathmatch servers
- 1v1 / 2v2 arena sockets for coordinated play

### 2.2 Small Kits, High Skill Ceiling

- Every ability must have clear counterplay (telegraphed, dodgeable, or punishable)
- Cooldowns are the primary resource — no mana, no ammo
- Light attack combos reward mechanical execution (chain timing, directional reads)

### 2.3 Physics-Driven Combat

- Hitstun and knockback create openings for combos
- Directional Influence (DI) lets the defender influence their trajectory during knockback, making combos an interactive exchange rather than a scripted sequence
- Weight stat determines knockback resistance — heavier characters are harder to launch

---

## 3. Character Roles

The roster is divided into five roles, each with a distinct stat profile and playstyle:

### Brawler (Colossus)
- **Fantasy:** Melee juggernaut who controls the pace of engagement
- **Strengths:** Durability, crowd control (grapple, shield bash), teamfight presence
- **Weaknesses:** Moderate speed, predictable approach, vulnerable when abilities are down
- **Signature:** Shield Bash (armored startup), Grapple (pull into melee range)

### Ranger (Marksman)
- **Fantasy:** Precision ranged fighter who controls space
- **Strengths:** Long-range poke, zone denial (traps), kite potential
- **Weaknesses:** Low HP, poor close-range options, relies on spacing
- **Signature:** Snare Trap (root on trigger), Arrow Volley (spread pressure)

### Assassin (Wraith)
- **Fantasy:** Elusive burst damage that strikes from unexpected angles
- **Strengths:** Highest speed, teleport (Shadow Step), stealth, execute ultimate
- **Weaknesses:** Lowest HP, fragile, punishable if caught
- **Signature:** Shadow Step (teleport), Death Mark (delayed detonation, CD reset on kill)

### Tank (Titan)
- **Fantasy:** Immovable force that disrupts and protects
- **Strengths:** Highest HP, knockback immunity (Fortify), wall-creating ultimate
- **Weaknesses:** Slowest speed, predictable, limited ranged options
- **Signature:** Charge (grab + wall slam), Arena (trapping walls)

### Mage (Sol)
- **Fantasy:** Glass cannon that controls space with area denial
- **Strengths:** High damage output, long-range beam (Sunbeam), powerful AoE ultimate
- **Weaknesses:** Low HP, light weight (launched easily), telegraphed projectiles
- **Signature:** Solar Orb (slow, high-damage projectile), Sunburst (AoE from above)

---

## 4. Combat System

### 4.1 Light Attack Chain

Every character has a 3-hit light attack combo:
- **Hit 1-2:** Low knockback, short hitstun. Fast recovery.
- **Hit 3 (Finisher):** Higher knockback multiplier, longer hitstun. Slower recovery.
- **Chain Window:** Time window to chain the next hit. Miss it, the combo resets.

Chain windows vary by character (Titan: 0.5s generous, Wraith: 0.3s tight).

### 4.2 Knockback & DI

- Knockback is a physics impulse (force + upward component)
- Negative knockback force = pull toward the attacker (Grapple)
- The defender can influence their trajectory while airborne using WASD (Directional Influence)
- Weight stat scales knockback resistance linearly

### 4.3 Hit Detection Shapes

| Shape | Behavior | Example |
|-------|----------|---------|
| MeleeCone | Frontal cone from the attacker | Shield Bash, Haymaker |
| Projectile | Traveling hitbox with speed and lifetime | Solar Orb, Arrow Volley |
| CircleAoE | Radial blast from a center point | Colossus Slam, Supernova |
| Beam | Hitscan line from caster to max range | Sunbeam |
| SelfBuff | No hitbox — applies to self | War Cry, Fortify |

### 4.4 Status Effects

Statuses are applied via `FGameplayTag` and affect combat outcomes:
- **Burn** — Damage over time
- **Stun** — Cannot act
- **Slow** — Reduced movement speed
- **Poison** — Damage over time (stacking)
- **Root** — Cannot move (can still attack)

---

## 5. Movement

### 5.1 Core Movement
- Ground speed varies by character (480-700)
- Jump force varies by character (500-650)
- Air control is intentional but limited — you commit to your jump trajectory

### 5.2 Dash
- Universal movement ability on cooldown
- Quick directional burst, maintains momentum on landing
- Can be chained into attacks or slides

### 5.3 Hitstun
- Landing an attack applies a brief window where the target cannot act
- Diminishing returns on repeated hits to prevent infinite stuns
- Knockback scales up as combo length increases (natural combo breaker)

---

## 6. Game Modes

### FFA Deathmatch (Primary)
- Continuous drop-in/drop-out arena
- Kill scoring, immediate respawn
- Kill grants partial HP recovery to reward aggression

### Arena Sockets 1v1 / 2v2 (Secondary)
- Round-based format
- Tight arena boundaries
- Same mechanics, coordinated team play

---

## 7. Technical Architecture

### Engine & Language
- **Unreal Engine 5.7** — C++ project
- **GAS (Gameplay Ability System)** — Ability activation, effects, attributes
- **Server-authoritative** — All combat decisions are validated server-side

### Data Flow
- Registry holds character definitions (compiled-in table, migratable to DataAssets)
- GameMode selects roster and manages spawns
- Character pawn holds runtime state (HP via GAS AttributeSet)
- Abilities read from character definition at spawn time

### Current Status
- Character data: Complete (5 characters, all stats defined)
- Pawn/Movement: Skeleton implementation
- GAS abilities: Stub implementations (real effects pending)
- AI: Bot controller with basic decision-making
- Networking: Placeholder state types, not yet wired
