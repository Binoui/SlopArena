# Task 1: Open and inspect the current placeholder

**Goal:** See what we're starting from — inspect the existing `HitSpark_Placeholder` prefab's ParticleSystem state.

**Context:** This is the first task of implementing a hit spark VFX for SlopArena. We have an existing placeholder prefab at `Assets/Prefabs/VFX/HitSpark_Placeholder.prefab` that contains a bare Unity ParticleSystem (Shuriken). We need to inspect its current configuration.

**Files:**
- Read-only: `Assets/Prefabs/VFX/HitSpark_Placeholder.prefab`

**Steps:**

1. **Initialize MCP session** — run:
   ```
   cd <project-root>
   source scripts/mcp-init.sh
   echo "SID=$SID"
   ```
   Expected: `SID=<token>` printed

2. **Instantiate placeholder into scene** — use MCP `script-execute` to load the prefab and instantiate it:
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
   Expected: Returns "OK: instantiated at ..."

3. **Get GameObject instanceID**:
   ```csharp
   using UnityEditor;
   using UnityEngine;
   public class S { public static string Main() {
       var go = GameObject.Find("HitSpark_Debug");
       if (go == null) return "ERROR: not found";
       return go.GetInstanceID().ToString();
   }}
   ```

4. **Inspect all modules with particle-system-get**:
   ```
   scripts/mcp-call.sh particle-system-get \
     '{"gameObjectRef":{"instanceID":<ID>},"includeMain":true,"includeEmission":true,"includeShape":true,"includeColorOverLifetime":true,"includeSizeOverLifetime":true,"includeVelocityOverLifetime":true,"includeRenderer":true}'
   `
   Expected: Returns current particle module data

5. **Report the instanceID and all current module values** in the report file.

**Report file:** `.superpowers/sdd/task-1-report.md`

Write the report with:
- MCP session ID (SID)
- GameObject instanceID
- Current ParticleSystem property values (lifetime, speed, size, maxParticles, color, shape, emission burst count, renderer settings)
- Any issues found (missing material, unexpected values)

**Return status:** `DONE` with commits (none expected — read-only) and a one-line summary.
