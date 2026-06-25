# Repo Cleanup — After Unity Port

When the Unity client reaches feature parity and Godot is no longer used,
execute this cleanup plan in order.

## Phase 1 — Delete Godot client files

```bash
git rm -r Scripts/
git rm -r scenes/
git rm -r tools/
git rm -r addons/
git rm main.tscn
git rm project.godot
git rm export_presets.cfg
git rm SlopArena.csproj
git rm SlopArena.csproj.old
git rm SlopArena.sln
git rm icon.svg icon.svg.import
git rm heightmap.bin
git rm global.json
git rm .godot/ -r
git rm .editorconfig
git rm Makefile
git rm -r .githooks/
```

Also delete Godot-specific CI:
```bash
git rm .github/workflows/build.yml
git rm .github/workflows/release.yml
```

## Phase 2 — Keep server solution

Create `sloparena-server.sln` referencing only:
- `Shared/SlopArena.Shared.csproj`
- `Server/SlopArena.Server.csproj`
- `ServerApp/ServerApp.csproj` (optional, prototype)

## Phase 3 — Update `.gitignore`

Replace Godot entries with the official [Unity `.gitignore` template](https://github.com/github/gitignore/blob/main/Unity.gitignore).
Prefix entries with `[Uu]nit/` so they only apply to the Unity subdirectory:

```gitignore
# ========================
# Unity (client/)
# ========================
[Uu]nit/[Ll]ibrary/
[Uu]nit/[Tt]emp/
[Uu]nit/[Oo]bj/
[Uu]nit/[Bb]uild/
[Uu]nit/[Bb]uilds/
[Uu]nit/[Ll]ogs/
[Uu]nit/[Uu]serSettings/
[Uu]nit/[Mm]emoryCapturer/
[Uu]nit/Assets/Shared/bin/

# Unity generated C# project files — keep or ignore?
# If you reference Shared/ via source, ignore these:
[Uu]nit/*.csproj
[Uu]nit/*.sln

# Visual Studio / Rider
[Uu]nit/.vs/
[Uu]nit/.idea/
[Uu]nit/*.user
[Uu]nit/*.userprefs

# ========================
# .NET (server)
# ========================
bin/
obj/
*.user
*.suo

# ========================
# OS
# ========================
.DS_Store
Thumbs.db

# ========================
# Project data (keep)
# ========================
data/*.bin
```

**Why `[Uu]nit/` prefix:** The repo root still has the server (Shared/, Server/) at the
top level. Only the Unity subdirectory generates these files. Without the prefix,
someone opening `Shared/` in VS Code would hit false positives.

These ignored files are large, machine-specific, and auto-generated:
- `Library/` — up to 10GB of imported asset cache
- `Temp/` — temporary build artifacts
- `UserSettings/` — editor layout per developer
## Phase 4 — Update docs

- `CLAUDE.md`: Replace Godot-specific rules (no Godot. in Shared/ stays)
  - Remove: Godot UI conventions, FSM quirks, AnimationTree builder
  - Add: Unity project structure, Animator conventions
- `README.md`: Replace Godot build steps with Unity + server build
- `docs/architecture-overview.md`: Update directory map (remove Scripts/, scenes/, add Unity/)

## Phase 5 — Update CI

- Keep `nuget-publish.yml` (Shared/ NuGet publish, unchanged)
- Keep `discord-push.yml` (notifications, unchanged)
- Replace `release.yml` (Godot export → Unity build via game-ci/unity-builder)
- Add Unity license activation step

## Phase 6 — Repo restructuring (optional)

Consider moving Shared/, Server/ into `src/` for cleaner root:

```
SlopArena/
├── src/
│   ├── Shared/
│   └── Server/
├── client/
│   └── Unity/
├── assets/
├── data/
└── docs/
```

Optional — flat layout is fine for a solo project. Restructure only if you
plan to add more client platforms.
