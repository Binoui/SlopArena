# Character Import Checklist

## Authoring

- [ ] Create a Character Authoring Document with stable package ID and schema version.
- [ ] Define movement, collision, presentation semantic IDs, slots, timelines, and
      capability requirements in the document.
- [ ] Keep editable character assets under the character package directory.
- [ ] Record rig and animation bindings in the Character Asset Catalog.

## Cook

- [ ] Run the deterministic character cook.
- [ ] Confirm the runtime definition, client bindings, pose payload, and package
      manifest are generated together.
- [ ] Confirm source hash, cooked-content hash, package hash, and capability
      requirements are consistent.
- [ ] Confirm all required semantic animations and rig bindings exist.

## Runtime admission

- [ ] Add the package requirement to the Built-In Roster Manifest.
- [ ] Verify the four committed package files byte-for-byte.
- [ ] Verify a fresh `MatchContentCatalog` resolves the package by identity.
- [ ] Confirm missing, tampered, or mismatched payloads fail closed.
- [ ] Confirm client runtime resolves the generated catalog by package ID and hash.

## Verification

- [ ] Run `scripts/verify-fightguy-package.sh` for FightGuy or the equivalent package
      verifier for a new character.
- [ ] Build Shared and Server.
- [ ] Run the Shared test suite.
- [ ] Test Ability Lab, Training, and a local match with the same cooked package hash.
