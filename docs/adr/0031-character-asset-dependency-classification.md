# ADR-0031: Character Asset Dependency Classification

**Status:** Accepted — 2026-08-28  
**Related:** ADR-0029 (Character Authoring and Cooking)

## Decision

Character asset catalog references are classified during authoring and cooking as `package`, `shared-approved`, `foreign`, or `missing`.

- `package` means the asset is below the target package root.
- `shared-approved` means the asset is an animation source below the project-owned `Assets/Art/Characters/shared/` registry root. The registry version is `1`.
- `foreign` means the asset belongs to another package or an unapproved project location. Foreign dependencies are errors and cannot cook.
- `missing` means no resolvable Unity asset path exists.

The classification, source package/path, and shared approval reason/version are reported in bind, inspect, dry-run, cook status, and verify results. The policy does not change the package format or make roster admission implicit.

## Consequences

Copied references from another package fail closed instead of silently becoming shared content. Shared presentation clips remain reusable without adding a general asset registry. Moving an asset across ownership roots changes the cook input classification and therefore the source hash.
