# ADR-0030: Ability Lab Canonical Slot Projection

**Status:** Accepted — 2026-08-27  
**Deciders:** @Binoui  
**Related:** ADR-0025 (Workshop Package Architecture), ADR-0027 (ModKit and Preview), ADR-0029 (Character Authoring and Cooking)

## Context

Character packages persist move identity as canonical slot IDs such as `ground.1` and `air.F`. Ability Lab needs the same IDs, order, airborne state, input labels, and cooked ordinals without duplicating the compiler's private list or introducing a second UI-owned mapping. Human-facing labels and the legacy `CharacterClass` selector are compatibility adapters, not package identity.

## Decision

`CanonicalSlotProjection.All` is the Shared, immutable, read-only projection of the sixteen canonical package slots. Each `SlotAddress` contains the canonical ID, airborne flag, input label, and cooked ordinal. The projection preserves the existing order and emitted cooked schema: eight ground slots (`1`, `2`, `3`, `4`, `A`, `E`, `R`, `F`) followed by the corresponding eight air slots.

The compiler derives its canonical slot string list from this projection. `CanonicalSlotProjection.TryGet` provides exact canonical-ID and airborne/input-label lookups for UI and other read-only consumers. The projection does not replace wire-level `AbilitySlots` constants or the compatibility `AbilityLab.SlotIndices` mapping.

Canonical package IDs remain the single persisted move identity. Human labels and legacy `CharacterClass` selectors are adapters only; they are not alternate persisted data.

## Consequences

Shared consumers and Ability Lab can display and select package slots without copying canonical identity rules. Compiler output remains byte-compatible because ordering and ordinals are unchanged. Consumers cannot mutate the projection, and unknown IDs or labels fail closed.
