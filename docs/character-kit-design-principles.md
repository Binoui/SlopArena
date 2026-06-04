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
- **Rule**: no homing. The projectile travels straight in the character's facing direction.

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
| **DKO** | Light | Heavy | Ability | Ability | Ability | **ULT** | Dodge |
| **Battlerite** | Basic attack | Secondary | Main spell | Main spell | **ULT** | EX version | — |
| **Supervive** | Basic attack | Sprint atk | Ability | Ability | **ULT** | Ability | — |
| **Fangs** | Light | Heavy | Ability | Ability | **ULT** | — | Dodge |
| **Omega Strikers** | Basic attack | — | Ability | Ability | **ULT** | — | Dodge |

### Key Takeaways

- **Q = engage/CC** is the most solid pattern across all games
- **E = mobility/recovery** is extremely frequent — Battlerite, Fangs, DKO, Supervive all use it this way
- **R = ult** is the general pattern across most games
- **DKO is the exception**: ult on **F**, with R being a regular (but often strong) ability
- **Shift = dodge / physical roll** — never a spell, always a universal mechanic

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

## 10 Design Rules

### Rule 1: Everything is a skillshot
- No homing, no auto-aim
- The **character's facing direction** = the ability's direction
- The player aims with movement (WASD), not with a crosshair
- A projectile goes straight in the direction you face at cast time

### Rule 2: Abilities have commitment
- Startup + endlag. The stronger the ability, the more you risk on a whiff
- Light abilities: startup 0.1-0.2s, endlag 0.1s (you can act almost immediately)
- Heavy abilities: startup 0.3-0.6s, endlag 0.3s (you're vulnerable on whiff)
- Ultimates: startup 0.5-1.0s (very telegraphed, opponent can dodge)

### Rule 3: CC is the combat key
- Without CC, nobody can punish → chip damage stalemate
- With too much CC, it's stunlock → not fun
- **Sweet spot: 1 CC ability per kit** (6-10s CD)
- Hard CC (stun) lasts 1-2s max in a platform fighter

### Rule 4: One ability = one job
- Each ability has **one clear job**: poke, move, CC, zone, counter, buff, burst
- No abilities that "apply effect A + bonus if effect A already present"
- Synergies between **different slots** are OK
  (e.g. Q = slow → E = stun if slowed) but not within the same ability

### Rule 5: Recovery abilities must be usable often
- Short CD (3-6s). This is what makes combat dynamic
- Without mobility, a character is immobile and predictable
- The player should be able to use it for both engage **and** disengage

### Rule 6: Counter/parry is optional but powerful
- Rewards reads and creates "outplay" moments
- 1 character per pool max, or reserved for a specific archetype (support/control)
- Must have a whiff punish — if the player misses the counter window, they're vulnerable

### Rule 7: No mana, cooldowns only
- Players manage **timing**, not a resource
- Cooldowns let opponents read patterns ("he used his Q, I can engage now")
- Exception: a self-damage character (Thanatos in DKO) works as a trade-off mechanic

### Rule 8: Abilities interact between slots
- No nested effects within a single ability
- But **combinations between abilities** are encouraged
- Classic DKO combo flow:
  1. Q = slow (CC)
  2. E = stun if target has slow (combo extender)
  3. R or F = big damage during stun (burst)

### Rule 9: Every kit must have a clear weakness
- Rushdown: weak at range, predictable approach
- Control: weak in close range, vulnerable when rushed
- Support: weak in 1v1, depends on teammates
- Assassin: fragile, dead if the burst misses

### Rule 10: All characters have basic attacks
- LMB = light attack (2-4 hit combo depending on character)
- RMB = heavy attack (slower, stronger, often has a charge hold variant)
- In DKO, light attacks deal ~6/6/12 (total 24), heavy attacks deal ~10
- Even mages have physical light attacks (staff slap, punch, etc.)

---

## Applying to SlopArena's 3 Classes

Using the patterns above to fill out the existing 3 classes:

### Vanguard (Rushdown)
| Slot | Role | Type | CD |
|------|------|------|----|
| LMB | Light combo (3 hits) | Physical | 0s |
| RMB | Heavy smash | Physical | 0s, with recovery |
| Q | CC engage (knockup) | Ability | 7s |
| E | Mobility dash | Ability | 4s |
| R | Big damage burst | Ability | 25s |
| Passive | Damage or movespeed on hit | — | — |

### Wraith (Assassin)
| Slot | Role | Type | CD |
|------|------|------|----|
| LMB | Fast light combo (4 hits) | Physical | 0s |
| RMB | Heavy strike | Physical | 0s |
| Q | Stealth / teleport | Ability | 8s |
| E | Combo extender (conditional) | Ability | 6s |
| R | Single-target burst | Ability | 30s |
| Passive | Bonus damage from behind or vs isolated | — | — |

### Channeler (Control)
| Slot | Role | Type | CD |
|------|------|------|----|
| LMB | Projectile poke | Ability | 1.5s |
| RMB | Heavy zone swing | Physical | 0s |
| Q | Slow / root | Ability | 8s |
| E | Zone area denial | Ability | 10s |
| R | Big zone ultimate | Ability | 30s |
| F | Counter / parry shield | Ability | 10s |
| Passive | Extended range or AOE on hits | — | — |

---

## References
- `docs/research/dko-character-kits.md` — raw DKO data (13 gods, full ability tables)
- Research sessions: Battlerite (6 abilities, all skillshots), Supervive (4 abilities + platform fighter movement), Fangs (2 abilities + ult), Rumble (pure movement, no abilities)
