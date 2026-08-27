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
stages arenas plus the cooked roster manifest and FightGuy package payloads,
stamps `bundleVersion`, runs the Unity Windows player build,
then restores `ProjectSettings.asset` and unstages `StreamingAssets/`.

Both client and server staging trees contain:

- `content-cooked/roster/manifest.json`
- `content-cooked/fightguy/manifest.json`
- `content-cooked/fightguy/character.runtime.json`
- `content-cooked/fightguy/poses.bin`
- `content-cooked/fightguy/client.bindings`

Raw FightGuy authoring JSON, manual FightGuy animation configs, and
`fightguy_skeleton.bin` are not release inputs.

> The version stamp is reverted via `git checkout` of ProjectSettings.asset —
> the script refuses to run if that file has uncommitted changes.

### 3. Refresh the official game server (mini PC)

Optional if only the client changed. See `docs/systems/production-hosting.md`
("Redeploy game server"). Reminder: restart `server-1` after any master
redeploy — it registers once at startup and never retries.

### 4. Publish to GitHub Releases

```bash
gh release create v0.2.0-demo.1 build/release/SlopArena-0.2.0-demo.1.zip \
  --title "SlopArena 0.2.0-demo.1" \
  --notes "$(sed 's/<version>/0.2.0-demo.1/' docs/release/RELEASE_NOTES.template.md)"
```

Send the release URL to friends. They download → unzip → run → Training or Join.

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
