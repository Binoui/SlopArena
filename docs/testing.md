# Testing & Verification

> How to verify that your changes work — for agents and contributors.

---

## Quick Verification (after ANY code change)

```bash
# Must pass with 0 errors
dotnet build --nologo

# Lint check
make lint
```

If `dotnet build` fails with errors in `Shared/`, check that you didn't import `Godot.` — Shared/ is pure C#.

---

## Sandbox Testing (Godot Editor)

The fastest way to test gameplay changes:

1. Open `project.godot` in Godot 4.6 (.NET version)
2. Press **F5** to run
3. Select a character (Manki or Bunny)
4. You're in a split arena with 1 training dummy

**What to test:**
- Movement: WASD, space (jump/double jump), shift (dash)
- Combat: LMB combo, RMB (hold), Q/E/R/F abilities
- Targeting: Tab cycles target, scroll wheel zooms
- Hit registration: check Godot console for `[HITBOX]` log lines

**Console output to watch:**
- `[Match] Loaded baked data: ...` — hurtbox system active
- `[HITBOX] MeleeCone at (...)` — attack hitbox spawned
- `[Rollback] Tick X: d=(...)` — position desync detected and corrected
- `[Sim] Entity list: N entries` — hurtbox debug data (first 10 ticks)

---

## Local PvP Testing (2 instances)

Test the real server-authoritative multiplayer:

**Terminal 1 — Server:**
```bash
dotnet run --project Server/SlopArena.Server.csproj
```
Output: `[Match:...] Listening on UDP 9876, waiting for 2 players...`

**Terminal 2 & 3 — Clients:**
Open Godot twice (or export a client build). Both clients connect automatically via `NetworkClient`.

**What to verify:**
- Both players appear on each other's screens
- Attacks register damage on the opponent (check server console for hit logs)
- Void death → respawn works
- Match ends after 3 deaths with score display in console

**Server console signals:**
- `Player 1 connected` / `Player 2 connected — countdown started!`
- `GO!` — match started
- `Player 1 eliminated! Player 2 wins! (3-0)` — match ended
- `Player 1 timed out — stopping match.` — disconnect detected

---

## Running the Headless Server

```bash
# Build and run
dotnet run --project Server/SlopArena.Server.csproj

# Default port: 9876, arena: pit, both players: Manki
# Future CLI args: --port 8765 --arena split --class Bunny
```

The server runs at 60Hz with `ServerSimulation` (hit detection + hurtboxes + void death).

---

## Running Tools

```bash
# Bake arena binary data from .tscn scenes
make bake-arenas

# Bake skeleton data (run in Godot headless)
godot --headless --script tools/headless_bake.gd

# Inspect a GLB file
python tools/inspect_glb.py assets/characters/manki/manki.glb

# Validate baked skeleton data
python tools/read_skeleton_bin.py data/manki_skeleton.bin
```

---

## Common Failure Modes

| Symptom | Likely Cause | Check |
|---------|-------------|-------|
| Build fails with `Godot` not found | Used `Godot.` import in Shared/ | Remove Godot reference, use `System.MathF` |
| Character invisible in sandbox | Model not loaded | Check `GlbPath` and `bakedDataPath` in CharacterDefinition |
| Attacks don't connect | Hitbox offset wrong or TriggerTick > DurationTicks | Check `HitboxEvent` values, check console for `[HITBOX]` log |
| Opponent doesn't move in PvP | Server not running or wrong entity ID | Check server console, verify `OpponentEntityId = 2` |
| Rollback spam in console | Prediction threshold too tight or server desync | Check `distSq > 0.25f` threshold in MatchManager |
| Spell VFX invisible | `StatusSpells.AddToScene` commented out | Check `Scripts/Spells/StatusSpells.cs:177` |

---

## Profiling / Debug

```bash
# Count GD.Print() calls (should stay under 15 files)
grep -r "GD.Print" Scripts/ --include="*.cs" -l | wc -l

# Find long methods (>150 lines)
find Scripts -name "*.cs" -exec awk '/public|private|protected/ && /\(/ {s=NR} /^    \}/ {if(NR-s>150) print FILENAME":"s" ("NR-s" lines)"}' {} \;
```
