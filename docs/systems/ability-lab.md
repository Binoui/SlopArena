# Ability Lab

Ability Lab is a package-first UI Toolkit editor shell for authoring and inspecting fighter
packages. The shell owns selection, preview status, diagnostics, stage/tick controls, and
SceneView guidance. Package authoring and cooking remain in `AbilityLabPackageWorkspace` and
`CharacterPackageAuthoringService`.

## Agent-facing workspace commands

Typed Unity Pipeline commands expose package open, Shared two-fighter scenarios,
recorded-frame preview, read-only inspect, and PNG capture without temporary C#.
Use native commands first for gameplay setup and evidence; do not use `eval` to
construct fighters or emulate gameplay.

```bash
unity command --project-path client/Unity \
  sloparena.lab.open --target fightguy --format json
# Normal hit: close idle opponent; outcome is determined by Shared simulation.
unity command --project-path client/Unity \
  sloparena.lab.run --action ground.1 --ticks 60 --distance 1.2 \
  --opponent idle --format json
# Shield block, then a distant miss (no contact is a successful run).
unity command --project-path client/Unity \
  sloparena.lab.run --action ground.1 --ticks 60 --distance 1.2 \
  --opponent shield --format json
unity command --project-path client/Unity \
  sloparena.lab.run --action ground.1 --ticks 60 --distance 12 \
  --opponent idle --format json
# Shared grab against a nearby idle opponent.
unity command --project-path client/Unity \
  sloparena.lab.run --action grab --ticks 60 --distance 0.7 \
  --opponent idle --format json
unity command --project-path client/Unity \
  sloparena.lab.inspect --format json
```

The UI presents both fighters and scenario controls; native `run` options are
`--action <canonical-id|grab>`, `--ticks <last-frame>` (0–3600),
`--distance <metres>`, `--opponent idle|shield`, `--damage 0..999`, and
`--facing <relative-degrees>`. Defaults are 60, 2.5 m, idle, 0, and 180°.
Run outcomes are observed, never forced: a successful command can be a hit,
zero-damage block, miss, grab whiff, or capture/throw.

`preview --action <id|grab> --tick <frame>` seeks a recorded scenario when its
action matches the latest run. `capture` likewise consumes that same recorded
run and options when the action matches; it does not substitute defaults.
Without a matching run, grab preview/capture creates a default grab scenario,
not a copy of another action's options/horizon; canonical actions retain
ordinary authoring preview. Run explicitly first to choose opponent settings.
Capture frames into the ignored cache:

```bash
unity command --project-path client/Unity \
  sloparena.lab.preview --action grab --tick 7 --format json
unity command --project-path client/Unity \
  sloparena.lab.capture --action grab --ticks 7,19 \
  --output .ability-lab-cache/fightguy/grab --format json
```

Scenario frame indexes are zero-based: frame 0 contains the first input at
MatchTick 1. This is distinct from canonical authoring preview, whose requested
cumulative duration endpoint maps to the final authored stage tick
(`durationTicks - 1`). Scenario output contains `scenario.frames[]` with
`frameIndex`, `matchTick`, and both fighters' position, movement velocity,
knockback velocity, facing yaw (radians), damage, state/state ticks, grounded,
hitstop, hitstun, block stun, and interaction phase/IDs/timing. `contacts[]`
are accepted Shared hit/block records (including damage, blocked flag, impact
force, knockback direction/angle, stun/hitstop, and hit position); paired grab
capture/release appear in `interactions[]`, not fabricated hit contacts.
`presentationEvents[]` and `deaths[]` report observed Shared events and deaths.

For deeper collision diagnosis, the Shared simulation exposes the read-only
`ServerSimulation.LastTickAttackEntities` view of exact attack-collision
surfaces consumed on the latest pass (including active shields). It is a
diagnostic surface, not a separate simulation or a replacement for reported
accepted `contacts[]`.
Use semantic `data.result.success`, inspect diagnostics, and assess these
observations rather than treating command success as proof of a hit.

All mutating commands are Edit Mode only. Invalid options, unavailable/invalid
drafts, missing package/rig, and invalid action/frame return structured
diagnostics (for example `scenario.options.invalid`,
`scenario.action.unavailable`, `preview.tick.out-of-range`,
`lab.mode.unsupported`). Capture also rejects unsafe paths, symlinks, invalid
dimensions, duplicate ticks, and existing output files; a failed batch removes
its partial PNGs. Capture restores the prior scenario/cursor, playback,
visibility, camera, and render target on success or failure.

The workspace prepares current source in memory through existing compiler,
verified poses, catalog, and rig; it does not save, cook, change Undo history,
or write source/cooked files. Persisted authoritative preview still requires
valid cooked runtime/pose/catalog/rig content. Missing/invalid persisted
pose/rig prerequisites block scenarios. A clean in-memory prepared source can
report `dirty: false` while `authoritativePreview: false`; dirty describes
workspace edits, not whether a scenario hit. Never claim online play or game
feel from these isolated scenarios.

Authoring preview/capture retains canonical slot IDs and cumulative 60 Hz
ticks from move start, including zero and the requested duration endpoint;
that endpoint maps to the final authored stage tick (`durationTicks - 1`).
Capture `captures[]` reports action, requested tick, applied stage/local and
cumulative tick for authoring, or frame and MatchTick for scenarios, plus PNG
path. Root state is the restored workspace snapshot. Capture writes only below
repository-relative `.ability-lab-cache/`; batches allow at most 64 distinct
ticks and dimensions 64–4096. The compact Pipeline result is under
`data.result`; outer transport success is not semantic success.

See the [Unity CLI reference](../contributing/unity-cli.md) for command
discovery and native-command-first routing.

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

Nilus and other unmigrated content remain behind
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
