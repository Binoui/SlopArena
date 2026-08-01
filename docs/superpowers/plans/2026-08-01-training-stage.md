# Training Box Stage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a 40×40 m training box (checkerboard floor/walls + yellow 5 m / red 10 m grid lines) in the Arena_Offline scene, baked into `data/arenas/training.arena` for server collision.

**Architecture:** Visual geometry = Cube primitives in the Unity scene; collision = the same geometry baked via `SlopArenaArenaBaker` into the `.arena` binary the server sim reads (`loaded ?? ArenaRegistry.Get` precedence → no shared-code changes). Reuses the existing `training` registry key.

**Tech Stack:** Unity 6 Editor (URP Lit shader), C# editor scripts via gamedev-mcp-server (`scripts/mcp-exec.sh`), `SlopArenaArenaBaker` (reflection-driven), `ArenaBinaryFormat` (Shared DLL, already imported in Unity Plugins).

## Global Constraints

- All positions in Unity units = meters, Y-up. Floor top surface must be exactly Y=0 (matches sim ground).
- Arena key: `training` (existing registry entry — file wins at load). Baked output MUST land in `/home/binoui/Documents/projects/SlopArena/data/arenas/training.arena`.
- The 16 grid-line strips are VISUAL ONLY — they must NOT be in the baked collision (baker reads all MeshFilters under root; strips get temporarily reparented out before baking, restored after).
- Strips must be named with prefix `Line_` so the bake script can identify them.
- Spawn markers: tagged `SpawnPoint` (tag exists in TagManager). Player spawn (8, 0.5, 0); alt (-8, 0.5, 0). NPC dummy spawn is hardcoded in TrainingMatch at (0, 5, 0) — do NOT move it.
- Never use `unity-mcp/Unity_*` tools. All Unity interaction via gamedev-mcp-server scripts (`scripts/mcp-check.sh` first — must print `OK`).
- No git commits without explicit user permission (repo rule).
- No shared-code changes, no DLL rebuild.

---

### Task 1: Generate checker texture + 4 materials (me)

**Files:**
- Create: `client/Unity/Assets/Art/Stages/training_box/checker_8x8.png` (generated)
- Create: `client/Unity/Assets/Art/Stages/training_box/Mat_Checker_Floor.mat`
- Create: `client/Unity/Assets/Art/Stages/training_box/Mat_Checker_Wall.mat`
- Create: `client/Unity/Assets/Art/Stages/training_box/Mat_Line_5m.mat`
- Create: `client/Unity/Assets/Art/Stages/training_box/Mat_Line_10m.mat`

**Interfaces:**
- Produces: materials at exact paths above — Task 2 (user) drags them onto cubes by name. `Mat_Checker_Floor` = 8×8 checker at tiling (5,5) → 40×40 squares of 1 m on the 40 m floor. `Mat_Checker_Wall` = same texture at tiling (5,2.5) → 40×20 squares of 1 m on a 40×20 m wall face (walls are 20 m tall). `Mat_Line_5m` flat yellow `#FFD500`, `Mat_Line_10m` flat red `#FF3B30`. Texture: 8×8 squares, grey `#909090`/white `#F2F2F2`, Point filter, Repeat wrap (Clamp breaks tiling > 1).

- [ ] **Step 1: Pre-flight check**

Run: `scripts/mcp-check.sh`
Expected: `gamedev-mcp-server: OK (Unity is running)`. If DOWN, stop — nothing else works.

- [ ] **Step 2: Generate texture + materials via script-execute**

Run:
```bash
scripts/mcp-exec.sh --json 'using UnityEngine; using UnityEditor; using System.IO;
public class Script { public static string Main() {
  string dir = "Assets/Art/Stages/training_box";
  Directory.CreateDirectory(dir);
  var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
  var grey = new Color32(0x90, 0x90, 0x90, 0xFF);
  var white = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
  var px = new Color32[256*256];
  for (int y = 0; y < 256; y++)
    for (int x = 0; x < 256; x++) {
      int sy = y / 32, sx = x / 32;
      px[y*256 + x] = ((sx + sy) % 2 == 0) ? grey : white;
    }
  tex.SetPixels32(px);
  tex.Apply();
  string pngPath = dir + "/checker_8x8.png";
  File.WriteAllBytes(pngPath, tex.EncodeToPNG());
  AssetDatabase.ImportAsset(pngPath);
  var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
  imp.textureType = TextureImporterType.Default;
  imp.filterMode = FilterMode.Point;
  imp.mipmapEnabled = false;
  imp.wrapMode = TextureWrapMode.Repeat;   // MUST be Repeat: Clamp breaks tiling > 1 (surface renders solid)
  AssetDatabase.ImportAsset(pngPath);
  var tex2 = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
  var lit = Shader.Find("Universal Render Pipeline/Lit");
  var floor = new Material(lit); floor.name = "Mat_Checker_Floor";
  floor.SetTexture("_BaseMap", tex2); floor.SetTextureScale("_BaseMap", new Vector2(5,5));   // 1 m squares (40x40)
  var wall = new Material(lit); wall.name = "Mat_Checker_Wall";
  wall.SetTexture("_BaseMap", tex2); wall.SetTextureScale("_BaseMap", new Vector2(5,2.5f)); // 1 m squares (40x20)
  var y5 = new Material(lit); y5.name = "Mat_Line_5m";
  y5.SetColor("_BaseColor", new Color32(0xFF, 0xD5, 0x00, 0xFF));
  var r10 = new Material(lit); r10.name = "Mat_Line_10m";
  r10.SetColor("_BaseColor", new Color32(0xFF, 0x3B, 0x30, 0xFF));
  AssetDatabase.CreateAsset(floor, dir + "/Mat_Checker_Floor.mat");
  AssetDatabase.CreateAsset(wall, dir + "/Mat_Checker_Wall.mat");
  AssetDatabase.CreateAsset(y5, dir + "/Mat_Line_5m.mat");
  AssetDatabase.CreateAsset(r10, dir + "/Mat_Line_10m.mat");
  AssetDatabase.SaveAssets();
  return "created: " + Directory.GetFiles(dir, "*.mat").Length + " mats + png in " + dir;
}}'
```
Expected output value: `created: 4 mats + png in Assets/Art/Stages/training_box`

- [ ] **Step 3: Verify assets exist**

Run: `scripts/mcp-call.sh assets-find '{"filter":"t:Material Mat_Line"}'` (adjust filter if the tool requires a different schema — check `scripts/mcp-call.sh assets-find '{}'` error first) and list the folder via bash `ls client/Unity/Assets/Art/Stages/training_box/`.
Expected: 4 `.mat` files + `checker_8x8.png` present.

---

### Task 2: User builds the box in the scene (user, coached by me)

**Files:**
- Modify: `client/Unity/Assets/Scenes/Arena_Offline.unity` (scene objects only, no file edit)

**Interfaces:**
- Produces: scene hierarchy `TrainingBox` root containing 21 cubes (names below) + 2 `SpawnPoint`-tagged markers. Task 3 bakes this root; Task 4 wires the scene.

- [ ] **Step 1: Create the TrainingBox root**
  In the Unity Hierarchy: right-click empty area → `Create Empty`, name it `TrainingBox`, set Transform position to (0, 0, 0), scale (1, 1, 1). All following objects are created by right-clicking `TrainingBox` → `3D Object` → `Cube`, then setting Transform and material in the Inspector.

- [ ] **Step 2: Floor + 4 walls (5 cubes)**
  For each row: create cube under TrainingBox, rename, set Position/Scale, drag material from Project view (`Assets/Art/Stages/training_box/`):

  | Name | Position (x, y, z) | Scale (x, y, z) | Material |
  |---|---|---|---|
  | Floor | (0, -0.25, 0) | (40, 0.5, 40) | Mat_Checker_Floor |
  | Wall_N | (0, 10, 20.25) | (40, 20, 0.5) | Mat_Checker_Wall |
  | Wall_S | (0, 10, -20.25) | (40, 20, 0.5) | Mat_Checker_Wall |
  | Wall_E | (20.25, 10, 0) | (0.5, 20, 40) | Mat_Checker_Wall |
  | Wall_W | (-20.25, 10, 0) | (0.5, 20, 40) | Mat_Checker_Wall |

  Check: floor top surface = Y=0 (position.y −0.25 + half-height 0.25). Walls sit at ±20.25 so their inner face is exactly at ±20. NOTE: walls are 20 m tall (user decision: "real box" — a separate future stage will be wall-less).

- [ ] **Step 3: 16 grid-line strips (visual only)**
  Yellow strips (Mat_Line_5m), scale (0.15, 0.05, 40) or (40, 0.05, 0.15), all at Y = 0.02:

  | Name | Position (x, y, z) |
  |---|---|
  | Line_5m_X+5 | (5, 0.02, 0) |
  | Line_5m_X-5 | (-5, 0.02, 0) |
  | Line_5m_X+15 | (15, 0.02, 0) |
  | Line_5m_X-15 | (-15, 0.02, 0) |
  | Line_5m_Z+5 | (0, 0.02, 5) |
  | Line_5m_Z-5 | (0, 0.02, -5) |
  | Line_5m_Z+15 | (0, 0.02, 15) |
  | Line_5m_Z-15 | (0, 0.02, -15) |

  Red strips (Mat_Line_10m), same shapes/Y:

  | Name | Position (x, y, z) |
  |---|---|
  | Line_10m_X+10 | (10, 0.02, 0) |
  | Line_10m_X-10 | (-10, 0.02, 0) |
  | Line_10m_X+20 | (20, 0.02, 0) |
  | Line_10m_X-20 | (-20, 0.02, 0) |
  | Line_10m_Z+10 | (0, 0.02, 10) |
  | Line_10m_Z-10 | (0, 0.02, -10) |
  | Line_10m_Z+20 | (0, 0.02, 20) |
  | Line_10m_Z-20 | (0, 0.02, -20) |

  X-direction strips use scale (0.15, 0.05, 40); Z-direction strips use (40, 0.05, 0.15).

- [ ] **Step 4: Place spawn markers**
  Create 2 empty GameObjects under TrainingBox named `spawn_player` and `spawn_alt`. Select each, set Tag to `SpawnPoint` in the Inspector (top of Inspector, Tag dropdown), positions:

  | Name | Position (x, y, z) |
  |---|---|
  | spawn_player | (8, 0.5, 0) |
  | spawn_alt | (-8, 0.5, 0) |

  Do NOT add any spawn at (0, 0, 0) — the NPC dummy hardcodes that position.

- [ ] **Step 5: Tell me it's done**
  Say "blocks done" — I run the Task 3 verification (bake) + Task 4 (wiring) immediately. Do not enter Play mode yet.

---

### Task 3: Bake collision (me)

**Files:**
- Create (overwrite): `data/arenas/training.arena`

**Interfaces:**
- Consumes: Task 2 scene hierarchy (TrainingBox + spawns).
- Produces: `training.arena` with exactly 60 collision triangles (5 cubes × 12) and 2 spawn points, bounds X/Z ≈ ±20.

- [ ] **Step 1: Verify user's layout before baking**

Run:
```bash
scripts/mcp-exec.sh --json 'using UnityEngine; using System.Text;
public class Script { public static string Main() {
  var root = GameObject.Find("TrainingBox");
  if (root == null) return "MISSING TrainingBox";
  var sb = new StringBuilder();
  int cubes = 0, lines = 0;
  foreach (Transform c in root.transform) {
    var mf = c.GetComponent<MeshFilter>();
    if (mf == null && c.CompareTag("SpawnPoint")) { sb.AppendLine("SPAWN " + c.name + " pos=" + c.position); continue; }
    cubes++;
    if (c.name.StartsWith("Line_")) lines++;
    sb.AppendLine(c.name + " pos=" + c.position + " scale=" + c.localScale + " mat=" + (c.GetComponent<MeshRenderer>()?.sharedMaterial?.name ?? "NONE"));
  }
  sb.Insert(0, "children=" + root.transform.childCount + " cubes=" + cubes + " lines=" + lines + "\n");
  return sb.ToString();
}}'
```
Expected: `children=23 cubes=21 lines=16`, every cube has the right mat (no `NONE`), 2 SPAWN lines at (8, 0.5, 0) and (-8, 0.5, 0). If any line is off, correct with the user before baking.

- [ ] **Step 2: Bake with strips excluded**

Run:
```bash
scripts/mcp-exec.sh --json 'using UnityEngine; using UnityEditor; using System.Reflection;
public class Script { public static string Main() {
  var root = GameObject.Find("TrainingBox");
  var tmp = new GameObject("BakeExclude");
  var strips = new System.Collections.Generic.List<Transform>();
  foreach (Transform c in root.transform)
    if (c.name.StartsWith("Line_")) strips.Add(c);
  foreach (var s in strips) s.SetParent(tmp.transform, true); // world pos preserved, out of hierarchy
  var w = (SlopArenaArenaBaker)ScriptableObject.CreateInstance(typeof(SlopArenaArenaBaker));
  var t = typeof(SlopArenaArenaBaker);
  var F = BindingFlags.Instance | BindingFlags.NonPublic;
  t.GetField("_arenaRoot", F).SetValue(w, root);
  t.GetField("_arenaName", F).SetValue(w, "training");
  t.GetField("_displayName", F).SetValue(w, "Training Room");
  t.GetField("_killHeight", F).SetValue(w, -15f);
  t.GetField("_autoBounds", F).SetValue(w, true);
  t.GetField("_outputDir", F).SetValue(w, "/home/binoui/Documents/projects/SlopArena/data/arenas");
  t.GetMethod("BakeArena", F).Invoke(w, null);
  foreach (var s in strips) s.SetParent(root.transform, true);
  Object.DestroyImmediate(tmp);
  return "baked, strips restored";
}}'
```
Expected: `baked, strips restored`; Unity console shows `[ArenaBaker] Baked: training.arena`, `Triangles: 60`, `Spawns: 2`, `Output: /home/binoui/Documents/projects/SlopArena/data/arenas/training.arena`.

- [ ] **Step 3: Verify the .arena content**

Run:
```bash
scripts/mcp-exec.sh --json 'using UnityEngine; using System.IO; using SlopArena.Shared;
public class Script { public static string Main() {
  var path = "/home/binoui/Documents/projects/SlopArena/data/arenas/training.arena";
  var a = ArenaBinaryFormat.LoadFromFile(path);
  if (a == null) return "FAILED to load";
  return "tris=" + (a.CollisionTriangles?.Length ?? 0) + " spawns=" + a.SpawnPoints.Length +
         " bounds X[" + a.MinX + "," + a.MaxX + "] Z[" + a.MinZ + "," + a.MaxZ + "] kill=" + a.KillHeight +
         " spawn0=" + a.SpawnPoints[0].X + "," + a.SpawnPoints[0].Y + "," + a.SpawnPoints[0].Z;
}}'
```
Expected: `tris=60 spawns=2 bounds X[-20,20] Z[-20,20] kill=-15 spawn0=8,0.5,0`. If tris > 60, the strips leaked into the bake — stop and fix the exclusion logic.

---

### Task 4: Wire the scene (me)

**Files:**
- Modify: `client/Unity/Assets/Scenes/Arena_Offline.unity`

**Interfaces:**
- Consumes: Task 3 baked file.
- Produces: scene with TrainingBox as the stage, colosseum model removed, `TrainingMatch._arenaNameOverride = "training"`, stale `_arenaName` prefab override purged.

- [ ] **Step 1: Remove old stage, point at training, clean stale override**

  NOTE: `SerializedObject` must wrap the TrainingMatch COMPONENT, not the GameObject
  (`so.FindProperty` on a GameObject returns null for MonoBehaviour fields → NRE).

Run:
```bash
scripts/mcp-exec.sh --json 'using UnityEngine; using UnityEditor; using UnityEditor.SceneManagement; using System.Text; using SlopArena.Client.World;
public class Script { public static string Main() {
  var sb = new StringBuilder();
  var old = GameObject.Find("colosseum");
  if (old != null) { Object.DestroyImmediate(old); sb.AppendLine("colosseum removed"); }
  else sb.AppendLine("colosseum already gone");
  var match = GameObject.Find("TrainingMatch");
  if (match == null) return "TrainingMatch not found";
  var tm = match.GetComponent<TrainingMatch>();
  var mods = PrefabUtility.GetPropertyModifications(match);
  var stale = 0;
  if (mods != null) {
    var keep = new System.Collections.Generic.List<PropertyModification>();
    foreach (var m in mods) {
      if (m.propertyPath == "_arenaName") { stale++; continue; } // stale override from renamed field
      keep.Add(m);
    }
    PrefabUtility.SetPropertyModifications(match, keep.ToArray());
  }
  sb.AppendLine("stale mods purged=" + stale);
  var so = new SerializedObject(tm);
  var p = so.FindProperty("_arenaNameOverride");
  if (p == null) return "FIND FAILED _arenaNameOverride";
  p.stringValue = "training";
  so.ApplyModifiedProperties();
  EditorSceneManager.MarkSceneDirty(match.scene);
  EditorSceneManager.SaveScene(match.scene);
  sb.AppendLine("arenaNameOverride=training, scene saved");
  return sb.ToString();
}}'
```
Expected: `colosseum removed` (or `already gone` if a prior partial run deleted it), `stale mods purged=0`, `arenaNameOverride=training, scene saved`.

- [ ] **Step 2: Verify scene state**

Run:
```bash
scripts/mcp-exec.sh --json 'using UnityEngine; using System.Text;
public class Script { public static string Main() {
  var sb = new StringBuilder();
  foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
    sb.AppendLine(go.name + (go.name == "TrainingBox" ? " children=" + go.transform.childCount : ""));
  var match = GameObject.Find("TrainingMatch");
  var so = new UnityEditor.SerializedObject(match);
  sb.AppendLine("arenaNameOverride=" + so.FindProperty("_arenaNameOverride").stringValue);
  return sb.ToString();
}}'
```
Expected: no `colosseum` root, `TrainingBox children=23`, `arenaNameOverride=training`.

---

### Task 5: Play-mode verification (me + user look at the result)

- [ ] **Step 1: Enter play mode**

Run: `scripts/mcp-exec.sh 'UnityEditor.EditorApplication.isPlaying = true;' true`
Expected: no error. Unity enters play mode.

- [ ] **Step 2: Check startup logs**

Run: `scripts/mcp-call.sh console-get-logs '{"logTypes":"ScriptingLog","maxEntries":10}'`
Expected: contains `[TrainingMatch] Loaded arena from file: .../data/arenas/training.arena` and `[Training] tick=1 pos=(8.0,...` (player spawned at the box's spawn 0). NOTE: console may be noisy with MCP handshake spam — if the tick line is missing, fall back to Step 2b.

- [ ] **Step 2b: Direct sim-state check (fallback — console-independent)**

Run (after `sleep 16` in play mode — enough for NPC gravity float/ramp to land it):
```bash
scripts/mcp-exec.sh --json 'using UnityEngine; using SlopArena.Client.World; using SlopArena.Shared;
public class Script { public static string Main() {
  var tm = GameObject.Find("TrainingMatch").GetComponent<TrainingMatch>();
  var f = typeof(TrainingMatch).GetField("_bridge", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
  var bridge = f.GetValue(tm);
  var gs = bridge.GetType().GetMethod("GetState");
  var p = (CharacterState)gs.Invoke(bridge, new object[]{ (ulong)1 });
  var n = (CharacterState)gs.Invoke(bridge, new object[]{ (ulong)100 });
  bool pIn = System.Math.Abs(p.PX) < 20.5f && System.Math.Abs(p.PZ) < 20.5f;
  bool nIn = System.Math.Abs(n.PX) < 20.5f && System.Math.Abs(n.PZ) < 20.5f;
  return "player=(" + p.PX.ToString("F1") + "," + p.PY.ToString("F1") + "," + p.PZ.ToString("F1") + ") npc=(" + n.PX.ToString("F1") + "," + n.PY.ToString("F1") + "," + n.PZ.ToString("F1") + ") inBounds p=" + pIn + " n=" + nIn + " npcGrounded=" + n.IsGrounded;
}}'
```
Expected: player ≈ (8.0, 0.9, 0.0), NPC grounded on the floor, both `inBounds` True. Note: the scene's TrainingMatch has `_npcAiMode = Idle` (pre-existing override) — the dummy stands still by design.

- [ ] **Step 3: Screenshot the stage**

Run:
```bash
scripts/mcp-exec.sh --json 'using UnityEngine;
public class Script { public static string Main() {
  ScreenCapture.CaptureScreenshot("/tmp/training_stage.png");
  return "captured";
}}'
```
Wait 2 s, then read `/tmp/training_stage.png` with `inspect_image` (question: "Describe the scene: is there a grey/white checkerboard floor, red and yellow grid lines, and 4 grey walls? Any obvious artifacts like z-fighting?").
Expected: checkerboard floor with 5 m squares, yellow + red grid lines visible, 4 walls enclosing.

- [ ] **Step 4: Exit play mode**

Run: `scripts/mcp-exec.sh 'UnityEditor.EditorApplication.isPlaying = false;' true`
Expected: no error, Unity back in edit mode.

- [ ] **Step 5: Report + optional commit**

Show the screenshot result and ask the user whether to commit the new assets (`docs/superpowers/specs/2026-08-01-training-stage-design.md`, `docs/superpowers/plans/2026-08-01-training-stage.md`, `Assets/Art/Stages/training_box/`, modified scene + `.arena`). Never commit without explicit permission.

---

## Self-Review

**Spec coverage:**
- Checkerboard floor + walls 5 m squares → Task 1 (texture/tiling) + Task 2 (blocks) ✓
- Yellow 5 m / red 10 m grid → Task 1 (materials) + Task 2 (16 strips) ✓
- Solid walls in collision → Task 3 (bake includes 4 wall cubes) ✓
- 40×40 m box, kill −15 → Task 2 (scales) + Task 3 (`_killHeight`) ✓
- Spawns (8, 0.5, 0) + alt → Task 2 Step 4, verified Task 3 ✓
- Materials/bake/wiring split → Tasks 1/3/4 (me), Task 2 (user) ✓
- Reuse `training` key, no shared changes → Task 3 (`_arenaName="training"`) ✓
- Verification (bake content, scene state, play logs, screenshot) → Task 5 ✓

**Placeholder scan:** no TBD/TODO; all script-execute blocks complete; expected outputs given for every step.

**Type/name consistency:** `Line_` prefix used consistently in Task 2 names and Task 3 strip-detection; `_arenaNameOverride` (live field) vs stale `_arenaName` (purged) distinguished; `training` key consistent across Tasks 3-5; materials referenced by exact asset names created in Task 1.
