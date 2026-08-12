# Ability Lab (issue #119)

Visual hitbox/hurtbox authoring tool for SlopArena: a play-mode scene + editor window
that poses a character frame-by-frame through the **same Shared resolvers the server
uses**, shows server-accurate hurtboxes and hitboxes, and persists hitbox edits
directly into the character's C# data source (`src/Shared/Characters/*Data.cs`).

The preview cannot drift from the simulation: hurtbox poses come from
`ServerSimulation.BuildEntitiesFromState` and hitbox positions from
`HitboxGeometry.ResolvePositions` — the identical pure functions the game server runs.

## Open & use

1. Unity Editor (main checkout) → menu **Tools → SlopArena → Ability Lab**.
2. Click **Create Lab Rig** (GameObject `AbilityLab` + camera in the current scene).
3. Camera works immediately, even before Play:
   - **right-drag** orbit, **middle-drag** pan, **scroll** zoom, **Reset camera** button.
4. Press Play, pick a character.

The wireframe display is drawn by the rig via `OnRenderObject` (GL lines), so it shows
in **both the Game view and the Scene view** — no Gizmos toggle required.

| Element | Color | Source |
|---|---|---|
| Hurtboxes | green | `BuildEntitiesFromState` (baked skeleton + `HurtboxBoneDefs`) |
| Hitboxes | orange | `HitboxGeometry.ResolvePositions` |
| Dummy opponent (optional) | red | idle-frame hurtboxes, facing the player |

## Scrubbing & playback

- Tick slider, **-1/+1**, **play/pause**, speed popup (0.25×–2×).
- Pose mapping is the game's own: clip progress = `tick / DurationTicks` (equivalent to
  the server's `frameCount / DurationTicks` playback speed). A scrubbed frame is exactly
  the in-game frame at that tick.
- Facing-yaw slider rotates hurtboxes + hitboxes together (bone math is yaw-rotated,
  matching the server).
- Dummy toggle shows a posed opponent (idle frame 0) at a configurable distance; the
  dummy renderer is fully hidden when the toggle is off.
- Timeline: one bar per hitbox (trigger → trigger+duration), current tick marker,
  **Jump** button scrubs to the trigger tick. Live while playing.
- Airborne variant toggle → air specs (`AirLMB`/`AirRMB` + shared air slots).

## Hitbox editing

Per hitbox in the editor list: shape (sphere/capsule), trigger tick, duration, radius,
Off X/Y/Z, capsule End X/Y/Z, and a **bone dropdown** (every baked skeleton bone, plus
"entity (origin)"). Damage/stun/knockback display read-only (carried through unchanged).

Scene handles (active hitboxes only, scrub into a trigger window to grab them):
click a sphere to select, drag to move (capsule: drag either end), ring handle scales
radius. **+ Add hitbox** copies damage/stun/kb from the previous event; **Remove**
deletes; **Undo/Redo** cover all edits. Everything applies live to the preview.

Bone-attached hitboxes resolve against the **full baked bone set** — any baked bone is
attachable (`HitboxGeometry` looks up `BakedAnimationData.BoneNames` directly; the
hurtbox defs are only a fallback). The bake carries 9 curated mixamorig bones
(Head/Spine2/Hips/Hands/Feet/Toes) per character — see `SlopArenaBaker.humanBones`.

## Persistence — save to source (C#)

**Save to source (C#)** writes the edited stages straight into
`src/Shared/Characters/<Char>Data.cs` — the compiled source of truth. There is no
intermediate JSON. Apply:

```
dotnet build src/Shared/     # rebuilds the DLL, auto-copies to Unity Plugins
```

then start a match — server and client both read the compiled defs, so the edit is in
the real kit. No deploy step, no mirror.

The writer (`src/Shared/CSharpCharacterWriter.cs`) is a structural text transform, not
string replacement:

- Addresses stages by `(slot, airborne, stage)` → the C# property
  (`LMB`/`AirLMB`, `RMB`/`AirRMB`, `Slot1`/`AirSlot1`, `E`/`AirE`, `R`/`AirR`,
  `F`/`AirF`, `Slot2`–`Slot5`/`AirSlot2`–`AirSlot5`, `A`/`AirA`).
- Walks the file brace-balanced, skipping comments/strings; `Stages` never matches
  `ChargedStages`; single-letter properties (`A =`, `R =`) never match
  `BoneTrailDef` fields.
- Replaces exactly the target stage's `HitboxEvents` initializer with generated C# in
  the files' style (`new HitboxEvent[] { new() { … } }`); everything else in the file
  stays byte-identical — hand-tuned params, comments, descriptions are untouched.
- Handles `Array.Empty<HitboxEvent>()`, missing-property insert, and preserves zeroed
  Custom knockback (Nilus' deliberately INERT hitbox) on round-trip.
- **Revert edits** discards unsaved edits (preview snaps back to the last-built data).

## Semantics & limits

- Keys are delta-only: a save rewrites only stages you edited; untouched moves keep
  their authored data.
- Trigger tick 0 never fires (the stage-chain ticker increments before the trigger
  check) — the editor clamps new events to tick 1+.
- `ChargedStages` are **not editable** yet — the editor covers `Stages` only.
- Editing targets the selected `(slot, airborne, stage)` literally. Air slots 2–10
  share the ground spec in code (aliased fields); editing their air variant writes to
  the same underlying stage unless a separate air spec exists.
- No mid-match hot-swap: defs are bound at entity registration. Save → rebuild → next
  match (by design, sim-authoritative).
- Hurtboxes are display-only (the tool's editing scope is hitboxes). The hurtbox JSON
  loader (`HurtboxOverride`) remains supported if a file exists.

## Tests

`tests/Shared.Tests/CSharpCharacterWriterTests.cs` (622+ total green):

- Golden tests run against the **real** `MankiData.cs`: an edit changes exactly one
  block; identical blocks in different stages resolve by position; `ChargedStages`
  untouched.
- Formatting goldens (sphere/capsule/custom knockback/bone-attached/multi-event/empty),
  insert-into-empty-element, comment-with-commas splitting, unknown-property and
  out-of-range failures, and key round-trip (all slots × airborne × stage).

## Key files

| File | Role |
|---|---|
| `client/Unity/Assets/Scripts/Runtime/Tools/AbilityLab.cs` | rig: pose, display, editing state, save |
| `client/Unity/Assets/Scripts/Editor/AbilityLabWindow.cs` | window UI + scene handles |
| `src/Shared/CSharpCharacterWriter.cs` | source write-back (shared, tested) |
| `src/Shared/HitboxGeometry.cs` | hitbox position resolver (server + tool) |
| `src/Shared/ServerSimulation.cs` | hurtbox pose resolver (server + tool) |
| `src/Shared/Characters/*Data.cs` | the data files the tool writes |
