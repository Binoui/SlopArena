---
name: sloparena-animation-authoring
description: "Author and safely import SlopArena character animation clips through Blender, Unity Humanoid import, package catalog binding, cooking, and real-surface verification."
category: game-dev
---
# SlopArena Animation Authoring

Use this skill when creating or changing a character animation clip. It owns presentation assets and catalog bindings only. It does **not** change gameplay timing, damage, hitboxes, input, server simulation, or VFX behavior.

## Authority

```text
Blender source/action
  -> imported FBX and Unity .meta importer settings
  -> CharacterAssetCatalog.asset semantic binding
  -> inspect/cook generated package artifacts
  -> Ability Lab / Training presentation
```

`character.json` owns move timing in 60 Hz ticks. The animation clip must conform to that timing; do not retime gameplay to fit an animation without an explicit gameplay change.

`CharacterAssetCatalog.asset` owns the clip reference. The embedded Unity clip `fileID` is part of that binding and can change after an FBX reimport.

Raw FBX, Unity `.meta`, and catalog bindings are source. `content-cooked/` and generated animation catalogs are outputs; never hand-edit them.

## Before authoring

1. Identify the package, semantic animation ID, catalog entry, FBX asset, take name, clip duration/fps/frame range, and the ability event tick(s).
2. Map timing before posing:
  ```text
   clipFrame = tick / 60 * clipFps
   tick      = clipFrame / clipFps * 60
  ```

   Example: at 30 fps, frame 3 occurs at tick 6.
3. Record the current Unity import state: Humanoid/Generic rig mode, avatar validity, root-motion settings, clip name, and embedded clip `fileID`.
4. Preserve a byte-identical FBX and `.meta` backup before reimporting. Do not overwrite a working source asset without a recovery copy.
5. Define pose invariants: start/end neutral pose, no unapproved root translation, fire/active-frame silhouette, recoil/settle, and transition compatibility with the adjacent animation.

Before editing, write the intended presentation beats:

tick    frame    beat

0       0        entry

3       3        anticipation

6       6        maximum compression

8       8        launch

10      10       gameplay active/fire

14      14       recoil

22      22       settle

24      24       handoff

## Blender authoring

- Work from the known-good source rig/action. Keep the hierarchy, rest pose, scale, bone names, and take identity stable unless the change explicitly includes a rig migration.
- Pose a readable fighting-game arc: anticipation, active/impact, recoil or follow-through, settle. Favor clear silhouettes and short contrast over physically realistic weapon handling.
- Keep frame 0 and the intended handoff/end frame exact when the move blends from/to an existing pose.
- The Blender action timing is authoritative in seconds. Preserve the intended action duration and authored FPS. Unity's reported `AnimationClip.frameRate` is verification data, not permission to resample or retime the source.
- Treat root translation as gameplay-sensitive presentation data. Do not add it unless the move contract explicitly permits it.

## Unity import gate

1. Import the candidate FBX through Unity.
2. Inspect the imported clip on the actual character prefab, not only in Blender:
  - avatar and Humanoid import are valid;
  - mesh stays at expected scale and origin;
  - frame 0 and end-frame neutral poses are correct;
  - clip duration, fps, and binding count are expected;
  - fire, recoil, and settle frames read at gameplay camera scale.
3. Reject the candidate if Unity changes bind pose, root offset, scale, facing, or Avatar validity. Do not compensate with client-side transforms.
4. A Blender FBX export that fails this gate is not a valid delivery. Preserve the known-good source asset and diagnose the importer/rig contract first.

## Catalog cutover

After the import, resolve the imported clip's current embedded `fileID` and update the corresponding `CharacterAssetCatalog.asset` semantic binding if it changed. A correct GUID with a stale `fileID` is an invalid binding.

For Manki:

- Manki is package-native and cooked; preserve the package registry and presentation boundaries.
- Ground R uses semantic ID `anim.manki.gr`.
- Its binding is in `client/Unity/Assets/CharacterPackages/manki/CharacterAssetCatalog.asset`.
- Never assume an earlier imported `fileID` remains valid after reimport; read Unity's current importer metadata.

## Cook and verify

Run the supported Unity CLI flow from the repository root:

```bash
unity pipeline list --format json
unity command --project-path client/Unity recompile --format json
unity command --project-path client/Unity recompile_status --format json
unity command --project-path client/Unity get_console_logs --severity error --limit 20 --format json
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
```

Require all of the following:

- Unity Pipeline reachable;
- recompile reports no failure/errors;
- current Unity error console is empty;
- final package inspect reports `status: valid` and `dirtyOrStale: false`;
- source and cooked-source hashes match;
- the real character prefab shows the intended motion at start, anticipation, active, recoil, and end frames;
- Ability Lab or Training exercises the semantic animation through the normal presentation path when available.

Record Unity-facing verification in the ignored root `TESTING-UNITY.md`.

## Presentation acceptance:

- anticipation direction is readable before the active event;

- the active pose is visually distinct from anticipation and recoil;

- the primary action reads from the normal gameplay camera without relying on close-up detail;

- the character silhouette does not collapse behind its own limbs/weapon;

- pose contrast is prioritized over smooth interpolation;

- no unnecessary motion competes with the gameplay-critical action.

## Exceptional recovery: exporter incompatibility

Do **not** make binary FBX editing the normal pipeline.

It is permitted only when all of these are true:

1. Blender has authored the desired bone rotations;
2. Unity rejects or corrupts the normal exporter output through bind-pose, unit, root-offset, or Avatar failure;
3. a known-good FBX container with identical rig/take/import contract exists;
4. only verified live rotation-key values can be replaced without changing hierarchy, rest transforms, translation, scale, key timing, take identity, or binary structure;
5. the patched result passes every Unity import, catalog, cook, and prefab gate above.

Document the reason, exact validation evidence, and recovery artifact before using this route. Never use it to bypass a package/compiler diagnostic.