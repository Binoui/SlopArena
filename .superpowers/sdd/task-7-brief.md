# Task 7: Apply Prefab Override and Rename

**Goal:** Save the modified `HitSpark_Debug` GameObject's ParticleSystem changes back to the prefab asset, then rename it to `HitSpark.prefab`.

**Context:** GameObject `HitSpark_Debug` (InstanceID: -3946) in the active Unity scene has all its ParticleSystem modules configured (Main, Emission, Shape, SizeOL, ColorOL, Renderer with HitSpark material). These changes exist only on the scene instance — we need to apply them back to the prefab asset.

**Pre-requisites:**
- Original prefab at `Assets/Prefabs/VFX/HitSpark_Placeholder.prefab`
- HitSpark material at `Assets/Art/Materials/HitSpark.mat`
- Scene has HitSpark_Debug with all module changes applied

**Steps:**

1. **Apply changes to prefab:**
   ```csharp
   using UnityEditor;
   using UnityEngine;
   public class Script { public static string Main() {
       var go = GameObject.Find("HitSpark_Debug");
       if (go == null) return "ERROR: HitSpark_Debug not found";
       var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
       if (prefab == null) return "ERROR: not a prefab instance";
       
       // Apply overrides — all property modifications
       PrefabUtility.SavePrefabAsset(prefab);
       
       // Rename
       var path = AssetDatabase.GetAssetPath(prefab);
       AssetDatabase.RenameAsset(path, "HitSpark.prefab");
       
       // Clean up scene instance
       Object.DestroyImmediate(go);
       
       // Refresh
       AssetDatabase.Refresh();
       return "OK: saved as " + AssetDatabase.GetAssetPath(
           AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/HitSpark.prefab"));
   }}
   ```

2. **Verify the asset exists:**
   ```
   scripts/mcp-call.sh assets-find '{"filter":"t:Prefab HitSpark"}'
   ```
   Expected: Shows path to `Assets/Prefabs/VFX/HitSpark.prefab`

3. **Verify the material asset also exists:**
   ```
   scripts/mcp-call.sh assets-find '{"filter":"t:Material HitSpark"}'
   ```
   Expected: Shows path to `Assets/Art/Materials/HitSpark.mat`

**Report file:** `.superpowers/sdd/task-7-report.md`

Write the report with:
- Whether prefab save succeeded
- Rename result (new path)
- Material asset path
- Any errors
- The paths for downstream task (Task 8: scene wiring)

**Return status:** DONE / NEEDS_CONTEXT / BLOCKED and one-line summary.
