# Player Pushbox Collision

## Problem

Player hurtboxes currently exist only for attack hit detection. `ServerSimulation` moves each entity independently, then builds animation-driven hurtboxes for `SpellResolver`; no system prevents two players from occupying the same horizontal space or passing through one another.

Hurtboxes must remain attack geometry. Using animated limb hurtboxes as body collision would make movement depend on animation pose and would let an extended arm or weapon push another fighter.

## Goals

- Prevent active fighters from occupying the same body space.
- Prevent normal movement from passing through another fighter.
- Keep the result deterministic between authoritative and shared simulation runs.
- Keep attack hurtboxes and pushboxes separate.
- Preserve vertical movement and knockback behavior.

## Non-goals

- Full rigid-body physics.
- Collision between individual limbs.
- Stage geometry changes.
- Damage, hitstun, or attack priority changes.
- Client-side collision hacks.

## Geometry

Each fighter uses a stable vertical cylinder derived from the existing character definition:

- horizontal radius: `CharacterDefinition.CapsuleRadius`
- vertical span: `CharacterDefinition.CapsuleHeight`
- center: `(PX, PY, PZ)`

The cylinder is a movement pushbox, not an attack hurtbox. Bone-driven hurtboxes and legacy hurtbox capsules remain unchanged and continue to feed `SpellResolver` only.

Two pushboxes overlap when:

1. their vertical spans overlap; and
2. the horizontal distance between centers is less than the sum of their radii.

The first implementation uses horizontal circle separation. It does not attempt capsule-vs-capsule swept collision or stage-style continuous collision detection.

## Tick order

`ServerSimulation.Tick()` becomes:

1. pre-tick ability activation
2. target lock
3. independent movement simulation for every entity
4. pushbox resolution for every entity pair
5. warp-arrival processing
6. hurtbox construction
7. attack hit resolution
8. projectile explosions
9. blast/death processing

Pushbox resolution occurs after movement so every entity is corrected from the same tick snapshot, and before attack hit detection so attack geometry observes the final authoritative positions.

## Deterministic pair resolution

- Enumerate non-eliminated entities in ascending entity-ID order.
- Resolve each unordered pair once.
- Ignore pairs whose vertical spans do not overlap.
- For horizontal overlap, move both entities along the shortest horizontal separation vector.
- Split penetration equally when both entities are movable.
- If one entity is not movable, move only the other entity.
- For nearly coincident centers, use a deterministic fallback direction based on entity IDs rather than an undefined zero vector.

The correction is positional. It must not overwrite vertical position or velocity.

After correction, remove only horizontal velocity directed into the other pushbox. Preserve separating velocity, tangential velocity, vertical velocity, and knockback components that point away from the collision.

## State rules

Pushboxes apply to all non-eliminated fighters, including grounded, airborne, attacking, and hitstun states. This prevents a launched or airborne fighter from tunneling through another fighter while keeping the correction soft and local.

Pushbox correction must not:

- clear `KVY` or alter vertical launch behavior;
- change hitstun timers, damage, or attack state;
- create a hit result;
- use Unity physics or client-only state;
- run against eliminated spectators.

Warp/reposition movement is the exception. A warp ability must be able to reach its authored endpoint and must not become trapped behind a pushbox. Warp movement remains authoritative; pushbox resolution must either skip an entity while `WarpSpeed > 0` or treat that entity as non-movable for the correction. The implementation should use the existing warp state rather than adding an ability-specific flag.

## Boundary behavior

Pushboxes do not resolve against stage geometry. Existing stage collision remains responsible for ground, ledges, and arena surfaces. If pushbox correction moves a fighter horizontally outside the stage, the existing blast-line/death rules remain authoritative.

## Tests

Add shared simulation tests covering observable behavior:

1. Two grounded fighters moving toward one another stop at the sum of their radii.
2. Two overlapping fighters are separated deterministically.
3. Reversing registration/dictionary insertion order produces the same positions.
4. Non-overlapping fighters remain unchanged.
5. Vertical separation prevents horizontal pushbox correction.
6. Airborne fighters are also separated when their vertical spans overlap.
7. Knockback vertical velocity and hitstun remain unchanged after pushbox correction.
8. A warp entity is not blocked by pushbox correction.
9. Eliminated entities do not participate.
10. Attack hurtbox results remain independent of pushbox geometry.

## Proposed implementation seam

Add a focused pushbox resolver to shared simulation code, called once from `ServerSimulation.SimulateMovement()` after all per-entity `Simulation.SimulateTick()` calls and burst movement side effects. Keep the resolver pure with respect to pair math where practical, and keep state mutation in the server simulation layer.
