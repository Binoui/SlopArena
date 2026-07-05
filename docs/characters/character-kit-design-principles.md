# Character Kit Design Principles

> Design patterns extracted from DKO, Battlerite, Supervive, Fangs, Omega Strikers, Smash.
> These patterns appear in **every** game analyzed — they're the fundamentals of a character kit
> for a 3D platform fighter with abilities.
>
> Use as a reference when designing SlopArena class kits.

---

## The 8 Ability Archetypes

Every ability has a **specific job**. A character kit is a selection from these archetypes.
No character has everything — each gets 4-6 slots distributed among these roles.

### 1. Poke / Projectile
- Ranged attack, directional skillshot
- Use: spacing, zone control, chip damage
- CD: 0.5-3s (or spammable with low damage)
- In DKO: neutral-B equivalent, or LMB for mages

### 2. Mobility / Recovery
- Dash, teleport, sprint boost, grapple, leap
- Use: close the distance (engage) or escape danger (disengage)
- CD: 3-6s (short — players need to use it often for the game to feel dynamic)
- **Very frequent pattern: bound to E**
- Sometimes a double charge with refire (Zeus Surge in DKO)

### 3. Crowd Control / Engage
- Stun, root, slow, silence, knockup, fear, pull
- Use: **initiate a fight** → land the CC → follow up
- CD: 6-10s
- **Frequent pattern: bound to Q**
- In a platform fighter: CC lasts 1-2s max. More = stunlock, less = no punish window.

### 4. Zone / Area Denial
- Persistent AOE, ground trap, wall, smoke, burning ground
- Use: control space, force enemy movement, cut off escape routes
- CD: 8-12s
- Duration: 3-6s (long enough to matter, short enough not to frustrate)
- Examples: Ymir Ice Wall (6s), Sol Fireball burning ground, Thor Tectonic Rift (5s)

### 5. Counter / Parry
- "Trance" (Battlerite), "Protector Guardian" (Athena DKO)
- Use: **defensive read** — if the enemy hits you during the buff, they get punished
- CD: 8-12s
- Active window: 0.5-0.75s
- **Rule**: 1 character per pool max, or a very specific counter (projectiles only)
- Outcome: either a stun on the attacker, or a big damage payoff

### 6. Buff / Self-Enhancement
- Shield, damage boost, speed boost, invisibility, damage resistance
- Use: prepare before an engage, or pop during combat
- CD: 10-15s
- Duration: 4-8s
- **Rule**: buff must be visible to the opponent (clear feedback)

### 7. Combo Extender / Finisher
- A stronger hit that connects after a CC or setup
- Use: **convert a hit into real damage** — the "payoff"
- CD: 4-8s
- **Common pattern**: conditional — "if target has slow → stun", "if target is stunned → bonus damage"
- This archetype turns poke into kill threat

### 8. Ultimate / Burst
- Big hit with clear visual impact
- Use: finisher, reversal, teamfight swing
- CD: 25-35s
- **Pattern**: must be dodgeable but impactful if it lands
- Examples: zone ult (Athena Aegis Charge), damage ult (Battlerite), summon ult (Wukong clone)

---

## Keybinding Patterns by Game

Different games map abilities differently. The common thread across all of them:

| Game | LMB | RMB | Q | E | R | F | Shift |
|------|-----|-----|--|--|--|--|-------|
| **DKO** | Light | Heavy¹ | Ability² (aimed) | Ability³ (mobility) | Ability⁴ (burst) | **ULT** | Dodge |
| **Battlerite** | Basic attack | Secondary | Main spell | Main spell | **ULT** | EX version | — |
| **Supervive** | Basic attack | Sprint atk | Ability | Ability | **ULT** | Ability | — |
| **Fangs** | Light | Heavy | Ability | Ability | **ULT** | — | Dodge |
| **Omega Strikers** | Basic attack | — | Ability | Ability | **ULT** | — | Dodge |


**Footnotes:**
¹ **RMB base damage ~10% uncharged** — the standard heavy attack damage across DKO
² **Q = aimed spell, often recovery** — aimed skillshots go on Q for muscle memory; many recovery abilities also on Q (the most accessible key for frequent use). Cancel aimed Q spells with RMB (quick cancel, not charge).
³ **E = mobility/utility** — gap closers, movement tech, self-buffs. CD ~16s.
⁴ **R = burst/spam tool** — lower CD (~12s), often the highest DPS ability. Spammable finisher or poke.

---

## Cooldown Templates (DKO Baseline)

| Slot | CD | Role |
|------|----|------|
| Q | 21s | Aimed ability, often recovery. Highest CD of the 3 ability slots because it's the most accessible key and often defines the character's identity |
| E | 16s | Mobility, utility, setup. Mid-range CD — frequent enough to use each engage, long enough to punish whiffs |
| R | 12s | Burst/spam tool. Lowest CD — repeatable threat, defines DPS pattern |
| F | 25-35s | Ultimate. Impact-per-use, not spam |

These are **baseline values**. They shift per character identity and class archetype:
- Rushdown characters tend to have lower CDs on engage tools
- Control/zone characters have longer CDs on area-denial tools
- Assassins have shorter CDs on mobility, longer on burst

### Key Takeaways

- **Q = engage/CC** across most games; **DKO standard is aimed + recovery** on Q — the most accessible key for frequent-use abilities. Cancel aimed Q spells with RMB (quick cancel, not charge).
- **E = mobility/recovery** is extremely frequent — Battlerite, Fangs, DKO, Supervive all use it this way. DKO baseline CD: ~16s.
- **R = burst/spam tool** in DKO (lowest CD at ~12s, defines DPS pattern). Other games map R = ult.
- **DKO is the exception**: ult on **F**, with R being a regular (but often strong) ability
- **Shift = dodge / physical roll** — never a spell, always a universal mechanic
- **RMB baseline: ~10% uncharged** across DKO. The exact value can vary per character but 10 is the tuning anchor.

---

## The 4 Class Archetypes

Across DKO, Battlerite, and all games in the genre, characters fall into 4 broad archetypes
with distinct ability slot distributions:

### Rushdown / Brawler
| Slot | Role | CD |
|------|------|----|
| LMB | Light attack or weak poke | 0s |
| Q | CC engage (knockup, stun) | 6-8s |
| E | Mobility / gap close | 4-6s |
| R or F | Big damage burst | 25s |
| Passive | Damage buff or movespeed | permanent |

Examples: Thor (DKO), Hercules (DKO), Shaggy (MultiVersus), Bakko (Battlerite)
**Gameplan**: gap close → CC → damage. No poke, no zone. All-in on engage.

### Control / Zone
| Slot | Role | CD |
|------|------|----|
| LMB | Projectile poke | 1-2s |
| Q | CC (slow, root) | 7-10s |
| E | Zone / area denial | 8-12s |
| R or F | Big zone ult | 30s |
| Passive | Zone buff or range bonus | permanent |

Examples: Ymir (DKO), Sol (DKO), Iva (Battlerite), Sirius (Battlerite)
**Gameplan**: poke at range → place zones → force the enemy into a trap.

### Support / Utility
| Slot | Role | CD |
|------|------|----|
| LMB | Medium poke | 1s |
| Q | Mobility (self or team) | 6-8s |
| E | Counter / Parry or heal | 8-12s |
| R or F | Team buff / heal / resurrection | 30s |
| Passive | Ally buff | permanent |

Examples: Arthur (DKO), Poloma (Battlerite), Pearl (Omega Strikers), Lucie (Battlerite)
**Gameplan**: stay in backline, buff teammates, counter enemy engages.

### Assassin / Glass Cannon
| Slot | Role | CD |
|------|------|----|
| LMB | Fast light combo (often 4 hits) | 0s |
| Q | Stealth / teleport / trick | 5-8s |
| E | Combo extender (conditional) | 6-8s |
| R or F | Big single-target ult | 30s |
| Passive | Bonus damage under a condition | permanent |

Examples: Loki (DKO), Izanami (DKO), Croak (Battlerite), Shifu (Battlerite)
**Gameplan**: flank → burst → escape. High risk, rewards individual skill.

---

## 9 Design Rules

### Rule 1: Abilities have commitment
- Startup + endlag. The stronger the ability, the more you risk on a whiff
- Light abilities: startup 0.1-0.2s, endlag 0.1s (you can act almost immediately)
- Heavy abilities: startup 0.3-0.6s, endlag 0.3s (you're vulnerable on whiff)
- Ultimates: startup 0.5-1.0s (very telegraphed, opponent can dodge)

### Rule 2: CC is the combat key
- Without CC, nobody can punish → chip damage stalemate
- With too much CC, it's stunlock → not fun
- **Sweet spot: 1 CC ability per kit** (6-10s CD)
- Hard CC (stun) lasts 1-2s max in a platform fighter

### Rule 3: Each ability has a general role
- Every ability has a **primary job**: poke, move, CC, zone, counter, buff, burst, extender, finisher
- Don't cram 3 jobs into one spell — a finisher can have a small slow attached, a movement ability can have a small hitbox, but a spell that simultaneously zones + heals + silences is overloaded
- The role is a **design north star**, not a straightjacket
- Synergies between **different slots** are encouraged
  (e.g. Q applies slow → E hits harder on slowed targets)

### Rule 4: Recovery abilities must be usable often
- Short CD (3-6s). This is what makes combat dynamic
- Without mobility, a character is immobile and predictable
- The player should be able to use it for both engage **and** disengage

### Rule 5: Counter/parry is optional but powerful
- Rewards reads and creates "outplay" moments
- 1 character per pool max, or reserved for a specific archetype (support/control)
- Must have a whiff punish — if the player misses the counter window, they're vulnerable

### Rule 6: No mana, cooldowns only
- Players manage **timing**, not a resource
- Cooldowns let opponents read patterns ("he used his Q, I can engage now")
- Exception: a self-damage character (Thanatos in DKO) works as a trade-off mechanic

### Rule 7: Abilities interact between slots
- **Combinations between abilities** are encouraged
- Classic DKO combo flow:
  1. Q = slow (CC)
  2. E = stun if target has slow (combo extender)
  3. R or F = big damage during stun (burst)

### Rule 8: Every kit must have a clear weakness
- Rushdown: weak at range, predictable approach
- Control: weak in close range, vulnerable when rushed
- Support: weak in 1v1, depends on teammates
- Assassin: fragile, dead if the burst misses

### Rule 9: All characters have basic attacks
- LMB = light attack (2-4 hit combo depending on character)
- RMB = heavy attack (slower, stronger, often has a charge hold variant)
- In DKO, light attacks deal ~6/6/12 (total 24), heavy attacks deal ~10
- Even mages have physical light attacks (staff slap, punch, etc.)

---

## Applying to SlopArena — Current Roster

Only one character is active in the codebase. Design others from scratch following these patterns.

### Manki (Explosive Bomber / Rushdown Hybrid)

| Slot | Role | Type | Notes |
|------|------|------|-------|
| LMB | Light combo (3 hits) | Physical | Punch → kick → fire uppercut (launcher) |
| AirLMB | Air Kick (2 hits) | Physical | 2-hit air combo, first kick lunges, second has higher KB |
| RMB | Aerosol + Lighter (charge) | Ability | Charged heavy, cone flame zone denial |
| AirRMB | Knuckle Spike | Physical | Slow windup downward spike, high KB |
| Q | Round Bomb (projectile) | Ability | Lobbed arc, explodes on impact — poke/zone |
| E | Grapple Gun | Ability | Fire tether, reel toward enemy or terrain. 3 dmg, no stun. |
| R | Bazooka | Ability | Fire rocket in aim direction, arcs with gravity, explodes on contact. Rocket jump via self-damage (4). |
| F | Overclock (ult) | Ability | Self-buff 8s — all attacks deal +3 bonus damage and +0.5m larger hitboxes |
| Passive | — | — | (None yet) |

**Gameplan**: poke with Q → close gap with grapple → ground combo → rocket jump for air follow-up. E grapple is gap closer AND escape tool.
### Future characters

When designing a new character, start from one of the 4 archetypes defined above, then fill the 8 ability slots (LMB, AirLMB, RMB, AirRMB, Q, E, R, F). Document in `docs/characters/<name>.md`.

---

## References
- `docs/research/dko-character-kits.md` — raw DKO data (13 gods, full ability tables)
- `docs/combat-systems.md` — universal combat mechanics: attack patterns, aerial rules, aiming types, hitbox philosophy, finisher design
- Research sessions: Battlerite (6 abilities, all skillshots), Supervive (4 abilities + platform fighter movement), Fangs (2 abilities + ult), Rumble (pure movement, no abilities)

