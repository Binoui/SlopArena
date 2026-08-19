# SlopArena Visual Production Roadmap

**Status:** Active execution roadmap — 2026-08-19  
**Direction source:** [`visual-identity-discovery.md`](visual-identity-discovery.md)  
**Goal:** Make the mechanically complete demo look intentional, readable, and inviting enough to attract players and future contributors.

This roadmap coordinates presentation work. It does not replace the PvP or character-kit roadmaps. Finished abilities remain the demo's gameplay blocker; visual work should use presentation-only seams and avoid destabilizing simulation.

## Product target

SlopArena should read as **mischievous, energetic, and slightly unruly**: graphic-stylized 3D, strong silhouettes, controlled neutral scenes, and brief spectacle at meaningful combat moments.

The target is not a collection of individually impressive assets. It is one coherent match:

1. An arena establishes a recognizable place.
2. Fighters remain readable during neutral play.
3. Movement and attacks communicate intent before contact.
4. Hits communicate strength and direction immediately.
5. KO, stock, and result moments provide a satisfying arc.
6. Menus and HUD feel like the same game.

## Locked principles

- **Gameplay readability first.** Neutral play stays clean; heavy hits and KOs may briefly dominate.
- **Server events drive presentation.** VFX, camera, and audio react to authoritative or predicted simulation events; they never alter gameplay state.
- **Shared grammar, character flavor.** Universal timing and strength tiers carry gameplay meaning. Character layers add identity without replacing that grammar.
- **Normals stay restrained.** Their accents expose motion and active timing. Specials own persistent particles, larger silhouettes, and richer secondary effects.
- **One gameplay camera is the judge.** Every visual slice is reviewed in motion at the real match camera, not only in Scene view or a promotional still.
- **Collision and render shells stay separate.** Decorative stage assets never become authoritative gameplay geometry.
- **Hybrid assets, unified treatment.** Generated, free, purchased, and custom sources are acceptable when materials, palette, lighting, scale, and VFX make them coherent.
- **No asset shopping before a demonstrated gap.** Prototype with current content first; replace only what presentation cannot rescue.
- **Record provenance.** Official assets retain author, source, license, editable source, dependencies, modifications, and AI/tool disclosure.

## Current baseline

### Implemented or prototyped

- Five player-facing arena layouts: **Slop Court**, **Splash Deck**, **After Hours**, **Rec Center Roof**, and **Picnic Panic**.
- A separate **Training Lab** designed for measurement and debugging rather than Stage Select.
- Per-stage atmosphere controls and a first shared `MatchVisualStyle` lighting/material treatment.
- A graphic character-shader prototype with player-color separation.
- Shared light, medium, heavy, and launch impact tiers driven by accepted simulation hits.
- One pooled impact renderer used by Training and PvP.
- Authored bone trails on some normal attacks.

### Provisional or missing

- Arena geometry and props are visual prototypes, not final render shells.
- Lighting, character materials, and rim treatment are not yet a locked style.
- Existing bone ribbons look generic and overstate normal attacks.
- No deliberate movement-start, jump, landing, fast-fall, launch-dust, KO, or respawn grammar.
- No strength-tiered camera response.
- Character-specific hit layers are mostly absent.
- Functional HUD, menus, transitions, and results remain visually basic.
- Combat audio has not received the same strength-tiered presentation pass.

## Milestone 1 — Shared combat readability

**Outcome:** Attacks read clearly before, during, and after contact without turning every normal into a special.

### 1.1 Replace bone ribbons with normal-attack accents — FightGuy approved

Replace continuous glowing bone trails with a pooled accent system tied to authored attack timing.

Working contract:

- Spawn only around active hitbox frames, with limited anticipation when an animation needs it.
- Build a short tapered arc or compressed afterimage from recent limb/weapon motion.
- Let motion speed and authored attack strength drive width, length, and opacity.
- Keep a typical normal visible for roughly 4–8 frames.
- Use one primary shape for light normals; allow one restrained secondary fragment for heavy normals.
- Remove glow during idle and ordinary recovery.
- Use character color as an accent, not a full-screen light source.
- Share timing and pooling infrastructure while permitting different surfaces:
  - FightGuy: blunt crescents and compressed blue-white afterimages.
  - Manki: rough arcs with sparse sparks or smoke wisps.
  - Kistu: narrow blade arcs and glints.
  - Nilus: thin unstable void tears.

**Gate:** Project-owner gameplay-camera approval replaces mandatory capture artifacts.
FightGuy's full grounded/aerial normal set was approved on 2026-08-19: paths remain
legible at gameplay zoom, effects clear before neutral, and normals remain visually
distinct from specials. Character-specific rollout beyond FightGuy remains pending.

### 1.2 Add restrained camera response

- No shake for weak multihits.
- Small, short impulse for medium hits.
- Stronger but brief directional impulse for heavy hits.
- Short FOV impulse or emphasis for launch and KO only.
- Preserve stable framing and opponent visibility.
- Presentation remains local and never affects simulation or aim.

**Gate:** A blind review can distinguish medium, heavy, and launch contacts from camera plus impact presentation, while neutral movement remains stable.

### 1.3 Add movement and environment feedback

- Dash-start accent and restrained speed residue.
- Jump puff, double-jump accent, landing compression, and fast-fall streak.
- Weight-scaled landing dust and launch dust.
- Surface palette comes from the stage; timing comes from movement state transitions.
- Pool all repeated effects.

**Gate:** Run, dash, jump, double jump, fast fall, and landing are identifiable in a muted gameplay clip without debug UI.

### 1.4 Add KO and respawn presentation

- Directional KO streak or boundary response.
- Stock-loss emphasis without hiding remaining fighters.
- Character-colored respawn/materialization burst.
- Clear invulnerability read that does not resemble an attack effect.

**Gate:** A spectator can identify who was eliminated, from which direction, and when the fighter becomes controllable again.

### 1.5 Establish combat-audio tiers

- Shared light, medium, heavy, launch, shield/defense, and KO categories.
- Character layers supplement shared transient and low-end weight.
- Audio follows the same accepted hit event and strength classification as VFX.
- Repeated multihits avoid exhausting or clipping playback.

**Gate:** With the screen obscured, heavy and launch events remain distinguishable from light normals without excessive loudness.

## Milestone 2 — Rendering foundation

**Outcome:** Characters and stages look like one game before expensive asset replacement begins.

### 2.1 Lock one gameplay lighting rig

- Directional key light, controlled ambient fill, contact shadows, and atmospheric depth.
- Conservative bloom and exposure.
- No generic post-processing that reduces silhouette or hit-effect clarity.
- Verify bright and dark stages with the same fighters and camera.

### 2.2 Lock the small material family

- Graphic character material with controlled bands or gradient, consistent texture/value ranges, and optional restrained rim.
- Gameplay-surface, background-architecture, distant/fogged, emissive-accent, and stylized environmental-effect materials.
- Player-color treatment remains readable but does not permanently outline every fighter.
- Prefer a few boring, maintainable shaders over one universal master shader.

### 2.3 Publish the practical style sheet

Record:

- Palette and value hierarchy.
- Fighter/background contrast rules.
- Character and environment material parameters.
- Shared VFX shape, timing, and coverage limits.
- Character-specific flavor boundaries.
- UI shape and typography direction.
- Asset provenance requirements.
- Good and bad gameplay-camera examples.

**Milestone gate:** Neutral screenshots from at least one bright and one dark arena look intentional without debug colors, and a ten-second combat clip stays readable through movement and impacts.

## Milestone 3 — Signature arena vertical slice

**Outcome:** One arena proves the environment-production method and becomes the quality reference for the remaining roster.

### 3.1 Select the signature arena

Compare the five layouts in motion using:

- Recognizable premise and thumbnail silhouette.
- Neutral-play readability.
- Midground landmarks and background vista opportunities.
- Compatibility with the locked lighting/material family.
- Scope achievable without changing collision.

Do not select by concept art alone.

### 3.2 Build its render shell

Keep baked collision untouched. Add separate layers for:

1. Gameplay surface and edge treatment.
2. Midground architecture and props.
3. Background vista and atmosphere.
4. Lighting and environmental motion.
5. Cosmetic response to launches and KOs where useful.

**Gate:** The arena is recognizable from a thumbnail, every playable edge remains readable, decorative assets never affect collision, and performance remains stable with four fighters and combat VFX.

### 3.3 Propagate the method

After the signature arena passes, extract reusable materials, prop rules, atmosphere settings, prefab conventions, and performance budgets. Apply them to the other four arenas without forcing identical scenery.

## Milestone 4 — Character presentation layers

**Outcome:** Every fighter belongs to the shared game while retaining a recognizable visual signature.

FightGuy remains the system testbed; Manki remains the tone and flagship-identity anchor.

For each character:

- Unified material treatment and palette pass.
- Idle, run, jump, landing, hitstun, and death presentation audit.
- Normal-attack accents using the shared restrained contract.
- Character-specific special VFX.
- Character-specific hit flavor layered over shared impact tiers.
- Portrait/icon treatment.
- Spawn, KO, and victory treatment.
- Audio palette.

**Per-character gate:** A neutral movement clip, a normal string, one special, one launch, and one KO are identifiable as that character without abandoning the shared strength grammar.

## Milestone 5 — Whole-shell coherence

**Outcome:** The match no longer jumps between polished combat and prototype menus.

### 5.1 Match HUD

- Consistent typography and spacing.
- Player-color system and portraits.
- Readable damage escalation and stocks.
- Clear target/lock state without debug-looking indicators.
- Shared panel, icon, and accent-shape language.

### 5.2 Frontend and transitions

Apply the same system to:

- Main menu.
- Server browser.
- Lobby and character select.
- Stage select.
- Match loading and countdown.
- Fight callout, stock loss, match end, results, and return to lobby.

**Milestone gate:** A recorded flow from menu to results contains no visually unrelated screen, placeholder debug styling, or unexplained instantaneous transition.

## Milestone 6 — Flagship proof

**Outcome:** Produce the artifact that demonstrates the game's promise to players and contributors.

Use FightGuy plus the signature arena to capture a controlled 30-second sequence:

1. Arena establishing shot.
2. Neutral movement.
3. Dash, jump, and landing.
4. Light and medium contacts.
5. Character-flavored attack.
6. Heavy launch with restrained camera response.
7. KO and respawn or match end.
8. HUD and results presentation.

Review the moving sequence, not selected frames. Fix discontinuities between systems before adding more assets.

**Release gate:** The clip looks coherent in motion, communicates every major gameplay event without debug UI, and contains no effect or screen that clearly belongs to a different visual language.

## Milestone 7 — Contributor launch

**Outcome:** Let contributors improve the game without requiring them to invent its production rules.

Publish:

- Practical style sheet.
- Source-file and provenance requirements.
- Character and arena contribution templates.
- Technical constraints and acceptance media.
- Example implementation from the flagship slice.
- Small starter tasks: one landing-dust variant, portrait, prop, character idle, heavy-hit flavor layer, environmental motion, or documented playtest report.

Do not begin with unrestricted “make a character” requests. Curated small work establishes trust and teaches the shared grammar.

## Execution order

The immediate queue is:

1. Replace normal bone ribbons with restrained attack accents.
2. Add strength-tiered camera response.
3. Add movement and landing feedback.
4. Validate and lock lighting plus the character-material family.
5. Select and build the signature arena.
6. Add KO/respawn and combat-audio tiers.
7. Build FightGuy's complete presentation pass.
8. Restyle HUD, match flow, and results.
9. Capture the flagship proof.
10. Package contributor guidance and starter tasks.

Character ability completion remains the gameplay priority. Visual work should proceed as bounded slices that finish cleanly and do not create a parallel rewrite of combat, netcode, or stage collision.

## Definition of done for every visual slice

A slice is complete only when:

- It is exercised in the real Unity gameplay camera.
- Training and PvP share the same presentation path where the underlying event exists in both.
- Simulation state and packet formats remain unchanged unless separately designed and approved.
- Repeated effects are pooled or otherwise allocation-safe.
- Bright and dark stage readability is checked.
- A before/after capture demonstrates the intended improvement.
- `TESTING-UNITY.md` contains the remaining human playtest checks.
- Unity script compilation is clean.
- Relevant system documentation describes the final contract.
