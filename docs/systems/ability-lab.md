# Ability Lab

Ability Lab is a package-first UI Toolkit editor shell for authoring and inspecting fighter
packages. The shell owns selection, preview status, diagnostics, stage/tick controls, and
SceneView guidance. Package authoring and cooking remain in `AbilityLabPackageWorkspace` and
`CharacterPackageAuthoringService`.

## Package discovery and workflow

1. Open `Tools → SlopArena → Ability Lab` with the Ability Lab scene component.
2. Select a source package from `Assets/CharacterPackages`. Discovery may show a source-only
   package by display name and stable package ID even when it has no cooked output or roster
   admission.
3. Open the package in package mode. Ability Lab inspects source and catalog state; opening
   or editing does not cook, write generated bindings, update cook status, or change roster
   rows.
4. The rooted resolver checks staged `Application.streamingAssetsPath/content-cooked` first,
   then the repository `content-cooked` directory. It never depends on the process working
   directory.
5. Authoritative persisted preview requires a verified cooked manifest, runtime definition, pose
   payload, generated animation catalog, and rig. Missing or invalid content shows
   `Preview unavailable` with structured code, path, and message diagnostics. It never
   falls back to FightGuy or legacy content. A source-only package such as Bonk is therefore
   discoverable but has no authoritative preview.
6. With a valid package preview, scrub the stopped timeline at tick `0` or later. Edit Mode
   refreshes the existing renderer, baked bones, hurtboxes, and hitboxes in SceneView.
7. `SAVE + COOK` remains the only persistence path and the only way to produce authoritative
   persisted preview/package output. A failed cook returns semantic failure and preserves the
   last valid artifact, generated cache, and status.
8. Editor Training and Solo Play have a separate `edit → Play` path. They compile the current
   source and semantic assets in memory into the existing cooked runtime/catalog types. Invalid
   source blocks match start; it never falls back to a persisted cooked package.

The compact toolbar status has this precedence: `No package → Unsaved → Cooking… → Cook
failed → Stale → Cooked`. Clicking status opens the structured diagnostics panel. Hashes and
raw IDs are shown only in Advanced.

## Authority boundary

Canonical package slot IDs are the persisted move identity. `CanonicalSlotProjection.All`
exposes the sixteen read-only `SlotAddress` values in ground-then-air order, with input
labels `1`, `2`, `3`, `4`, `A`, `E`, `R`, `F`. Human labels and the legacy `CharacterClass`
selector are adapters only; see ADR-0030.

A stale source keeps the last verified cooked preview visible and shows stale diagnostics.
Missing or invalid cooked content produces `Preview unavailable`. Missing generated
catalog/rig bindings are reported at the preview seam, and the rig setup state distinguishes
no scene rig, valid rig, and unavailable package preview.

## Compatibility mode

Manki, Kistu, Nilus, and other unmigrated content remain behind
`LegacyCharacterCatalogAdapter`. Compatibility is a separate read-only UI shell with a
persistent legacy-authority banner. It is the only Ability Lab path that may use legacy
`CharacterClass` selection. Package mode has no package editing controls in Compatibility and
never uses the legacy roster to resolve a package.

## Moves interaction

The Moves timeline supports snapped marker/body drags and hitbox endpoint resizing. Each
release applies one immutable source edit and one workspace Undo snapshot; canceled or
unchanged drags do not mutate the draft. The retained timeline caches its projection while
scrubbing, supports `0.5×..4×` zoom with horizontal scrolling, and preserves inspector
control identity. Root and timeline focus support Left/Right tick stepping, `Ctrl+S`,
`Ctrl+Z`, `Ctrl+Shift+Z`, and Escape drag cancellation; text fields keep their normal editor
shortcuts.

In package Edit Mode, active resolved hitboxes expose SceneView selection buttons and a radius
handle. Radius changes commit through the same source workspace authority. Compatibility,
unavailable previews, playback, and non-Moves pages expose no package handles or mutation
path. Existing `AbilityLab.OnRenderObject` remains the only visible geometry path, including
optional baked bones.

## Agent workflow

Inspect is read-only and reports source status, canonical slots, hashes, stale reasons, and
structured diagnostics:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
```

Cook publishes only a validated package:

```bash
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

Failed cooks do not replace last-valid artifacts or persisted cook status. Compatibility is
legacy-only. Do not delete or rename source packages or cooked artifacts while running the
package/frontend self-tests. Package creation currently has no Ability Lab UI control; use
the existing `AbilityLabPackageWorkspace.NewPackage` editor seam until onboarding is scoped.
