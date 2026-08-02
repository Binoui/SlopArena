---
name: sloparena-build-export
description: Build, export, and release SlopArena — Shared build, server publish, Unity player build, and CI/CD status.
---

# SlopArena Build & Export (Unity)

## When to use

- Building or testing the Shared library or the .NET server
- Publishing the game server for release
- Building the Unity player (Linux/Windows)
- Checking what CI does (or doesn't) automate

## Build & Test Core

```bash
# Shared library (canonical source of truth) — post-build copies the DLL
# into client/Unity/Assets/Plugins/SlopArena.Shared/ automatically.
dotnet build src/Shared/ --nologo

# Test suite (33 suites, ~390 tests)
dotnet test tests/Shared.Tests/ --nologo
```

Always build `src/Shared/` after any Shared change so the Unity plugin DLL stays in sync.

## Server

```bash
# Dev run
dotnet run --project src/Server/

# Release publish
dotnet publish src/Server/SlopArena.Server.csproj -c Release -o build/server
```

Output: `build/server/SlopArena.Server.dll` plus the shared DLLs it depends on. Project layout: `src/Server/{Program.cs, MatchInstance.cs, MatchControlServer.cs, MultiMatchOrchestrator.cs, GameServerRegistration.cs, ServerSkeleton.cs, SlopArena.Server.csproj}`.

## Unity Player Build

No in-repo Editor build script exists (no `BuildPipeline`/`executeMethod` under `Assets/Scripts`, `scripts/`, or `.github/`) — the standalone player args need none:

```bash
"$UNITY_EDITOR" -batchmode -quit -projectPath client/Unity \
  -buildLinux64Player build/linux/SlopArena.x86_64
# or
"$UNITY_EDITOR" -batchmode -quit -projectPath client/Unity \
  -buildWindows64Player build/windows/SlopArena.exe
```

`$UNITY_EDITOR` = the Unity 6000.0.78f1 install path. On a headless/license-gated machine the first build may need manual activation — the command itself is standard.

## CI Status (as of 2026-08)

Only `.github/workflows/{nuget-publish, discord-push}.yml` exist. **There is no Unity build workflow.** If one is added, `game-ci/unity-builder` is the standard route [external knowledge — not yet verified in this repo].

## Gotchas

- **Unity `.meta` files must be committed** — Unity regenerates GUIDs otherwise, breaking prefab/script references.
- **`Library/` is gitignored** — never commit it; it's a local cache.
- **Shared DLL drift** — if the Unity client shows stale behavior, rebuild `src/Shared/` (the plugin copy is post-build).
