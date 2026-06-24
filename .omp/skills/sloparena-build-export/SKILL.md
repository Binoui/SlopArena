---
name: sloparena-build-export
description: Build, export, and release SlopArena — Godot export presets, CI/CD pipeline, and common export failure fixes.
---

# SlopArena Build & Export

## When to use

- Setting up or fixing Godot export presets (Windows/Linux)
- Debugging export failures (missing files, parse errors, Roslynator conflicts)
- Configuring CI/CD for automated builds (GitHub Actions + GitHub Releases)
- Embedding non-standard files in exports (.bin, raw data)
- Single-file .exe vs multi-file distribution

## Export Presets

Located in `export_presets.cfg`. Current presets:

| Preset | Platform | Path | Single-file |
|--------|----------|------|-------------|
| 0 | Windows Desktop | `build/windows/SlopArena.exe` | Yes |
| 1 | Linux | `build/linux/SlopArena.x86_64` | Yes |

Key settings for single-file distribution:
- `binary_format/embed_pck=true` — embeds .pck in .exe
- `dotnet/embed_build_outputs=true` — embeds .NET runtime DLLs in .exe
- `export_filter="all_resources"` — include everything
- `include_filter="data/*.bin"` — force-include raw .bin files
- `exclude_filter="tools/*"` — exclude dev tools

## Common Export Failures

### 1. Roslynator blocks Windows export
**Symptom:** `GDEExtention: failed to copy shared object` during Windows export. Linux works fine.
**Cause:** Roslynator analyzers with `IncludeAssets=runtime;native` — Godot tries to copy their native DLLs.
**Fix:** Change to `IncludeAssets=analyzers` only in `SlopArena.csproj`:
```xml
<PackageReference Include="Roslynator.Analyzers" Version="4.15.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>analyzers</IncludeAssets>
</PackageReference>
```

### 2. Raw .bin files not included in export
**Symptom:** Baked skeleton data loads in editor but not in exported build. Hurtboxes fall back to inaccurate capsules.
**Cause:** `.bin` files have no `.import` metadata — Godot skips them even with `export_filter="all_resources"`.
**Fix A (export filter):** Add `include_filter="data/*.bin"` in export presets. Works when .pck is separate.
**Fix B (embedded resource):** Add `<EmbeddedResource Include="data/*.bin" />` to `.csproj`, then load from assembly manifest as fallback. Works with single-file exports. See `references/embedded-resource-loading.md`.

### 3. Broken .tscn in tools/ blocks export
**Symptom:** Parse error on `tools/bake_arenas.tscn:4` during export e.g. `Parse Error. [Resource file res://tools/bake_arenas.tscn:4]`
**Cause:** Two possibilities:
  1. Godot tscn format requires `[ext_resource]` declarations to come BEFORE `[node]` blocks that reference them. If out of order, ExtResource() ID can't be resolved.
  2. File was removed from disk but stale `.godot/` cache references it.
**Fix:**
  - For ordering: reorder the tscn so `[ext_resource]` lines appear above all `[node]` blocks.
  - For stale caches: close/reopen Godot, or clear `.godot/` cache.
  - Add `exclude_filter="tools/*"` to export presets as safety net. Note: Godot still parses excluded files during resource scanning, so a broken tscn can crash the export even with the exclude filter in place. The tscn must be valid regardless.

### 4. External server process fails in exported build
**Symptom:** `StartLocalServer()` tries to launch `ServerApp.dll` from the filesystem. `ProjectSettings.GlobalizePath("res://")` doesn't resolve in exported builds.
**Fix:** Guard with `OS.HasFeature("editor")`:
```csharp
private void StartLocalServer(CharacterClass playerClass)
{
    if (!OS.HasFeature("editor")) return;
    // ... launch process
}
```
The sandbox uses local `ServerSimulation` — no external process needed.

### 5. Godot UI overwrites manual edits to export_presets.cfg
**Symptom:** You edit `export_presets.cfg` by hand, then open Export → Presets in Godot to change one option. All your manual edits vanish — Godot rewrites the entire file on save.
**Root cause:** Godot treats `export_presets.cfg` as its own file. Opening the Export dialog and clicking OK rewrites every section, discarding anything not in the UI.
**Fix:** Either:
  - Make ALL changes through the Godot Export UI (Project → Export → presets tab), or
  - Make manual config edits and NEVER touch Export settings in Godot afterward (restart Godot between config edits and export run).
**Verify:** After any change, `grep embed_build_outputs export_presets.cfg` should show `true` for both presets.

### 6. `data_SlopArena_*` folder persists — MAY be required at runtime  
**Symptom:** After export with `dotnet/embed_build_outputs=true`, a `data_SlopArena_windows_x86_64/` folder full of DLLs appears. Looks like the setting didn't work.  
  
**⚠️ BEHAVIOR DIFFERS BY EXPORT HOST:**  
  
| Export host | Assemblies embedded? | Runtime requires `data_*` folder? |  
|-------------|---------------------|----------------------------------|  
| Native Windows | ✅ Yes (into .pck) | ❌ Can delete — harmless artifact |  
| Linux → Windows (cross-compile) | ❌ **NOT embedded** (Godot 4.6 bug on Linux host) | ✅ **MUST keep** — only copy of 77 MB assemblies |  
| GitHub Actions (ubuntu) | ❌ **NOT embedded** — CI succeeded with official Godot binary but `data_*` folder was still produced and zipped alongside .exe | ✅ **MUST keep** — ship zip as-is, folder required at runtime |  
  
**Verification:** Check whether assemblies are actually inside the .exe's embedded .pck. See `references/pck-forensics.md` for the full procedure, but quick check:  
- Compare file sizes: `du -sh build/windows/data_SlopArena_*/` (~77 MB) vs `stat --format=%s build/windows/SlopArena.exe` (~101 MB). If PCK is only 5-6 MB when assemblies are 77 MB, they're NOT embedded.  
  
**Root cause:** `dotnet publish` always produces the `data_*` folder. On native Windows, Godot also embeds a copy into the .pck. On Linux→Windows cross-compile, the embedding fails silently — the `data_*` folder is the ONLY copy.  
  
**Fix:**  
- **Exporting from Windows:** delete the folder after export — .exe is self-contained.  
- **Exporting from Linux→Windows (local or CI):** keep the folder. Ship `.exe` + `data_SlopArena_windows_x86_64/` together. The `data_*` folder is required at runtime.  
- **Exporting Linux natively:** works perfectly from Linux host — no assembly issues.

### 7. Friend clone fails to build — `Action` type not found in MankiAbilities.cs

**Symptom:** Friend clones the repo, opens in Godot 4.6.3 .NET with .NET 8 SDK, gets compile error on `private readonly Action _onFinished;` — "The type or namespace name 'Action' could not be found."

**Cause:** `Action` is `System.Action` (the standard .NET delegate type). The file only has `using Godot;` and `using SlopArena.Shared;` — missing `using System;`. Some .NET SDK configurations have implicit usings enabled, others don't, depending on how Godot .NET was installed.

**Fix:** Add `using System;` to the file:
```csharp
#nullable enable
using System;
using Godot;
using SlopArena.Shared;
```

**Prevention:** Always add explicit `using System;` in files that use `Action`, `Func`, `Task`, or other `System`-namespace types. Don't rely on implicit usings — they're inconsistent across Godot .NET installations.

## CI/CD Release Pipeline

Workflow: `.github/workflows/release.yml`

**Trigger:** Push to `release` branch, or manual dispatch (`workflow_dispatch`).

Uses `firebelley/godot-export@v8.0.0` — downloads official Godot .NET binary from GitHub releases.

> **⚠️ Critical pitfalls with this action:**
> 1. Must use **full SemVer tag** (`@v8.0.0`, not `@v8` or `@v6`). GitHub Actions looks for the exact tag.
> 2. **No mono headless build exists** — Godot doesn't ship a headless mono binary. Use the full editor URL: `Godot_v4.6.3-stable_mono_linux_x86_64.zip`
> 3. The v8 API is completely different from v6 — see `references/firebelley-godot-export.md` for details.

### Single-job v8 workflow

The v8 action exports ALL presets in one job using `presets_to_export`:

```yaml
- uses: firebelley/godot-export@v8.0.0
  with:
    godot_executable_download_url: https://github.com/godotengine/godot/releases/download/4.6.3-stable/Godot_v4.6.3-stable_mono_linux_x86_64.zip
    godot_export_templates_download_url: https://github.com/godotengine/godot/releases/download/4.6.3-stable/Godot_v4.6.3-stable_mono_export_templates.tpz
    relative_project_path: ./
    use_preset_export_path: true    # respect export_path in presets
    archive_output: true            # auto-zips each preset's output
    cache: true                     # cache Godot binary between runs
    presets_to_export: "Windows Desktop,Linux"
```

Key differences from v6:
- No `godot_version`, `godot_dotnet`, `platform`, `preset_name`, `project_path` inputs
- Uses direct download URLs instead
- `presets_to_export` is a comma-separated list of preset NAMES
- `archive_output: true` creates zips automatically
- Single job handles all presets — no need for matrix builds

### Generate a release

Push to `release` branch:
```bash
git push --force origin main:release
```

Or trigger manually: GitHub → Actions → "Export & Release" → "Run workflow".

### Output

The release zip contains `SlopArena.exe` + `data_SlopArena_windows_x86_64/` folder. Keep the `data_*` folder — it's required at runtime on Windows unless native Windows export verified the .exe is standalone.

## build/ directory

`build/` is gitignored. Export outputs go here:
- `build/windows/SlopArena.exe` — single-file Windows build
- `build/linux/SlopArena.x86_64` — single-file Linux build

Without single-file settings, Godot creates a `data_SlopArena_*` folder with .NET runtime DLLs alongside the .exe.

## Verification checklist

After any change to export presets or .csproj:
```bash
dotnet build --nologo   # must pass with 0 errors
```

Before pushing to release branch:
- `grep embed_build_outputs export_presets.cfg` — both presets should show `true`
- `grep export_filter export_presets.cfg` — should be `"all_resources"`, not `"scenes"`
- `dotnet build --nologo` — 0 errors
- Check any file that uses `Action`/`Func`/`Task` has `using System;` at top
- Verify `.csproj` Roslynator packages have `IncludeAssets=analyzers` (not `runtime; build; native; ...`)

Then export in Godot or push to CI:
- If exporting from Linux→Windows (local or CI): keep the `data_*` folder — required at runtime
- Characters load with correct hurtboxes (baked skeleton data present)
- Tools don't appear in the build
- F5 sandbox mode works identically to editor
