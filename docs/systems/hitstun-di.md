# Hitstun + DI System

Melee-based hit response — knockback formula, hitstun, hitstop, DI/SDI/ASDI, and the flight law. Decided by the wayfinder map ([#128](https://github.com/Binoui/SlopArena/issues/128)); the authoritative record is **[ADR-0019](https://github.com/Binoui/SlopArena/blob/main/docs/adr/0019-melee-based-hit-response.md)**. This doc is the working reference.

## Hit Sequence

```
Attack lands → Hitstop (freeze, DI/SDI decision window) → Launch (KV, DI-rotated)
            → Hitstun lock (constant KV flight, no input) → actionable flight
            → post-hitstun drift (linear friction + flight gravity) → landing / tech
```

1. **Hitstop** (`HitstopTicks`): per-pair freeze (ADR-0012). The defender commits DI input and one-shot SDI here; Burst is pressable (ADR-0014).
2. **Launch**: at hitstop exit, the queued knockback velocity (KV) is applied, rotated by DI (below).
3. **Hitstun** (`ActionState.Hitstun`): the **no-input lock** — the victim flies at **constant KV** (no decay, no freeze). Control returns at lock end with residual speed.
4. **Post-hitstun flight**: linear horizontal-azimuth friction (10 m/s²), flight gravity (8 m/s²), aerials usable, no airdodge (ADR-0019 §6).

## Knockback Formula (ADR-0019 §1)

```
mag = (base + growth·(P/100 + 1.0) + Damage·0.1) · 200/(W + 100)
```

- `base` / `growth`: per-hitbox (custom-only — `KnockbackProfile` is gone).
- `P` = victim DamagePercent; `Damage` = the hit's damage; `W` = victim weight (`CharacterDefinition.Weight`, default 100; **KB-only** — no throws exist).
- Growth floor f = 1.0 (growth lives at 0% victim damage). No cap (999 unreachable).

## Hitstun (ADR-0019 §2)

```
hitstun = max(1, (int)(0.5 · kbMag))     // k = 0.5 provisional
```

- **Pure function of the applied KB** — `StunTicks` is **gate-only**: `0` → no lock at all (zero-KB hitboxes AND KB-bearing moves like the burst shove stay lock-free). This is the one deviation from Melee's min-1.
- Cap / floor-8 / ceiling-60 dropped.
- `HitstunLevel` anim tier re-derives from the *applied* hitstun (was: authored `StunTicks`).
- Burst is the explicit exception — the only thing that acts inside the lock (ADR-0014).

## Hitstop (ADR-0019 §3)

```
hitstop = min(12, (int)((damage/3 + 6) · mul))    // mul = hitstop_multiplier, default 1.0
```

Jabs 7, mediums 8, kills 10 ticks. Cap 12 = never-biting safety. The old `hitstop_*` key set (low-damage ×2, multihit ×0.5, floor, beyondFirst) is gone — one `hitstop_multiplier` per hit.

## DI (ADR-0019 §4)

- **Capture** the committed 8-way input (`DIX`/`DIY`) during `HitstopTicks`; **rotate at hitstop exit** (hitstop = 0 → capture at the hit tick, rotate immediately).
- Rotate the launch toward `(DIX, 0, DIY)` by up to **`18°·sin²(angle)`**, **magnitude preserved**. Perpendicular input = full rotation; input along the launch = no rotation.
- **Plane rotation** (3D): elevation tilts toward horizontal for vertical launches (survival-DI works on spikes/launchers); horizontal launches degenerate to azimuth.
- Combo Influence is **gone** (ADR-0013 superseded) — no additive drift, no `LaunchMagnitude`.

## SDI + ASDI (ADR-0019 §5)

- **One-shot SDI**: the first nonzero input during `HitstopTicks` → one position shift along the 8-way hold.
- **ASDI**: one more shift at flight start along the committed DI direction (~0.4× magnitude). Up to 2 shifts/hit; **mashing adds nothing**.
- **No timers/flag** — the first-input tick is a pure function of replayed relayed input → zero wire bytes (CSP stays 113).

## Knockback During Flight

- **Hitstun**: constant KV; **Melee hard-set bounce** on stage contact during hitstun.
- **Post-hitstun**: horizontal-azimuth KV decays linearly (10 m/s²), KVY untouched (flight gravity 8 m/s²). No re-lock — the victim is actionable (jump, aerials, specials) once the lock ends.

## Balancing (post-design, ADR-0019 Consequences)

| Axis | Current | Lever |
|------|---------|-------|
| Hitstun k | 0.5 | per-char weight, k, vertical-KB lean (steeper angles + magnitude) |
| DI rotation | 18°·sin² | rotation cap |
| SDI | global scale + ASDI ~0.4× | `sdiScale`, ASDI fraction |
| Weight | W=100 all | per-character values |
| Hitstop | Melee shape | `hitstop_multiplier` per hit |

Golden tests: movement/hitstop/DI snapshots regenerate (REGENERATE_GOLDENS) when the model lands.

## Animation Tiers

`HitstunLevel` — 0 = small / 1 = medium / 2 = hard — **re-derived from applied hitstun** at hit time (ADR-0019 consequence), not from authored `StunTicks`. The client maps level → clip (`hit_light` / `hit_medium` / `hit_hard`) through `CharacterAnimationConfig`, played via `_animancer` with speed from `HitstunTicks`.

## References

- **[ADR-0019](docs/adr/0019-melee-based-hit-response.md)** — authoritative decision record.
- ADR-0012 (hitstop per-pair freeze), ADR-0013 (superseded — Combo Influence), ADR-0014 (Burst exception), ADR-0020 (movement), ADR-0021 (frame timing).
- `docs/research/melee-knockback-model.md`, `melee-frame-analysis.md` — the Melee research.
- Ticket lineage: [#130](https://github.com/Binoui/SlopArena/issues/130) flight, [#131](https://github.com/Binoui/SlopArena/issues/131) hitstun, [#132](https://github.com/Binoui/SlopArena/issues/132) KB formula, [#133](https://github.com/Binoui/SlopArena/issues/133) DI, [#134](https://github.com/Binoui/SlopArena/issues/134) SDI, [#143](https://github.com/Binoui/SlopArena/issues/143) hitstop.
