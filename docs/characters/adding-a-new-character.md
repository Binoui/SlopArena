# Adding a Character

A character is a package, not a registry factory.

## 1. Create the package

Create the character package directory under `client/Unity/Assets/CharacterPackages/<Name>/`.
Add its Character Authoring Document and package-owned asset catalog. Use stable
semantic animation and bone IDs. Define slots as cooked timelines and declare every
internal capability requirement.

## 2. Bind assets

Bind the package rig and animation clips in the Character Asset Catalog. The generated
runtime animation catalog must contain the package ID, source hash, rig, semantic clips,
frame counts, and extrapolation metadata.

## 3. Cook and admit

Run the deterministic cook. It produces the runtime definition, client bindings, pose
tracks, and manifest. Add the package requirement to the cooked Built-In Roster
Manifest. Runtime catalog construction must reject identity, hash, capability, and
payload mismatches.

## 4. Verify

Run package verification, Shared tests, Shared build, and Server build. Exercise
Ability Lab, Training, and a local match. Confirm all surfaces resolve the same cooked
package hash and use the generated rig/catalog.

Legacy Manki, Kistu, and Nilus support remains behind `LegacyCharacterCatalogAdapter`
until their own package migrations. Do not add `Build<Name>`, registry overrides, raw
source loaders, manual FightGuy animation configs, or skeleton-bin runtime ownership.
