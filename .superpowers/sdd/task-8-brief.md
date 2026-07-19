# Task 8: Wire Into Scenes

**Goal:** Update scene references to use the new `HitSpark.prefab` instead of the old `HitSpark_Placeholder.prefab`.

**Context:** The new prefab is at `Assets/Prefabs/VFX/HitSpark.prefab`. The `CombatFeedback` component in each scene has a `_hitSparkPrefab` serialized field (private with SerializeField) that currently references the old placeholder.

**Pre-requisites:**
- HitSpark.prefab at `Assets/Prefabs/VFX/HitSpark.prefab`
- HitSpark.mat at `Assets/Art/Materials/HitSpark.mat`

**Steps:**

1. **Find all scenes referencing the old prefab:**
   Use `script-execute` to find all CombatFeedback instances:
   ```csharp
   using UnityEditor;
   using UnityEngine;
   public class Script { public static string Main() {
       var sb = new System.Text.StringBuilder();
       var scenes = EditorBuildSettings.scenes;
       foreach (var s in scenes) {
           if (!s.enabled) continue;
           sb.AppendLine(s.path);
       }
       // Also check current scene
       sb.AppendLine("Active: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
       return sb.ToString();
   }}
   ```

2. **Update CombatFeedback reference in the active scene:**

   The active scene should be the TrainingMatch scene (Arena.unity or similar). Find CombatFeedback and assign the new prefab:
   ```csharp
   using UnityEditor;
   using UnityEngine;
   using SlopArena.Client.Combat;
   public class Script { public static string Main() {
       var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/HitSpark.prefab");
       if (prefab == null) return "ERROR: HitSpark.prefab not found";
       
       var feedback = GameObject.FindObjectOfType<CombatFeedback>(true);
       if (feedback == null) return "ERROR: CombatFeedback not found in scene";
       
       var field = typeof(CombatFeedback).GetField("_hitSparkPrefab",
           System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
       if (field == null) return "ERROR: _hitSparkPrefab field not found";
       
       field.SetValue(feedback, prefab);
       
       UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
           UnityEngine.SceneManagement.SceneManager.GetActiveScene());
       
       return "OK: assigned HitSpark.prefab to CombatFeedback in " + 
           UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
   }}
   ```

3. **Save the scene:**
   ```csharp
   using UnityEditor;
   using UnityEditor.SceneManagement;
   using UnityEngine.SceneManagement;
   public class Script { public static string Main() {
       var scene = SceneManager.GetActiveScene();
       EditorSceneManager.SaveScene(scene);
       return "OK: saved " + scene.path;
   }}
   ```

4. **Check if PvPMatch scene also has CombatFeedback:**

   Load and check:
   ```csharp
   using UnityEditor;
   using UnityEditor.SceneManagement;
   using UnityEngine;
   public class Script { public static string Main() {
       EditorSceneManager.OpenScene("Assets/Scenes/Arena_PvP.unity");
       var feedback = GameObject.FindObjectOfType<SlopArena.Client.Combat.CombatFeedback>(true);
       return feedback != null ? "FOUND" : "NOT FOUND";
   }}
   ```

If PvP scene has CombatFeedback, assign the prefab there too. Then save.

**Report file:** `.superpowers/sdd/task-8-report.md`

Write the report with:
- Which scenes were found to have CombatFeedback
- Whether the prefab assignment succeeded
- Whether scenes were saved
- Any errors

**Return status:** DONE / NEEDS_CONTEXT / BLOCKED and one-line summary.
