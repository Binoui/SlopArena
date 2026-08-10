# Issue #117 — Kit Expansion (batch 1: FightGuy)

> Status: Design settled 2026-08-10 v2 (user re-tier). Implementation pending go-ahead.

## The real layout — 10 keys (user decision, supersedes ADR-0016's provisional key set)

| Tier | Keys | World |
|---|---|---|
| Base normals — **universal schema, every character** | LMB / RMB (+ air variants) | jab / chargeable poke-lunge |
| FG normals — **universal schema** | 1 2 3 4 | spacing medium / anti-air / big punish / get-off-me |
| Abilities (B-moves / MOBA) | Q E R F | projectile, upward mobility, engage, ult |

Keys 5 and A are optional extras — **demo skips them** ("we don't have to use them all").

Key note: **Q = slot 11 (slot A)**. Unity `Key` is position-based; on AZERTY the QWERTY-Q
position is the physical "A" key — which is already the Azerty preset default for slot A
(`InputBindings.DefaultKey(SlotA, Azerty) == Key.Q`). So Ki Shot's **data** moves to slot 11;
**zero binding changes, zero wire changes.**

## The universal normal schema (basis for all future kits)

| Key | Role | Design |
|---|---|---|
| LMB | jab | light, fastest, lowest commit, short range |
| RMB | chargeable poke/lunge | medium-heavy, hold-to-charge, more range |
| 1 | medium spacing | more range than jab, mid speed/damage |
| 2 | anti-air | upward geometry, launches into air combos |
| 3 | big punish normal | slow, telegraphed, high KB (FightGuy: stomp) |
| 4 | get-off-me | hits around character (360°), escape pressure |

Air variants: LMB/RMB air = mandatory (schema); 1-4 air = **optional per character** (FightGuy
demo: ground-only; air pass later). Engine rule: a slot with no air spec is grounded-only.

## FightGuy 11-slot kit (demo)

| Slot idx | Key | Move | Behavior | Ground | Air | CD |
|---|---|---|---|---|---|---|
| 0 | LMB | Dragon Jab | data (`LmbCombo`) | light jab | Rising Kick (AirLMB) | 0 |
| 1 | RMB | Uppercut | `FightGuyUppercut` (charge) | charged launcher | Helicopter spike (AirRMB) | 60 |
| 2 | 1 | Dragon Thrust | data | forward medium, spacing | — | 0-15 |
| 3 | E | Rising Dragon | **new rising-kick class** | anti-air rising kick | recovery burst (FloatWindow reset) | 180-240 |
| 4 | R | Cyclone Kick | `FightGuyCycloneKick` (moved from E) | forward stun lunge | shared | 120 |
| 5 | F | Tempest | `FightGuyTempest` | pull + launcher | ground-only (new: no air spin) | 540 |
| 6 | 2 | Dragon Uppercut | data | anti-air launcher | — | 20-30 |
| 7 | 3 | Dragon Stomp | data | big punish, slow, huge KB | — | 45-60 |
| 8 | 4 | Ki Wave | data | get-off-me, 360° pushback | — | 30-45 |
| 9 | 5 | — | — | empty (demo) | — | — |
| 10 | Q | Ki Shot | `FightGuyKiShot` (moved from slot 2) | aimed projectile + mark | shared | 120 |

**Dragon's Kick is cut** — it was redundant with Cyclone (both forward kicks, per user).
The mark-execute synergy drops; marks stay as a passive setup hook for a later pass.
[PENDING USER CONFIRM]

## Engine deltas

1. **`CharacterDefinition.GetSlotAbility` air semantics change** — replaces the
   GroundedOnly/AirborneOnly flag proposal (one mechanism, no flags):
   - `(n, true) => AirN` — a slot needs an air spec to fire in the air; **null air = grounded-only**.
   - Shared ground+air = `AirN` references the ground spec (same object, read-only data).
   - Distinct air move = separate spec.
   - +9 nullable fields (`AirSlot1, AirE, AirR, AirF, AirSlot2..5, AirA`) + switch rows.
   - Migration for all 4 characters: `AirSlot1 = Slot1, AirE = E, AirR = R, AirF = F`
     (preserves current air use of the ability slots); normals leave Air null.
2. **New class: rising kick** (`FightGuyRisingKick` or generic) — upward VY impulse + hitbox +
   `IsRecoveryMove` flag (engine already resets FloatWindow when airborne). ~30 lines. The
   dive class is dropped — Dragon Stomp covers the downward-punish fantasy.
3. **`AbilityFactory`** FightGuy rows: slot 2 → `LmbCombo` (Thrust), slot 3 → rising kick,
   slot 4 → `FightGuyCycloneKick`, slots 6-8 → `LmbCombo` (Uppercut/Stomp/Wave), slot 10 →
   `FightGuyKiShot`. Slots 0/1/5 unchanged.
4. **`FightGuyData`**: Ki Shot spec moves `Slot1 → A`; `Slot1..4` = 4 normal specs; `E` = rising
   kick; `R` = Cyclone (moved spec); `F` unchanged; `Slot5` empty.
5. **Tests**: golden regen (slot-2 data changed from Ki Shot to Thrust), grounded-only gating,
   rising-kick recovery (FloatWindow reset only airborne), shared-vs-null air resolution.
6. **Docs**: `fightguy.md` (this design), `character-kit-design-principles.md` (universal
   normal schema), ADR-0016 amendment note (Q key = slot 11 on AZERTY; "1" freed).

## Manki (batch-1 second, same skeleton)

Same layout + normal schema; gaps: upward-mobility/recovery slot, defensive tier, normal
tier data. Full design follows FightGuy implementation.
