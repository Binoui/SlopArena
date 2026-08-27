# Combat Systems

This document describes mechanics shared by every SlopArena fighter. Character-specific kit data belongs in the character package; package execution is covered by [Ability Architecture](ability-architecture.md).

## Move model

Each fighter has twelve move concepts, each with grounded and aerial entries:

- normals `1`, `2`, `3`, and `4`;
- specials `A`, `E`, `R`, and `F`.

That produces the canonical 16-entry grid. `A` is the signature special, `E` is recovery-capable mobility, `R` is the playmaking special, and `F` is the long-cooldown power special. A package may alias entries in authoring, but cooking expands them into explicit runtime slots. Physical labels such as `LMB` and `RMB` are input mappings, not move identity.

Every move is a fixed-timeline entry with authored stage timing, animation IDs, and typed deterministic operations. The Shared simulation controls activation, duration, collision, interruption, and completion.

## Movement and resources

- Camera-relative 8-direction movement uses one ground Run tier; there is no selectable walk/sprint split.
- Jump and double jump use the character's movement definition. ShortHop is release-timed during the opening jump window.
- FastFall sets the configured downward speed while airborne and falling, except during Hitstun.
- Dash is a short Shift-triggered burst used for approach and evasion. Its opening ticks provide DashInvincibility; the dash tail is vulnerable. Grounded Dash hard-stops on expiry, while aerial Dash preserves momentum.
- LedgeHang is occupied and single-occupancy. Drop, ledge jump, and stand are explicit escapes.
- Each ability entry can be limited to one use per flight. Landing resets air-use counters.
- RecoveryMove is the per-character return-to-stage move. It is the only ordinary move that resets the FloatWindow mid-air.

All gameplay timing uses 60 Hz ticks. Values are authored in package data and resolved by Shared simulation.

## Damage and hit response

SlopArena uses damage percent rather than a conventional health pool. A hit applies damage, then Knockback using the hit's profile and the victim's current percent. Profiles cover Light, Medium, Launcher, Kill, Spike, and explicit Custom values.

- **Hitstun** is the victim's no-action duration after a hit. Inputs buffer according to the simulation rules; the victim's residual launch continues after Hitstun ends.
- **Hitstop** freezes the attacker/victim pair briefly while the match clock continues. Knockback starts when the freeze ends.
- **Combo Influence** is additive launch drift selected by the defender during Hitstop and Hitstun.
- **Clash** resolves simultaneous Interruptible hitboxes as mutual pushback and short stun instead of an arbitrary trade.
- **Burst** is a long per-entity cooldown. Defensively it breaks Hitstun and knockback with recovery; offensively it cancels the user's Duration Lock and emits a fixed-knockback extender.

Visual hit reactions, VFX, audio, and camera effects are presentation only. Damage and state transitions occur in Shared simulation.

## Duration locks and interruption

A Duration Lock prevents action during an authored move commitment. The engine owns interruption:

- IASA lets an authored stage accept a new ability from its configured tick onward;
- Hitstun, death, Burst, and simulation-owned overrides cancel active content through the cancellation path;
- landing lag applies to an aerial move unless the landing tick is in its auto-cancel window;
- a cancellation never depends on an authored cleanup operation that may not execute.

`IasaTicks = 0` and `LandingLagTicks = 0` preserve the default no-early-out/no-landing-commitment behavior. Auto-cancel windows are per air stage.

## Hitboxes and projectiles

The Shared resolver owns collision. A move can issue fixed-position, capsule, sphere, bone-attached, or projectile operations through its cooked timeline or an approved trusted capability. The resolver handles:

- facing-relative and cooked-pose bone positions;
- entity collision and owner-hit rules;
- projectile velocity, gravity, lifetime, and ground contact;
- explosion queues and lingering rehit zones.

No Unity physics query or client-only trajectory determines gameplay.

## Targeting and aiming

The client may provide camera-derived aim and target intent. The server validates targetability, range, and final state. Soft-lock selection favors an enemy near screen center and is used by supported abilities for targeting, camera behavior, and warp/recovery decisions. An ability's authored aim mode defines whether it uses facing, camera aim, or a target/zone representation.

Aim indicators and camera movement are visual input aids. They do not bypass server validation or replace Shared simulation.

## Design rules

- Give 3D attacks enough width, height, or depth to compensate for camera perspective, while preserving readable counterplay.
- Telegraph high-damage F moves with a wind-up so Dash and Burst decisions matter.
- Make aerial strength and recovery resources part of the fighter's tradeoff rather than granting every move unrestricted air use.
- Keep move behavior deterministic, bounded, and expressible through engine-owned primitives.

## Related docs

- [Ability Architecture](ability-architecture.md) — cooked timelines, typed operations, capabilities, and interruption ownership.
- [Character kit design principles](../characters/character-kit-design-principles.md) — role and counterplay guidance.
- [Adding a Character](../characters/adding-a-new-character.md) — package authoring and cooking.
- [Netcode Architecture](netcode-architecture.md) — server authority, prediction, and rollback.
- [Hitstun DI](hitstun-di.md) — detailed launch-drift design.
