# Ability Lab (issue #119)

Visual hitbox/hurtbox authoring tool for SlopArena: a play-mode scene + editor window
that poses a character frame-by-frame through the **same Shared resolvers the server
uses**, shows server-accurate hurtboxes and hitboxes, and persists complete authored
character definitions as versioned JSON under `content/characters/<id>/character.json`.

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

## Persistence — versioned character JSON

The Shared serializer is `src/Shared/CharacterContentSerializer.cs`. The authored
FightGuy file is:

```text
content/
└── characters/
    └── fightguy/
        └── character.json
```

The Ability Lab loads `content/characters/<id>/character.json` before calculating
baked data, hurtbox display overrides, or preview state. FightGuy requires valid JSON
content; an invalid or missing FightGuy file is reported and does not fall back to
`FightGuyData.cs`. For other characters, a missing content file retains the existing
`CharacterRegistry.Get` fallback.

**Save JSON** clones the complete loaded definition through the serializer, applies
the working stage hitbox and hitstop edits, validates the edited definition, and
writes one deterministic full document. It then reloads the JSON and clears the
working overlays and undo/redo history. Saving an edit therefore preserves special
effect keys, parameter dictionaries, movement, animation settings, and unrelated
abilities; it does not rewrite C# source or require a Shared rebuild for Ability Lab
preview. Revert discards unsaved overlays and returns to the loaded JSON baseline.

`CharacterContentSerializer` uses schema version `1`, camel-case field names, string
enum values, indented output, and null-only omission. Zero and `false` authored values
remain present. It serializes the existing gameplay structs/classes directly, keeps
air ability aliases by reference identity, and emits the fixed ability slot order.
Load errors include the JSON field or file path where available. Missing
`schemaVersion`, missing `id`, unsupported versions, unknown enums, unknown ability
keys, null ability entries, invalid aliases, and missing ability stages are rejected;
the shared loader never silently falls back.

## Semantics & limits

- Stage edit keys remain `slot:airborne:stage`; the content ability names are the
  lower-camel slot identities (`slot1`, `airSlot1`, `e`, `airE`, and so on).
- Trigger tick 0 never fires (the stage-chain ticker increments before the trigger
  check) — the editor clamps new events to tick 1+.
- `ChargedStages` are not separately editable yet, but full-definition JSON save
  preserves them.
- Editing targets the selected `(slot, airborne, stage)` literally. Aliased air
  abilities share the ground object; distinct air specs remain distinct.
- No mid-match hot-swap: normal game/server runtime still consumes the compile-time
  `CharacterRegistry` and `FightGuyData.cs` fallback for this milestone. Save JSON
  affects the next Ability Lab reload; runtime registry/content adoption is deferred.
- Hurtboxes are display-only (the tool's editing scope is hitboxes). The existing
  `HurtboxOverride` loader remains supported if a file exists.
- Workshop packaging, semantic built-in capability IDs, and registry migration are
  explicitly outside this change.

## Tests

`tests/Shared.Tests/CharacterContentSerializerTests.cs` loads the real FightGuy JSON
and compares it with `CharacterRegistry.Get(CharacterClass.FightGuy)`. It covers base
data, movement and hurtboxes, representative normal hitboxes, air capsule data,
special effects and all special parameter dictionaries, alias identity,
deterministic byte output, invalid-content errors, and registration plus one
simulation tick.

## Key files

| File | Role |
|---|---|
| `client/Unity/Assets/AbilityLab/Runtime/AbilityLab.cs` | rig: load, pose, display, edit overlays, JSON save |
| `client/Unity/Assets/AbilityLab/Editor/AbilityLabWindow.cs` | window UI + scene handles + Save JSON |
| `src/Shared/CharacterContentSerializer.cs` | versioned deterministic JSON envelope and validation |
| `content/characters/fightguy/character.json` | authored FightGuy content |
| `src/Shared/HitboxGeometry.cs` | hitbox position resolver (server + tool) |
| `src/Shared/ServerSimulation.cs` | hurtbox pose resolver (server + tool) |
| `src/Shared/Characters/FightGuyData.cs` | compile-time runtime fallback/reference |
