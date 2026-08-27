# Ability Lab

Ability Lab is an editor-facing authoring and preview tool.

## FightGuy

FightGuy preview loads the admitted `MatchContentCatalog` entry, its cooked package,
its `poses.bin` payload, and the generated Unity animation catalog/rig. Preview uses
that package authoritatively, so the lab matches Training, PvP, and GameServer
content.

The editable source document is `client/Unity/Assets/CharacterPackages/fightguy/character.json`.
Source JSON is editor input only. It is cooked deterministically into the four-file
runtime package under `content-cooked/fightguy/` and verified by package hashes.

Invalid drafts do not replace the last valid cooked artifact. The UI reports the
working draft as non-authoritative until the package cooks and verifies successfully.

## Agent workflow

Ability Lab and Pipeline commands use the same `CharacterPackageAuthoringService`.
Agents edit the source files directly, then invoke:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target fightguy --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target fightguy --format json
```

Inspect is read-only and reports package status, canonical slots, hashes, stale
reasons, and structured diagnostics. Cook publishes only a validated package.
Failed cooks return diagnostics without replacing last-valid artifacts or persisted
cook status.

## Legacy characters

Manki, Kistu, and Nilus remain on the legacy adapter while their packages are migrated.
Their existing animation-config and baked-data paths are editor/runtime compatibility
inputs only. FightGuy does not use those paths.

## Workflow

1. Edit the FightGuy authoring document or package-owned asset catalog.
2. Run the deterministic cook.
3. Verify the package manifest, hashes, bindings, and pose payload.
4. Reload the catalog entry in Ability Lab.
5. Scrub stages and confirm semantic animation and pose pairing.
