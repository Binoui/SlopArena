# HitSpark VFX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder hit spark prefab with an enhanced burst particle effect using Unity's built-in Particle System.

**Architecture:** Modify the existing `HitSpark_Placeholder.prefab` (one GameObject, one ParticleSystem) via Unity MCP tools. No code changes needed — `CombatFeedback.cs` already handles spawning.

**Tech Stack:** Unity URP, Particle System (Shuriken), MCP particle-system-get / particle-system-modify, Unity Editor

## Global Constraints

- MCP `particle-system-get` and `particle-system-modify` work on **scene GameObjects** (not asset prefabs directly) — must instantiate prefab into scene, modify, then apply override back to prefab
- `CombatFeedback.cs` already spawns the prefab and destroys after `_sparkLifetime` — no changes needed
- URP pipeline — use `Particles/Standard Unlit` material (verify existence first)
- All changes happen via MCP `script-execute` (C# Roslyn) + particle tools from gamedev-mcp-server
- No new scripts, no code reviews needed — pure asset work

---

### Task 1: Open and inspect the current placeholder

**Goal:** See what we're starting from — inspect the existing prefab's ParticleSystem state.

**Files:**
- Read-only: `Assets/Prefabs/VFX/HitSpark_Placeholder.prefab`

**Interfaces:**
- Consumes: MCP session ID from `scripts/mcp-init.sh`, MCP particle tools
- Produces: Known current-state values for each module

- [ ] **Step 1: Initialize MCP session**

```bash
cd <project-root>
source scripts/mcp-init.sh
echo "SID=$SID"
```
Expected: `SID=<token>` printed

- [ ] **Step 2: Instantiate the placeholder prefab into the active scene**

Use MCP `script-execute` to load the prefab and instantiate it:
```csharp
using UnityEditor;
using UnityEngine;
public class S { public static string Main() {
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/HitSpark_Placeholder.prefab");
    if (prefab == null) return "ERROR: prefab not found";
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.name = "HitSpark_Debug";
    Selection.activeGameObject = go;
    SceneView.FrameLastActiveSceneView();
    return "OK: instantiated at " + go.transform.position;
}}
```
Run via: `scripts/mcp-exec.sh '<code>'`

Expected: Returns "OK: instantiated at ..." and the prefab is selected in the scene.

- [ ] **Step 3: Inspect the current ParticleSystem with particle-system-get**

Use MCP to get the current state. We need the instanceID of the GameObject:
```csharp
using UnityEditor;
using UnityEngine;
public class S { public static string Main() {
    var go = GameObject.Find("HitSpark_Debug");
    if (go == null) return "ERROR: not found";
    return go.GetInstanceID().ToString();
}}
```
Then use the instanceID with `particle-system-get`:
```bash
scripts/mcp-call.sh particle-system-get \
  '{"gameObjectRef":{"instanceID":<ID>},"includeMain":true,"includeEmission":true,"includeShape":true,"includeColorOverLifetime":true,"includeSizeOverLifetime":true,"includeVelocityOverLifetime":true}'
```

Expected: Returns the current particle module data — 10 particles, 0.3s lifetime, speed 2, angle 25, etc.

- [ ] **Step 4: Check the Renderer module (material, billboard mode)**

```bash
scripts/mcp-call.sh particle-system-get \
  '{"gameObjectRef":{"instanceID":<ID>},"includeRenderer":true}'
```

Expected: Shows Renderer module data — renderMode should be Billboard, material should be the URP default particle material. If material is missing, we'll fix it in a later step.

---

### Task 2: Configure Main module (lifetime, speed, size, max particles)

**Goal:** Set the fundamental particle behavior — how long they live, how fast they move, how big they are.

**Files:**
- Modify: `HitSpark_Debug` GameObject's ParticleSystem component (asset prefab will be updated later)

**Interfaces:**
- Consumes: GameObject instanceID from Task 1
- Produces: Updated ParticleSystem with new Main module values

- [ ] **Step 1: Modify Main module settings**

Use `particle-system-modify` with the Main module parameters:
```bash
scripts/mcp-call.sh particle-system-modify \
  '{"gameObjectRef":{"instanceID":<ID>},"main":{"startLifetime":{"minMaxState":0,"scalar":0.4},"startSpeed":{"minMaxState":0,"scalar":3},"startSize":{"minMaxState":0,"scalar":0.3},"maxParticles":50,"duration":0.5,"looping":false}}'
```

Expected: Returns success. ParticleSystem now emits 0.3m particles, 3 speed, 0.4s life.

- [ ] **Step 2: Verify the changes**

```bash
scripts/mcp-call.sh particle-system-get \
  '{"gameObjectRef":{"instanceID":<ID>},"includeMain":true}'
```

Expected: Main module shows new values — scalar values updated.

---

### Task 3: Configure Emission module (burst, not continuous)

**Goal:** Replace continuous emission with a single burst of 35 particles.

**Files:**
- Modify: HitSpark_Debug ParticleSystem Emission module

- [ ] **Step 1: Set up burst emission**

```bash
scripts/mcp-call.sh particle-system-modify \
  '{"gameObjectRef":{"instanceID":<ID>},"emission":{"rateOverTime":{"minMaxState":0,"scalar":0},"bursts":[{"time":0,"count":{"minMaxState":0,"scalar":35},"cycleCount":1,"repeatInterval":0.01}]}}'
```

Expected: Particle system now emits 35 particles in one burst at time 0.

---

### Task 4: Configure Shape module (wide cone fanned burst)

**Goal:** Make particles spray outward in a wide fan (hemisphere-like).

**Files:**
- Modify: HitSpark_Debug ParticleSystem Shape module

- [ ] **Step 1: Set shape to cone with wide angle**

```bash
scripts/mcp-call.sh particle-system-modify \
  '{"gameObjectRef":{"instanceID":<ID>},"shape":{"shapeType":0,"angle":90,"radius":0.1}}'
```
(`shapeType: 0` = Cone in the ParticleSystemShape enum)

Expected: Particles now emit in a wide 90° cone spray.

---

### Task 5: Configure Size over Lifetime (pop then shrink)

**Goal:** Particles hold their size for a moment then shrink to nothing.

**Files:**
- Modify: HitSpark_Debug ParticleSystem Size over Lifetime module

- [ ] **Step 1: Enable module and set curve**

The size curve: hold at 1x for first 10% of lifetime, then smooth curve down to 0.05x.

```bash
scripts/mcp-call.sh particle-system-modify \
  '{"gameObjectRef":{"instanceID":<ID>},"sizeOverLifetime":{"enabled":true,"size":{"minMaxState":1}}}'
```

(The `minMaxState: 1` enables curve mode. We may need to use a script-execute for more precise curve editing since the particle-system-modify args might not support custom curves directly.)

Let's verify with `particle-system-get` first what the size module looks like:
```bash
scripts/mcp-call.sh particle-system-get \
  '{"gameObjectRef":{"instanceID":<ID>},"includeSizeOverLifetime":true}'
```

If the curve API is limited via MCP, we'll set the curve via script-execute:
```csharp
using UnityEngine;
public class S { public static string Main() {
    var go = GameObject.Find("HitSpark_Debug");
    var ps = go.GetComponent<ParticleSystem>();
    var module = ps.sizeOverLifetime;
    module.enabled = true;
    
    var curve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.1f, 0.9f),
        new Keyframe(0.5f, 0.2f),
        new Keyframe(1f, 0.05f)
    );
    module.size = new ParticleSystem.MinMaxCurve(1f, curve);
    return "OK: size curve set";
}}
```

---

### Task 6: Configure Color over Lifetime (gold→orange→fade)

**Goal:** Particles flash gold, transition to orange, then fade out.

**Files:**
- Modify: HitSpark_Debug ParticleSystem Color over Lifetime module

- [ ] **Step 1: Set color gradient via script**

```csharp
using UnityEngine;
public class S { public static string Main() {
    var go = GameObject.Find("HitSpark_Debug");
    var ps = go.GetComponent<ParticleSystem>();
    var module = ps.colorOverLifetime;
    module.enabled = true;
    
    var gradient = new Gradient();
    gradient.SetKeys(
        new GradientColorKey[] {
            new GradientColorKey(new Color(1f, 0.84f, 0f), 0f),      // #FFD700 gold at 0%
            new GradientColorKey(new Color(1f, 0.27f, 0f), 0.4f),    // #FF4500 orange at 40%
            new GradientColorKey(new Color(0.5f, 0f, 0f), 0.8f)      // dark red at 80%
        },
        new GradientAlphaKey[] {
            new GradientAlphaKey(1f, 0f),    // full opaque at 0%
            new GradientAlphaKey(0.8f, 0.3f),  // slight fade
            new GradientAlphaKey(0f, 1f)    // transparent at 100%
        }
    );
    module.color = new ParticleSystem.MinMaxGradient(gradient);
    return "OK: color gradient set";
}}
```

---

### Task 7: Verify in editor, apply prefab override

**Goal:** Save the modified GameObject's ParticleSystem back to the prefab asset and clean up.

- [ ] **Step 1: Apply changes to prefab**

```csharp
using UnityEditor;
using UnityEngine;
public class S { public static string Main() {
    var go = GameObject.Find("HitSpark_Debug");
    var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
    PrefabUtility.SavePrefabAsset(prefab);
    Object.DestroyImmediate(go);
    AssetDatabase.RenameAsset(
        AssetDatabase.GetAssetPath(prefab),
        "HitSpark.prefab"
    );
    AssetDatabase.Refresh();
    return "OK: HitSpark.prefab saved and renamed";
}}
```

- [ ] **Step 2: Verify asset exists**

```bash
scripts/mcp-call.sh assets-find '{"filter":"t:Prefab HitSpark"}'
```

Expected: Returns path to `Assets/Prefabs/VFX/HitSpark.prefab`

---

### Task 8: Wire into scenes

**Goal:** Update scene references to use the new prefab name.

**Files:**
- Modify: Scenes that reference `HitSpark_Placeholder` prefab

- [ ] **Step 1: Find all scene references to the old prefab**

```bash
grep -r "HitSpark_Placeholder" client/Unity/Assets/Scenes/ --include="*.unity" --include="*.prefab"
```

Expected: Lists scene files with references.

- [ ] **Step 2: Update scene references**

For each scene, find the CombatFeedback component and update `_hitSparkPrefab` reference. Use script-execute to assign the new prefab.

```csharp
using UnityEditor;
using UnityEngine;
public class S { public static string Main() {
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/HitSpark.prefab");
    var feedback = GameObject.FindObjectOfType<SlopArena.Client.Combat.CombatFeedback>();
    if (feedback == null) return "ERROR: CombatFeedback not found";
    
    var field = typeof(SlopArena.Client.Combat.CombatFeedback)
        .GetField("_hitSparkPrefab",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    field.SetValue(feedback, prefab);
    
    // Mark scene dirty
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
        UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    return "OK: assigned HitSpark.prefab to CombatFeedback";
}}
```

---

### Task 9: Test in TrainingMatch

**Goal:** Confirm the particle effect works correctly in-game.

- [ ] **Step 1: Enter play mode**

```bash
scripts/mcp-call.sh editor-application-set-state '{"isPlaying":true}'
```
Expected: Editor enters play mode with TrainingMatch scene loaded.

- [ ] **Step 2: Hit the NPC and observe**

- Move player toward NPC
- Attack (press left click or configured attack button)
- Observe: 35 gold-orange particles burst at the hit point, fan out, shrink, and fade

- [ ] **Step 3: Check console for errors**

```bash
scripts/mcp-call.sh console-get-logs '{"maxEntries":10,"logTypes":"Error"}'
```
Expected: No errors related to CombatFeedback, ParticleSystem, or prefab spawning.

- [ ] **Step 4: Exit play mode**

```bash
scripts/mcp-call.sh editor-application-set-state '{"isPlaying":false}'
```

- [ ] **Step 5: Save scene**

```bash
scripts/mcp-call.sh scene-save '{}'
```

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/specs/2026-07-19-hit-spark-vfx-design.md
git add docs/superpowers/plans/2026-07-19-hit-spark-vfx.md
git add client/Unity/Assets/Prefabs/VFX/HitSpark.prefab
git add client/Unity/Assets/Scenes/
git commit -m "feat: enhance hit spark VFX with Shuriken burst"
```
