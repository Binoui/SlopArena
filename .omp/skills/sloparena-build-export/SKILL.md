---
name: sloparena-build-export
description: Ships compatible SlopArena clients and backend releases. Use when user says 'release on Steam/VPS', 'build a release', 'deploy the servers', or 'publish SlopArena'. Not for ordinary Shared tests or Unity compilation.
---

# SlopArena Build, Export & Release

Act as the release operator. Deliver a compatible client/backend release with
observed live identities and a receipt another operator can resume without this
conversation. Build, image publication, VPS deployment, Steam upload and branch
activation are separate outcomes; do not report one as proof of another.

## Resolution rules

- `{game-root}` is the approved canonical game checkout. An explicit worktree wins;
  otherwise use the current game checkout or resolve the workspace's `SlopArena/`
  link. Never substitute main for an invalid explicit checkout.
- `{planning-root}` is the SlopArena workspace, separate from the game and Master
  repositories. Repository commands run with the corresponding checkout as `cwd`.
- Project paths below resolve from `{game-root}`. Load its instructions first;
  resolve the separate Master checkout and its instructions when publishing it.

## Default: Steam Playtest + VPS

Read `{game-root}/docs/systems/release-pipeline.md` for the current end-to-end
procedure and `{game-root}/deploy/vps/README.md` for guarded deployment/recovery.
Use `{game-root}/docs/testing.md` for applicable verification, not historical test
counts or a mandatory unrelated runtime pass.

Steam Playtest AppID is **5325920**, depot **5325921**. Use one operator release
ID across GameHost, Master and migration images, with exact source revisions and
published digests. Keep the client version, Steam BuildID, depot manifest and
live branch observation in `{game-root}/build/playtest/<version>/candidate.json`.
Load the previous receipt to recover paths/identities, then recheck live state;
its readiness, match count and authentication observations are not current proof.

- Ship only the approved source snapshot. Preserve unrelated edits; release
  approval does not implicitly authorize commits, pushes or installations.
- Coordinate saved-source writers. Live Unity commands use the canonical gateway
  in `{planning-root}/scripts/unity-editor-gateway.ts`, your own runtime-issued
  `ORCA_TERMINAL_HANDLE`, and the current coordination protocol. Missing identity,
  blocked ownership or ambiguous settlement is a blocker, not permission.
- An open Editor can build through native gateway `build`/`build_status` commands.
  Shared plugin copies and external staging require your own bounded hold.
  Never nest a claim inside that hold. Offline batchmode/ZIP builds require a
  separately authorized, confirmed-closed project; never close another owner's
  Editor to clear its lock.
- Verify current cooked-package freshness, exact roster/payload bytes, compiled
  Master endpoint, Shared/native library identity, attribution and secret/dev-file
  exclusions. Back up existing local staging and restore it after settlement.
- Credentials stay in the operator's visible local terminals/private VPS files.
  SSH-key unlock, VPS sudo, Steam login and mobile approval are distinct gates;
  cached Steamworks browser authentication does not prove SteamCMD login works.
- A changed roster needs the new catalog hash in private Master admission before
  deployment. Prepare a separate release-specific private Master environment and
  reference it in the candidate's `runtime.master_env_file`; preserve every
  credential/unrelated line and leave the previous release's file untouched.
  Automatic recovery restores file paths, not overwritten private contents.
  Never copy these environments into a release receipt.
- Require a successful fresh backup and a fresh zero-active-match observation
  immediately before guarded replacement. `release.py` does not enforce the
  zero-match operator gate. Never activate Steam default before compatible VPS
  readiness and registration are observed.
- `scripts/steam-playtest.sh <version> --upload` uploads only; it does not set a
  branch live. Activate the exact BuildID in authenticated Steamworks, complete
  any mobile confirmation, then observe the branch and depot manifest again.
  Crop screenshots to release proof; branch passwords/account settings stay out.

## Explicit alternate targets

For a ZIP/GitHub release or the Alfred home backend, use the corresponding
sections of `{game-root}/docs/systems/release-pipeline.md` and
`{game-root}/docs/systems/production-hosting.md`. Never apply home public-UDP or
rsync instructions to the Steam-only VPS. Current GameServer binaries retry
transient Master failures; a Master restart alone is not a reason to restart
GameHost. `build-release.sh` restores its saved settings/staging on normal exit,
including failure; do not blanket-reset the user's tree.

## Completion

Record source/image pins, package/catalog identity, observed Steam branch and
manifest, VPS readiness/registration, backup/schema results and any unexercised
player flow. Preserve resumable state at a human authentication gate; never mark
an uploaded but inactive build or a prepared but undeployed image as live.
Clean task-owned staging/helpers and close task-owned privileged sessions once
settled. Build/backend checks do not prove a packaged two-account match/rematch;
state that limit unless the actual player flow was exercised.
