# Airborne Tumble Animation Brief

## Purpose

Create the missing airborne knockback-drift animation used after the initial hit reaction.
The clip must sell the force carrying the fighter through the air, without whole-body
rotation. It is not a second hitstun reaction and must not look like a repeated flinch.

The runtime sequence is:

```text
hitstop pose freeze
  -> full hitstun reaction
  -> 0.5-second crossfade into airborne knockback drift (semantic clip: tumble)
  -> fall
```

The `PlayerRenderer` plays the complete initial hit-reaction clip before entering
`tumble`, then uses a 0.5-second entry crossfade. There is no automatic early overlap
between the hit reaction and tumble. Once entered, tumble loops while hitstun continues
in the air. Once the fighter is actionable, the current cycle finishes before
transitioning to `fall`. Grounding, jumping, or another explicit action interrupts the
phase immediately.

The legacy/default semantic clip name is `tumble`; the visual direction is airborne
knockback drift, not a literal tumble. A package may declare a different nonempty
`presentation.tumble` ID, which is used consistently by authoring, cooking, and runtime.

## Core performance

The fighter already faces the hitbox after hitstop. Preserve that facing rather than
adding a backward roll, side roll, diagonal tumble, or whole-body spin.

Start already carried by momentum, with body language compatible with the outgoing
hit-medium or hit-hard reaction:

- torso pitched back, with hips carried forward;
- chest and head maintaining a readable knocked-back silhouette;
- no new impact snap or launch wind-up at the beginning of each cycle;
- exact matching to all three hitstun tiers is not required;
- one strong, reusable drift loop is preferable to three nearly identical variants.

Sell the flight through limb drag and delayed follow-through:

- arms lag behind the torso and swing with delayed follow-through;
- hands and forearms remain loose rather than locked into an attack pose;
- legs trail behind the hips with visible secondary motion;
- feet should not plant, step, or prepare an intentional landing;
- head and shoulders follow the torso's restrained sway with slight delay;
- the body should feel carried by momentum rather than actively steering itself.

Use restrained looping sway, not rotation of the whole character. Local joint rotations
remain necessary for torso pitch, loose limbs, and secondary motion; “no rotation” does
not mean a rigid pose. Keep root orientation stable and preserve the runtime-facing
direction throughout. The motion must read as uncontrolled knockback flight from the
normal gameplay camera distance.

## Loop requirements

The clip is played as a loop during launch flight.

- Loop point must be visually clean.
- Root orientation stays stable throughout; no accumulated turn or boundary reset.
- The first and last poses must match in body posture and limb-motion direction and speed.
- Do not include a one-shot recovery pose at the end of the loop.
- Do not include a landing, bracing, get-up, or tech motion.
- The loop should remain readable when repeated for a short or long launch.

The loop has no separate launch or recovery beat:

```text
entry    already drifting: torso pitched back, limbs trailing
cycle    restrained torso sway with delayed, loose limb follow-through
seam     returns to the entry posture with continuous limb motion
```

Do not restart an impact reaction or accelerate into a new launch on every repeat.
Choose the cycle duration through gameplay-camera playback, not a gameplay timing lock.
A long cycle can leave an actionable fighter looking helpless until the fall transition.

## Transition to fall

The runtime stops the tumble loop and plays `fall` once the fighter is actionable and the
current tumble cycle has completed, unless grounding, jumping, or another explicit action
interrupts it first. Do not author a mandatory recovery sequence. Author the loop so that:

- the character can leave the loop from any cycle boundary without a visible snap;
- the body is already airborne and relaxed;
- the final pose does not imply impact or recovery;
- the existing fall clip can take over with a short crossfade;
- the character remains clearly in flight, not frozen in a hit reaction.

The tumble animation must not try to encode knockback distance, gravity, hitstun duration,
or landing timing. Shared simulation owns those mechanics. The clip only provides the
visual body response.

## Animation style

Prioritize silhouette and secondary motion over acrobatics:

- exaggerate limb drag enough to read at gameplay scale;
- preserve a clear torso silhouette throughout the sway;
- avoid limbs crossing the torso for the entire loop;
- preserve facing; no rolls, somersaults, inverted poses, or whole-body spins;
- keep the motion broad and physical without a repeated acceleration or impact beat;
- use overlap and follow-through in the wrists, elbows, knees, ankles, neck, and head.

The result should communicate:

> “The hit launched this fighter, and their body is still being carried by that force.”

It should not communicate:

> “The fighter is playing the same flinch animation repeatedly.”

## Technical delivery

- Author against the shared humanoid rig used by the package characters.
- Deliver one reusable semantic clip using the package's declared `presentation.tumble` ID (legacy/default: `tumble`).
- Target the existing 60 Hz sampled animation pipeline.
- Make the clip loop-safe.
- Do not add gameplay events, root-motion gameplay, hitboxes, or gameplay timing dependencies; Shared simulation continues to own hitstun, knockback, gravity, and landing timing.
- `presentation.tumble` is optional in the canonical character document. When absent or empty, no tumble binding or cooked runtime animation entry is required.
- When nonempty, `presentation.tumble` names the semantic animation ID carried into the cooked character catalog; bind that exact ID (for example, `anim.tumble`) rather than adding a parallel alias.
- Verify the clip on at least one representative character at normal gameplay camera distance.

## Acceptance checklist

- [ ] First pose reads as already carried by knockback, compatible with the hit reaction.
- [ ] Flight reads through torso pitch, loose limb drag, and restrained sway—not rolling.
- [ ] Runtime-facing direction stays intact; no whole-body spin or root-orientation drift.
- [ ] Arms, hands, legs, feet, head, and shoulders visibly trail the torso.
- [ ] No planted steps, attacks, landing, tech, or get-up motion.
- [ ] Loop seam preserves pose and limb momentum across at least three uninterrupted cycles.
- [ ] Short launches do not look truncated or broken.
- [ ] Long launches do not look like a repeating hit flinch.
- [ ] Transition into the existing `fall` clip is visually clean.
- [ ] Mid-cycle jump and attack interruptions are readable without a recovery beat.
- [ ] Clip works as a shared reusable animation rather than a character-specific move.
