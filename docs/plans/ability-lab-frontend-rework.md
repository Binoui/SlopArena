# Ability Lab Frontend Rework

**Status:** Planned — design confirmed 2026-08-27
**Scope:** Human visual frontend over the Character Package authoring/cooking architecture.
**Decision:** [ADR-0029](../adr/0029-character-authoring-and-cooking.md); planned canonical Slot projection ADR.

## Execution tickets

Work is intentionally sequential because every slice evolves the same Ability Lab shell,
controller state, and preview bridge.

1. [#173 — Build package-aware Ability Lab foundation](https://github.com/Binoui/SlopArena/issues/173)
2. [#174 — Deliver the complete Moves tuning loop](https://github.com/Binoui/SlopArena/issues/174)
3. [#175 — Add Compatibility Preview mode](https://github.com/Binoui/SlopArena/issues/175)
4. [#176 — Add grouped Character authoring](https://github.com/Binoui/SlopArena/issues/176)
5. [#177 — Add Assets and Advanced surfaces](https://github.com/Binoui/SlopArena/issues/177)
6. [#178 — Finish Ability Lab polish and verification](https://github.com/Binoui/SlopArena/issues/178)

## Product goal

Ability Lab optimizes for:

> Pick a Move → see it → scrub it → inspect/tweak operations → save/cook.

The normal workflow hides hashes, JSON paths, semantic IDs, DTO names, compiler terms,
and package schema details. Those remain available in Advanced/debug surfaces.

## Domain language

- **Slot:** canonical persisted move entry. The package has 16 entries: ground/air ×
  `1`, `2`, `3`, `4`, `A`, `E`, `R`, `F`.
- **Move:** human-facing Ability Lab label for a Slot. It is not a second persisted model.
- **Working Draft:** current editable source state. It may be unsaved or invalid.
- **Authoritative Preview:** preview loaded from the last successfully cooked package.
- **Compatibility Preview:** read-only preview of a legacy character through the adapter.
- **Ability:** human-facing label for a typed capability operation.
- **Source Conflict:** loaded source differs from files changed externally; saving is blocked.

## Authority rules

```text
package.json + character.json + CharacterAssetCatalog.asset
                              │
                              ▼
              CharacterPackageAuthoringService
                              │
                              ▼
                validation / deterministic cooking
                              │
                              ▼
                    immutable cooked package
```

Ability Lab is a human frontend over this path. It is not a second authoring API,
compiler, cooker, runtime, or package format.

- Source editing uses `AbilityLabPackageWorkspace` typed edit methods.
- Cooking uses `CharacterPackageAuthoringService`.
- Package preview uses a read-only verified preview-load seam.
- Package selection uses canonical Slot IDs.
- Legacy `CharacterClass`/`AbilitySpec` translation remains at the compatibility adapter boundary.
- No UI-specific gameplay data is persisted.
- No UI writes cooked artifacts directly.

## Final information architecture

Top-level tabs:

```text
Moves | Character | Assets | Compatibility | Advanced
```

Moves is the default tab.

### Global toolbar

```text
Package: FightGuy ▾   ● Cooked   Undo   Redo   SAVE + COOK
```

Toolbar status precedence:

```text
No package → Unsaved → Cooking… → Cook failed → Stale → Cooked
```

Clicking status opens an inline collapsible diagnostics panel. Detailed hashes and
compiler terminology stay in Advanced.

### Moves

```text
┌─────────────────────────────────────────────────────────────┐
│ Toolbar / package / status                                  │
├──────────────┬──────────────────────────────┬───────────────┤
│ MoveSelector │ Preview bridge / status       │ Inspector     │
│              │ External SceneView guidance   │               │
├──────────────┴──────────────────────────────┴───────────────┤
│ Timeline                                                    │
└─────────────────────────────────────────────────────────────┘
```

- Ground/Air buttons use the canonical 16 Slot projection.
- FightGuy opens with Ground 1 selected.
- The center is a preview/status bridge, not a second 3D renderer.
- The actual 3D preview remains the existing Ability Lab rig and SceneView.
- The timeline spans the full width below the three-column body.

### Character

Grouped foldouts:

- General;
- Movement: Ground, Air, Jump, Falling;
- Presentation;
- Hurtboxes, read-only for this rework.

Use precise numeric fields. Use sliders only where meaningful bounds exist.

### Assets

Grouped catalog bindings:

- Rig;
- Locomotion;
- Hit Reactions;
- canonical Move animations.

Use Unity ObjectFields and drag/drop. The Character Asset Catalog remains authoritative.
No asset generation or automatic animation selection.

### Compatibility

Separate read-only Compatibility Preview mode for Manki, Kistu, and Nilus.
It retains the existing legacy Play Mode lifecycle and shows a persistent authority banner.
No package source editor or SAVE + COOK controls appear here.

### Advanced

Expose:

- package paths and raw package ID;
- source/cooked/package hashes;
- raw semantic IDs;
- detailed diagnostics and provenance;
- compiler/cooker profile and schema data;
- semantic-ID rename;
- schema migration actions with explicit confirmation.

## UXML and USS structure

Create one editable root and four focused templates:

```text
AbilityLabWindow.uxml
AbilityLabToolbar.uxml
MoveSelector.uxml
MoveTimeline.uxml
Inspector.uxml
AbilityLabWindow.uss
```

`AbilityLabWindow.uxml` owns the root, tab strip, pages, diagnostics panel, Moves layout,
and later-mode shells. Character, Assets, Compatibility, and Advanced remain root-owned
shells until their phases implement content.

Use nested UI Toolkit split views:

```text
MoveSelector | MovesContent
              PreviewBridge | Inspector
```

Keep widths, gaps, padding, status colors, timeline height, and breakpoint behavior in
USS. Do not hard-code pixel layout in C#.

Narrow layouts keep the selector and preview visible, then stack the inspector below.

## C# responsibilities

### AbilityLabWindow

Rewrite the IMGUI surface as a UI Toolkit `EditorWindow` using `CreateGUI()`.

Own:

- UXML/USS loading;
- named-element binding;
- tab state;
- package/move/operation selection;
- ephemeral view-model state;
- keyboard shortcuts;
- targeted refresh and SceneView repaint.

Do not put compiler, cooker, artifact, or source-persistence logic here.

### Editor view-model

Ephemeral state only:

```text
selectedTab
selectedPackageId
selectedSlotId
selectedStageIndex
selectedOperationIndex
currentCumulativeTick
playing
diagnosticsExpanded
timelineZoom
```

Selection identity is canonical Slot ID plus source stage/operation indices.
Do not retain source object references across immutable workspace edits.

### Shared projection

Add a read-only Shared projection with immutable `SlotAddress` descriptors:

- canonical ID;
- ground/air flag;
- input label;
- ordinal.

Add focused Shared tests and a short ADR. Do not change cooked schema, wire format,
simulation semantics, or package identity.

### Package preview context

`LoadPreview(target)` returns:

- immutable cooked Character Definition;
- baked poses;
- generated animation catalog;
- rig;
- package identity and hashes;
- canonical Slot projection;
- structured diagnostics.

It never cooks or writes. Missing or invalid content returns a structured unavailable
result so the Working Draft can still be repaired.

### AbilityLab runtime

Add separate package and compatibility contexts.

Package context uses canonical Slot IDs and cooked package data. Compatibility retains
legacy selectors and indices behind the compatibility boundary.

Package preview and scrubbing become Edit Mode capable through the existing `ExecuteAlways`
component. Keep one renderer and one shared geometry path. Guard scene/asset lifecycle
operations carefully.

Playback opens stopped at tick 0. Play is explicit. Scrubbing works without Play Mode.
Compatibility remains on its existing Play Mode path.

### Local content resolver

Extract a Unity client/editor utility that returns rooted content locations, resolved
manifests, package roots, and structured diagnostics. Use it from `ClientSession`,
Compatibility Preview, and package preview loading.

Package mode does not require roster loading. Compatibility mode uses the resolver rather
than a cwd-sensitive relative path.

## Timeline

The timeline is an immutable projection of the Character Authoring Document.

Each operation carries:

- operation type;
- authored tick/range;
- friendly summary;
- source stage/operation address.

Use one cumulative `[0, duration]` axis. Playable ticks remain `0…duration−1`.
Operation ranges use half-open `[tick, tick + duration)` intervals.

Multi-stage moves show cumulative stage boundaries. Single-stage moves show no Stage 0
chrome.

Use a custom `MoveTimelineElement` with `generateVisualContent` and pointer events.

Phase 1 supports:

- scrub click/drag;
- operation selection;
- jump to authored operation tick;
- hitbox bars;
- typed markers;
- stage boundaries;
- tooltips and friendly summaries;
- SceneView highlight.

Phase 1 does not support timeline drag retiming or duration endpoint dragging.
Those belong to polish.

Timeline selection priority is deterministic: operation row, then hitbox bar/marker,
then background/tick selection.

## Inspector

Use a small built-in handler registry, not a reflection/plugin framework.

Handlers cover Move, Hitbox, Ability, Projectile, Movement, Presentation Event, and
completion markers.

Phase 1 edits Move and Hitbox handlers. Other operations display read-only typed summaries.

### Move inspector

Show:

- friendly Move name;
- stage timing;
- IASA;
- landing lag;
- auto-cancel values;
- catalog-derived animation selectors.

### Hitbox inspector

Group:

- timing: trigger tick and active duration;
- combat: damage, angle, base knockback, growth, stun, interruptibility, hit group;
- shape: sphere/capsule and radius;
- attachment: start/end bones and offsets.

Commit text edits on Enter or focus-out. Each completed edit creates one workspace undo
snapshot. Timeline drag interactions later use begin/end transactions.

Friendly labels derive from the Character Asset Catalog, baked skeleton, rig metadata,
and source declarations. Unknown values remain `Unknown (raw-id)` and never silently
remap.

## Save and preview state

Normal flow:

```text
edit Working Draft
      ↓
SAVE + COOK
      ↓
persist source
      ↓
canonical service cook
      ↓
refresh preview only on success
```

If cooking fails:

- retain the source edit on disk;
- retain the last Authoritative Preview;
- retain cooked artifacts and persisted status;
- show `Cook failed`;
- expose grouped diagnostics;
- allow repair and retry.

If source changes externally while the workspace is loaded:

- show Source Conflict;
- block save;
- offer Reload/Revert;
- never auto-merge or use last-writer-wins.

## Phases

### Foundation

Canonical projection, ADR, local resolver, preview-load seam, package context, Edit Mode
package preview, UXML/USS shell, tabs, toolbar, diagnostics, rig setup, and stale-doc
reconciliation.

### Moves

Canonical selector, Ground 1 default, source timeline projection, scrub/select, SceneView
highlight, Move/Hitbox inspectors, typed edits, undo, SAVE + COOK, diagnostics, and status.

### Polish

Timeline retiming, duration resizing, grouped drag undo, stronger SceneView manipulation,
zoom, responsive behavior, keyboard/focus polish, and bounded redraw performance.

### Character

Grouped General/Movement/Presentation controls and read-only Hurtboxes.

### Assets and Advanced

Friendly catalog bindings, rig/animation ObjectFields, hashes, raw IDs, diagnostics,
semantic-ID rename, and explicit schema migration actions.

## Testing

### Shared

Test canonical Slot order/address mapping, Ground/Air labels, cumulative stage offsets,
half-open operation ranges, aliases, and missing-slot behavior.

### Unity Editor

Use targeted tests for:

- UXML hierarchy and named controls;
- tab and package selection state;
- Ground 1 default;
- source timeline projection;
- inspector edits and undo;
- real catalog/rig/binding resolution;
- preview loading and replacement;
- SAVE + COOK success/failure;
- last-valid preservation;
- source conflicts;
- Compatibility read-only behavior.

Avoid pixel/layout snapshot tests.

## Manual verification

Run after Foundation and before each phase closes.

Check:

- normal wide window;
- narrow docked window;
- tall window;
- Ability Lab beside SceneView;
- Ground 1 readability;
- timeline tick/range readability;
- inspector spacing;
- dropdown clipping;
- scrolling;
- keyboard focus and shortcuts;
- Cooked, Unsaved, Stale, and Cook failed states;
- diagnostics panel;
- unavailable preview repair state;
- Compatibility Preview banner;
- SceneView selection/highlight;
- no new console errors.

## Non-goals

- server/netcode changes;
- cooked schema or wire-format redesign;
- new ability runtime;
- generic modding CLI;
- visual scripting;
- arbitrary expressions/branches;
- asset generation/import automation;
- automatic animation selection;
- Workshop publishing/version redesign;
- editable Hurtboxes;
- embedded custom 3D renderer;
- full professional animation-editor behavior;
- Phase 1 timeline drag retiming;
- full legacy editing;
- pixel-perfect UI test suite;
- per-property mutation commands;
- inspector plugin framework.
