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
- FastFall requires a fresh Down press while already descending; it sets and latches the configured downward speed until landing or interruption. Release preserves the latch; ascent/apex and locked/owned presses are discarded. Ordinary aerial attacks may coexist, but Hitstop, Hitstun, ledges and authored vertical motion prevent activation. FastFall overrides any active gravity window.
- Shield is a permanent, full-body grounded defense held with Left Shift / RT. It stops ordinary melee, projectile and explosion attacks from every direction, freezes combat facing, and enters a vulnerable 7-tick drop after release. Grab is C or the controller LB+RT chord.
- LedgeHang is occupied and single-occupancy. Drop, ledge jump, and stand are explicit escapes.
- Each ability entry can be limited to one use per flight. Landing resets air-use counters.
- RecoveryMove is the per-character return-to-stage move. It is the only ordinary move that resets the FloatWindow mid-air.

All gameplay timing uses 60 Hz ticks. Values are authored in package data and resolved by Shared simulation.

### Contextual Down

Down (X by default) is independent of backward movement. Held Down enters Crouch on an
eligible ground tick; a fresh edge at ≥ .65× RunSpeed enters Slide. A held-Down actionable
landing enters Slide at ≥ .25× RunSpeed, after collision, ability termination and landing
lag. Locked landings bank no slide. Zero-speed landings never gain propulsion.

Slide caps live horizontal magnitude at 1.25× RunSpeed, then decays by 2× RunSpeed per
second without steering. Below .15× RunSpeed it becomes Crouch. The landing contact tick
adds neither a second integration nor slide friction. Crouch brakes at 36 units/s² and
snaps below .015; release restores ordinary control that tick. Slide freezes Rush and
preserves LastDir. Walk-off clears low posture without automatically fast-falling.

SlideJump uses ordinary jump squat, ShortHop and resources. Its takeoff cap is 1.15×
RunSpeed versus ordinary RunSpeed; caps never refill velocity or restore wall-blocked
components. Grounded mobility uses normal movement; character-specific mobility stays in kit abilities.

All four roster packages opt their grounded normals 1–4 into `allowSlideCarry`.
Activation switches immediately to the attack pose, caps existing Slide momentum at
RunSpeed and decays it by 6× RunSpeed per second. Carry freezes during Hitstop and clears
on completion, cancellation, hit or motion takeover. Normal attack locks remain intact.

Default max-fall/fast-fall speeds: FightGuy 14/22, Manki 13.5/21, Bonk 15/24, Kistu 14/22.
The report's historical-fall and 3× slide-friction controls do not change these defaults.

### Settled CrouchBrace

Settlement requires one complete unfrozen, eligible, zero-horizontal-velocity Crouch tick.
An accepted hit snapshots settlement and a valid authored/baked low pose before reaction
cleanup. Ordinary formula launch receives a .90 multiplier on its final vector, after
Hitstun calculation and before directional influence. Damage, Hitstop and Hitstun are
unchanged. Deferred launch applies the captured factor once after Hitstop; late Down does
not earn it and release does not revoke it. Replacement hits and block contact clear/replace the queue. Scripted force, direct velocity overrides and pulls are excluded.

Roster low postures use real static Humanoid poses and matching baked hurtboxes, not
smaller stage capsules. Missing legacy low poses remain upright and cannot earn brace.

### Measured flow report

`dotnet run --project tools/AerialApproachReport -- --slide-flow --slide-deceleration 2
--fall-profile current --brace-multiplier 0.9 --assert --out artifacts/slide-flow/combat`
writes per-tick traces, aerial approaches, follow-ups and resolver target outcomes.
Use `3` for the friction control, `previous` for historical fall speeds and `1` to disable
brace in the report only. Each profile measures 140,640 movement scenarios plus recovery
ownership controls, ten-cycle resource routes and 1,152 finite response scenarios.
`status=complete` in target rows means the ground-normal follow-up ended; inspect
`aerialAccepted` and `slideStartTick` to distinguish a full aerial/slide route from
interruption or a crouched landing. These are scenario measurements, not balance proof.

## Damage and hit response

SlopArena uses damage percent rather than a conventional health pool. A hit applies damage, then Knockback using the hit's profile and the victim's current percent. Profiles cover Light, Medium, Launcher, Kill, Spike, and explicit Custom values.

- **Hitstun** is the victim's no-action duration after a damaging hit. Inputs buffer according to the simulation rules; residual launch continues after Hitstun ends.
- **Hitstop** freezes the attacker/victim pair briefly on contact while the match clock continues. Block hitstop does not enter damaging-hit launch/DI logic.
- **Block stun** locks a defender after a blocked contact for `clamp(ceil(0.6 × incoming damage) + 2, 4, 15)` ticks; overlaps retain the larger remaining duration.
- **Shield drop** lasts 7 vulnerable ticks after release; release during block stun starts the full drop only when the stun expires.
- **Clash** resolves simultaneous Interruptible hitboxes as mutual pushback and short stun instead of an arbitrary trade.

Visual hit reactions, VFX, audio, and camera effects are presentation only. Damage and state transitions occur in Shared simulation.

## Duration locks and interruption

A Duration Lock prevents action during an authored move commitment. The engine owns interruption:

- IASA lets an authored stage accept a new ability from its configured tick onward;
- grounded normals also accept directional movement at that same pre-tick IASA boundary; movement cancels the current activation and its remaining hitboxes rather than steering an active attack;
- explicit ability input takes priority over movement cancellation; hitstop, Hitstun, block stun, and landing lag still block cancellation, and jumping retains its existing full-lock gate;
- Hitstun, death, and simulation-owned overrides cancel active content through the cancellation path;
- landing lag applies to an aerial move unless the landing tick is in its auto-cancel window;
- a cancellation never depends on an authored cleanup operation that may not execute.

`IasaTicks = 0` and `LandingLagTicks = 0` preserve the default no-early-out/no-landing-commitment behavior. Auto-cancel windows are per air stage.

During positive grounded landing lag, Unity blends the current aerial pose into idle
over the remaining replicated `LandingLagTicks / 60` seconds. Ordinary and
auto-cancelled landings do not trigger the blend. Hitstun interrupts it, and zero
landing lag returns animation ownership to locomotion immediately. This presentation
never extends the gameplay lock.

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

All roster normals (`ground.1–4` and `air.1–4`) enable target-facing rotation. Specials opt in per move. Shared snaps enabled attacks toward the resolved enemy while locked on; without lock-on, `TrackingStrength` is the fraction of the shortest yaw difference closed per 60 Hz tick, not a per-second rate. Hitstop pauses attack tracking. Unity renders the resulting authoritative facing.

## Design rules

- Give 3D attacks enough width, height, or depth to compensate for camera perspective, while preserving readable counterplay.
- Telegraph high-damage F moves with a wind-up so Shield and movement decisions matter.
- Make aerial strength and recovery resources part of the fighter's tradeoff rather than granting every move unrestricted air use.
- Keep move behavior deterministic, bounded, and expressible through engine-owned primitives.

## Related docs

- [Ability Architecture](ability-architecture.md) — cooked timelines, typed operations, capabilities, and interruption ownership.
- [Character kit design principles](../characters/character-kit-design-principles.md) — role and counterplay guidance.
- [Adding a Character](../characters/adding-a-new-character.md) — package authoring and cooking.
- [Netcode Architecture](netcode-architecture.md) — server authority, prediction, and rollback.
- [Hitstun DI](hitstun-di.md) — detailed launch-drift design.
