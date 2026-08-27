# Unity Animation System

Animancer plays clips selected from authoritative server state. The runtime path is:

```text
MatchContentCatalog
  -> cooked FightGuy package + generated client catalog/rig
  -> PlayerRenderer
  -> Animancer
```

## Runtime ownership

FightGuy has one runtime owner: its verified cooked package under
`content-cooked/fightguy/`. The package contains the runtime definition, pose tracks,
client bindings, and manifest hashes. `MatchContentCatalog` admits the package and
keeps its immutable definition and baked pose projection together.

The generated Unity `CharacterAnimationCatalog` is a regenerable client binding. It
contains the package ID, source hash, rig reference, semantic animation IDs, frame
counts, and extrapolation metadata. Client setup rejects missing, duplicate, or
mismatched package bindings. Generated assets live under
`Resources/Generated/CharacterPackages/<package>/`.

Non-FightGuy characters remain on the temporary legacy policy: their catalog entries
may use `BakedDataPath` and `CharacterAnimationConfig`. That policy does not apply to
FightGuy. FightGuy never loads `Resources/AnimationConfigs/FightGuy`.

## Playback

`PlayerRenderer.ApplyServerState()` drives `UpdateAnimationState()`:

- idle/run use cooked presentation semantic IDs;
- jump, fall, dash, and hitstun use cooked presentation IDs;
- attacks use `(AttackSlot, ComboStage)` and cooked `AnimationNames`;
- the clip speed is `frameCount / DurationTicks`;
- generated extrapolation metadata controls behavior past clip length.

There is no AnimatorController, trigger table, or blend tree. Animancer clips are
played directly.

## Cooked poses

Server hitboxes use cooked pose tracks and semantic bone IDs. The client receives the
same `entry.BakedAnimation` payload used by the catalog entry. This keeps rendered
poses, bone-attached collision, and server timing on the same package hash.

## Weapon attachment

Weapon props use destination humanoid bones and the shared bind-space conversion
implemented by `WeaponAttachConfig` and `SlopArenaBaker`. Kistu remains the reference
for retargeted hand orientation.
