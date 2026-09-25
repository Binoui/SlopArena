# Release Pipeline — cutting a SlopArena demo release

## Version scheme

`v<major>.<minor>.<patch>-demo.<n>` (e.g. `v0.2.0-demo.1`). The `-demo`
suffix marks friends-only releases.

## What the pipeline produces

| Artifact | Where | Contents |
|---|---|---|
| `build/release/SlopArena-<version>.zip` | dev machine | Windows player `.exe`, bundled self-contained game server (`StreamingAssets/Server/`), arenas, `README.txt`, `HOSTING.txt` |
| `build/minipc/` | dev machine | linux-x64 framework-dependent game server (rsync'd to alfred) |
| Master server | alfred via rsync | published separately (see below) |

## Steps

### 1. Preflight

```bash
git checkout main && git pull --ff-only
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo     # CI runs this too
```

### 2. Build the zip

```bash
scripts/build-release.sh 0.2.0-demo.1
# requires the Unity editor (6000.0.78f1) — ~10 min batch build.
# Output: build/release/SlopArena-0.2.0-demo.1.zip
```

The script builds Shared + tests, publishes the self-contained Windows server
(embedded host-and-play), publishes the linux-x64 server for the mini PC,
stages the arenas plus every package named by the cooked roster manifest,
stamps `bundleVersion`, runs the Unity Windows player build,
then restores `ProjectSettings.asset` and unstages `StreamingAssets/`.

Both client and server staging trees contain the roster manifest and all four
payloads (`manifest.json`, `character.runtime.json`, `poses.bin`, and
`client.bindings`) for every admitted package: Manki, FightGuy, Kistu, and Bonk.
The scripts derive package IDs from `content-cooked/roster/manifest.json`;
they do not maintain a separate character list.

Raw authoring JSON, manual animation configs, and skeleton source files are not
release inputs.

> The version stamp is reverted via `git checkout` of ProjectSettings.asset —
> the script refuses to run if that file has uncommitted changes.

### 3. Refresh the official game server (mini PC)

Optional if only the client changed. See `docs/systems/production-hosting.md`
("Redeploy game server"). Restart `server-1` after replacing its binaries;
new GameServer binaries recover registration after Master restarts without
a manual restart.

### 4. Publish to GitHub Releases

```bash
gh release create v0.2.0-demo.1 build/release/SlopArena-0.2.0-demo.1.zip \
  --title "SlopArena 0.2.0-demo.1" \
  --notes "$(sed 's/<version>/0.2.0-demo.1/' docs/release/RELEASE_NOTES.template.md)"
```

Send the release URL to friends. They download → unzip → run → Training or Join.

## Off-site image packaging (not a home deploy)

`.github/workflows/gameserver-image.yml` runs on explicit dispatch from `main`
(`release_id` required) or a published release. It tests Shared and Server,
builds a Linux amd64 image, then starts the image with only an external test
config: a real match-start request must load `slop_court` and validate the
admitted cooked catalog. Only then does it push
`ghcr.io/binoui/sloparena-gameserver:<game-source-sha>`; no `latest` tag is
published. The image includes the published runtime, all cooked packages
selected by `content-cooked/roster/manifest.json`, and `data/arenas/*.arena`.
The runtime base is .NET 8.0.31; the build SDK is 8.0.425.

The job uploads `gameserver-image.txt` with the image digest, source revision,
runtime/SDK patches, and operator release ID. Pair it with the independently
published Master image/migration record from the Master repository under the
**same operator release ID**; retain both source SHAs and immutable digests in
the operator-controlled release record. Do not deploy tags or combine images
from unrelated schema revisions without checking compatibility. Publishing
does not change home services, player DNS, or the client endpoint.

For a local image-only check with Docker access:

```bash
docker build --platform linux/amd64 -f Dockerfile.gameserver \
  --build-arg SOURCE_REVISION="$(git rev-parse HEAD)" \
  --build-arg RELEASE_ID=local-test -t sloparena-gameserver:local-test .
scripts/smoke-gameserver-image.sh sloparena-gameserver:local-test
```

The VPS GameServer starts with an operator-owned, read-only `server.json` at
an absolute path supplied as the container argument. Its
`deploymentProfile` must be `"vps"`; a missing file/profile is fatal.
Set `hostId` to the Master-provisioned `ApprovedHost:Id`, `publicIp` to
the approved gameplay DNS, `port` to its approved UDP base port,
`maxConcurrentMatches` to the intended slot count, `masterServerUrl` to
the private Master URL, and `arenaDataDir` to `data/arenas`. Supply distinct
32–4096 character bearer-token `registrationKey` and `matchControlKey` values
(independent `openssl rand -base64 48` outputs work) through the private
read-only configuration file; match them to `ApprovedHost:RegistrationKey`
and `MatchControl:Key` on Master. Never put this file in an image, client,
release record or repository. Master owns the advertised public address
and the private `ApprovedHost:ControlUrl` (`/match/start` on the control
network); do not publish GameServer TCP control publicly. The packaged
`server.json` is intentionally absent. Legacy/local setups must select
`"development"` explicitly; the local image smoke does so.

This is service authentication, not a substitute for the restricted
HTTPS/UDP ingress and deployment preflight in the later VPS phase.

## CI

- This repo (`.github/workflows/ci.yml`): on push to main + PR — build
  `src/Shared/`, run `tests/Shared.Tests/` (757 passing, 9 skipped), build `src/Server/`.
- Master repo (`.github/workflows/build.yml`): build + test on push/PR to
  main; on `v*` tag push, publishes `dotnet publish -c Release` output as a
  GitHub Actions artifact.
- Deploy is NOT CI-triggered — home infra is not CI-reliable; deploy stays
  manual/scripted (rsync/ssh per this doc).

## Manual deploy (not in CI)

Master server publish + rsync: see `docs/systems/production-hosting.md`
("Redeploy master"). The master repo publishes independently of the game repo
and is NOT part of `build-release.sh`.
