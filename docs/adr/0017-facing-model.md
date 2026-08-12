# ADR-0017: Facing Model — Sticky Air Facing + LMB Camera Snap

**Status:** Accepted — 2026-08-12 (user decision; engine ticket #126)
**Deciders:** @Binoui

## Context

Facing is welded to velocity: the simulation overwrites `FacingYaw` from `Atan2(VX,VZ)` every tick. On the ground that is correct — you face where you run. In the air it breaks the platform-fighter model:

- Drift re-faces the fighter every frame, so air normals point along the drift vector — attack direction feels random and can't be read.
- There is no way to attack "behind" while moving away. The retreating back-air — Melee's core spacing tool — cannot exist; the only answer to "enemy behind me" is turning via movement, which commits you to moving toward them.

Two facts make the fix cheap:

- The 8-slot re-tier (2026-08-12) drops `LMB`/`RMB` as ability inputs, freeing the left mouse button as a utility key.
- The game is camera-relative 8-way movement with an absolute-yaw mouse camera, and the client already converts camera yaw → world yaw for projectile aim (`AimYaw`). The camera-to-facing conversion exists.

Melee precedent: air facing is sticky (no turning in the air); directional aerials (back air) are the spacing toolkit. This kit is role-based (8 normals: low/medium/high/AOE + air variants — no direction-based aerials), so "back air" resolves as *turn then attack* — the Melee turnaround equivalent, not a distinct move.

## Decision

1. **Ground facing = movement** (unchanged). You always face where you run.
2. **Air facing is sticky.** Facing locks at takeoff (last ground facing); drift and camera rotation do not re-face the fighter while airborne. The simulation stops overwriting `FacingYaw` from velocity in the air.
3. **`LMB` = snap facing to camera azimuth** — a utility input, freed by the slot re-tier. Usable when **not attacking** (respects the per-stage `CanTurn` gate), **not in hitstun**, not in burst/lock states. Works on the ground (instant turnaround for poke spacing) and in the air (cross-ups, retreating pressure, warp-cone setup — face the target to pass the warp check).
4. **No direction-based aerials for now.** Back-air style play = LMB turn + normal. The sticky-facing model makes true back attacks (S-direction air variants) a cheap later add if playtest demands them; they are not part of this decision.
5. **`LMB` no longer maps to an ability slot.** The old LMB slot becomes unreachable (intended — the 8-slot re-tier drops LMB/RMB as abilities; each character's normals pass moves its LMB move to slot 1). RMB is assigned to the target-lock toggle (ADR-0018).

Wire/impl shape: the client sends a `FaceToCamera` flag (one bit in the existing flags2 word) and the sim honors it at the input gate; the client drops the LMB pending-slot branch and reuses the AimYaw camera→world yaw math. The air-facing-overwrite guard is a one-line condition in movement processing.

## Considered Options

- **Camera-relative facing always (DKO-style)** — rejected: collapses the facing dimension entirely; there is no back side, only camera-forward/backward, and directional variety (cross-ups, spacing reads) disappears. This game wants Melee-style positioning depth, DKO does not.
- **Directional aerials now (S+normal variants)** — deferred: doubles the air normal count and re-opens the kit schema; the role-based kit is coherent first. Sticky facing + LMB turn delivers most of the playstyle; revisit on playtest evidence.
- **Facing follows drift + LMB snap only** — rejected as a no-op: the snap is reverted the next tick by the velocity overwrite. The sticky rule is the enabler, not an optional extra.
- **Dedicated turn button (e.g. T)** — rejected: LMB is already free and the mouse is the right hand anyway; keyboard hotkeys are at capacity (ADR-0016).

## Consequences

- **Air normals become deterministic**: attack direction = snapped/sticky facing, not drift.
- **Retreating pressure, cross-ups, and spacing reads become possible** — the bair playstyle arrives as turn-then-attack.
- **`LMB` is a utility input**, not a move; the old LMB slot dies with each character's normals pass (#120-123).
- **Warp synergy**: the snap sets up warp cones (face target → cone check passes).
- **Wire:** `InputState` gains one flag bit in flags2 (20 B unchanged); rollback replay carries it automatically.
- **Behavior change for kits still using the LMB slot** (Manki/Kistu/Nilus until their normals passes land): their LMB moves become unreachable the moment the snap lands. Accepted — consistent with the 8-slot re-tier; FightGuy is the feel test.
- **Air turns are instant** (no turn rate) — Melee-like; a quick-pivot animation is polish, not gameplay.
- **Goldens:** air-facing scenarios are additive (new scenarios pin drift-no-reface and snap-then-normal); existing goldens assert state at attack ticks, not facing, so they should not move — verify on landing.

## Implementation notes (ticket #126, 2026-08-12)

- Input gate seam: the snap is honored where pending inputs are consumed; the "not attacking" check uses the same state condition the input gate already applies (attack lock / hitstun / burst).
- The movement-facing overwrite gains an airborne guard: ground = velocity-facing as today; airborne = keep current facing unless a snap arrives.
- Client: `InputController.Poll()` removes the `mouse.leftButton → AbilitySlots.Lmb` branch and sets the snap flag + camera yaw; the camera component already owns absolute yaw.
- Playtest sequence (from the ADR discussion): jump (Z), rotate camera with mouse, LMB, press 2 — the medium normal fires behind the drift direction.
