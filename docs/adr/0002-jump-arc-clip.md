# ADR-0002: JumpArc animation model

Context: The jump animation clip (24 frames @ 30fps for FightGuy) contains a full jump loop — ascent frames 1-15 and baked descent frames 16-24. The original animation system switched to the Fall clip at the physics peak (VY crossing 0), which meant the jump clip was cut short mid-playback. This produced a visual mismatch: the jump clip's baked descent was never seen, and the abrupt VY-triggered transition looked wrong for a platform fighter.

Decision: Replace the VY-based fall transition with a JumpArc model. The jump clip plays in full (ascent + baked descent) during a normal jump. The Fall clip is reserved for extended air time — when the JumpArc clip finishes while the character is still airborne.

## Considered Options

- **Option A: Full clip, no VY transition (chosen).** Jump clip plays start to finish. Only transition to Fall when clip ends while still airborne. Simple code change, matches the designer's intent.
- **Option B: Split clip (ascend + descend).** Two separate clips from the same source, apex handled in code. More flexible for per-character tuning but requires re-exporting clips.
- **Option C: Full clip, speed modulated to match arc.** Same as A but per-character speed modulation. Premature optimization — jump timing naturally close to clip duration.

## Consequences

- Jump arc is cleared on: landing, aerial attack, hitstun — after interruption, character goes to Fall (generic airborne state).
- Double jump restarts the JumpArc clip from the beginning.
- Fall transition uses a slow crossfade (1.5s) matching the floaty gravity feel.
- Per-character tuning not needed initially — FightGuy's clip (0.8s) closely matches its jump arc duration.
- If a future character's jump arc differs significantly from its clip length, speed modulation or clip trimming may be needed.
