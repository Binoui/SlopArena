# Ability Lab

Ability Lab is a package-first UI Toolkit editor shell for authoring and inspecting fighter packages. The shell owns selection, preview status, diagnostics, stage/tick controls, and SceneView guidance. Package authoring and cooking remain in `AbilityLabPackageWorkspace` and `CharacterPackageAuthoringService`.

## Package workflow

1. Open `Tools → SlopArena → Ability Lab` with the Ability Lab scene component.
2. Select a source package from `Assets/CharacterPackages`. The selector displays the character display name and stable package ID, but stores only the package ID.
3. FightGuy opens through `AbilityLabPackagePreviewLoader.Load("fightguy")` with Ground `1` selected.
4. The rooted resolver checks staged `Application.streamingAssetsPath/content-cooked` first, then the repository `content-cooked` directory. It never depends on the process working directory.
5. The preview seam reads and verifies the cooked manifest, runtime definition, pose payload, generated animation catalog, and rig. It does not cook, write artifacts, update cook status, load `CharacterRegistry`, or infer a legacy selector.
6. With a valid package preview, scrub the stopped timeline at tick `0` or later. Edit Mode refreshes the existing renderer, baked bones, hurtboxes, and hitboxes in SceneView.
7. `SAVE + COOK` is the only persistence/cook path. Opening a package only inspects source and loads the last verified cooked artifact.

The compact toolbar status has this precedence: `No package → Unsaved → Cooking… → Cook failed → Stale → Cooked`. Clicking status opens the structured diagnostics panel. Hashes and raw IDs are shown only in Advanced.

## Authority boundary

Canonical package slot IDs are the persisted move identity. `CanonicalSlotProjection.All` exposes the sixteen read-only `SlotAddress` values in ground-then-air order, with input labels `1`, `2`, `3`, `4`, `A`, `E`, `R`, `F`. Human labels and the legacy `CharacterClass` selector are adapters only; see ADR-0030.

A stale source keeps the last verified cooked preview visible and shows stale diagnostics. Missing or invalid cooked content produces `Preview unavailable` with code, path, and message diagnostics. It never falls back to a C# character or legacy FightGuy selector. Missing generated catalog/rig bindings are reported at the preview seam, and the rig setup state distinguishes no scene rig, valid rig, and unavailable package preview.

## Compatibility mode

Manki, Kistu, Nilus, and other unmigrated content remain behind `LegacyCharacterCatalogAdapter`. Compatibility is a separate read-only UI shell with a persistent legacy-authority banner. It is the only Ability Lab path that may use legacy `CharacterClass` selection. Package mode has no package editing controls in Compatibility and never uses the legacy roster to resolve a package.

## Moves interaction

The Moves timeline supports snapped marker/body drags and hitbox endpoint resizing. Each release applies one immutable source edit and one workspace Undo snapshot; canceled or unchanged drags do not mutate the draft. The retained timeline caches its projection while scrubbing, supports `0.5×..4×` zoom with horizontal scrolling, and preserves inspector control identity. Root and timeline focus support Left/Right tick stepping, `Ctrl+S`, `Ctrl+Z`, `Ctrl+Shift+Z`, and Escape drag cancellation; text fields keep their normal editor shortcuts.

In package Edit Mode, active resolved hitboxes expose SceneView selection buttons and a radius handle. Radius changes commit through the same source workspace authority. Compatibility, unavailable previews, playback, and non-Moves pages expose no package handles or mutation path. Existing `AbilityLab.OnRenderObject` remains the only visible geometry path, including optional baked bones.

## Agent workflow

Inspect is read-only and reports source status, canonical slots, hashes, stale reasons, and structured diagnostics:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target fightguy --format json
```

Cook publishes only a validated package:

```bash
unity command --project-path client/Unity \
  sloparena.character.cook --target fightguy --format json
```

Failed cooks do not replace last-valid artifacts or persisted cook status. Do not delete or rename source packages or cooked artifacts while running the package/frontend self-tests.
