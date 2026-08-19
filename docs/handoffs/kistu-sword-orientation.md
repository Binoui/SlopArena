# Resolution: Kistu sword orientation mismatch

**Status:** Resolved — 2026-08-19

## User-visible problem

Kistu's runtime sword angle did not match the katana in the source animation pack. G2 made
the failure clearest: instead of tracing the intended forward arc, the blade pointed sharply
down during parts of the swing. The animation pose itself and server hit registration were
not the cause.

## Root cause

The source katana hierarchy is:

```text
Unity_Grruzam_BaseModeling_Katana
└── root/.../hand_r
    └── ik_hand_gun
        └── Modeling_Weapon_katana_Blade
```

Kistu has `mixamorig:RightHand` but no `ik_hand_gun`. The initial implementation copied the
source helper's local rotation directly onto Kistu's hand:

```text
Kistu blade rotation = Kistu right-hand rotation * source helper local rotation
```

That multiplication crossed coordinate spaces. The source `hand_r` and Kistu's humanoid-
retargeted `RightHand` have different bind axes, so the same local helper quaternion means
a different world orientation. This affected the shared weapon attachment, not one animation.

The source and destination use the same katana mesh (`Grru_Katana`, blade axis `+Z`), ruling
out a mesh-axis mismatch. `WeaponAttach` and `SlopArenaBaker` also agreed with each other,
ruling out a separate runtime/bake calculation error.

## Final fix: bind-space conversion

Convert the source helper transform into Kistu's hand bind space once:

```text
basis          = inverse(kistuHandBindRotation) * sourceHandBindRotation
PositionOffset = basis * sourceHelperLocalPosition
RotationOffset = basis * sourceHelperLocalRotation
```

Resolved values in `client/Unity/Assets/Resources/WeaponConfigs/kistu.asset`:

```yaml
PositionOffset: {x: 0.030417653, y: 0.09876204, z: 0.05625506}
RotationOffset: {x: 327.94504, y: 269.47595, z: 3.9653988}
```

The transform is constant and applies to idle plus every Kistu attack. Runtime and baking
use the same operations:

```csharp
bladePosition = rightHand.TransformPoint(entry.PositionOffset);
bladeRotation = rightHand.rotation * Quaternion.Euler(entry.RotationOffset);
```

- `WeaponAttach.Update` applies this to the visible sword.
- `SlopArenaBaker` applies it to the mesh-derived `_weapon_hilt` and `_weapon_tip`.
- `HitboxGeometry` resolves those baked endpoints for authoritative sword capsules.

Re-baked outputs:

- `data/kistu_skeleton.bin`
- `client/Unity/Assets/StreamingAssets/data/kistu_skeleton.bin`
- `client/Unity/Assets/StreamingAssets/Server/data/kistu_skeleton.bin`

## Rejected fixes

### Copy `ik_hand_gun.localRotation`

Rejected because the quaternion is expressed in the source hand's axes, not Kistu's. A
helper child under Kistu's hand would still require the bind-space conversion.

### Tune one global Euler angle by eye

An experimental `(60, 160, -80)` improved G2 pitch but broke other moves. It treated the
symptom without identifying the coordinate-space mismatch.

### G2-only per-frame pose track

Implemented temporarily, then removed. It matched all 72 G2 source frames exactly but was
an overfit:

- the same hand-axis mismatch exists across the Kistu sword kit;
- it duplicated source animation data in `kistu.asset`;
- it copied source world-space hand motion and partially defeated humanoid retargeting;
- it added animation-name/time plumbing to a component that only needs a bind transform.

The correct seam is one source-to-destination bind conversion shared by all moves.

## Verification

An Editor audit sampled all eight Kistu attacks and independently recomputed the visible
sword's hilt and tip from the configured bind transform. It compared those values with the
re-baked authoritative endpoints:

```text
8 moves
454 sampled frames
worst endpoint discrepancy: 0.00001543 m (kistu_a_4 frame 63)
```

Focused regression command:

```text
dotnet test tests/Shared.Tests/ --no-restore \
  --filter 'FullyQualifiedName~KistuG2HitCharacterizationTests|FullyQualifiedName~KistuServerPoseRecordingTests|FullyQualifiedName~KistuAbilityTests|FullyQualifiedName~KistuDashSlashTests'
```

Result: **33 passed, 0 failed**. `dotnet build src/Shared/ --nologo` completed with no
warnings or errors, and Unity reported `COMPILE-OK`.

`kistu_a_2.FBX` has no `ik_hand_gun` curve bindings. It still uses the structurally correct
shared bind-space grip, but its authored visual pose requires explicit Unity playtest
coverage.

## General rule

For future humanoid weapon characters:

1. Identify the source hand and weapon-helper bind transforms.
2. Identify the destination attachment-bone bind transform.
3. Convert the helper into destination bind space; never copy its local Euler/quaternion.
4. Store one position and rotation in `WeaponAttachConfig`.
5. Use that exact transform in both `WeaponAttach` and `SlopArenaBaker`.
6. Re-bake and audit every weapon animation for visual/server endpoint agreement.

See `docs/systems/animation-system.md` for the permanent pipeline invariant.
