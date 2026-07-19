# Task 9: Test in TrainingMatch (Play Mode)

**Goal:** Enter play mode and verify the hit spark VFX works correctly when attacks connect.

**Context:** All modules configured, prefab saved at `Assets/Prefabs/VFX/HitSpark.prefab`, scenes wired. Now we test by entering play mode in the TrainingMatch scene (Arena_Offline) and hitting the NPC.

**Pre-requisites:**
- HitSpark.prefab at `Assets/Prefabs/VFX/HitSpark.prefab`
- HitSpark.mat at `Assets/Art/Materials/HitSpark.mat`
- Arena_Offline scene has CombatFeedback wired with the new prefab

**Steps:**

1. **Open the Arena_Offline scene:**
   ```csharp
   using UnityEditor.SceneManagement;
   public class Script { public static string Main() {
       EditorSceneManager.OpenScene("Assets/Scenes/Arena_Offline.unity");
       return "OK: opened";
   }}
   ```
   (Or check the actual path with assets-find first)

2. **Enter play mode:**
   ```
   scripts/mcp-call.sh editor-application-set-state '{"isPlaying":true}'
   ```
   Expected: Editor enters play mode.

3. **Wait a few seconds for scene to initialize, then attack:**
   
   The player needs to move toward the NPC and attack. We might need to send input via InputController. But first, just entering play mode and checking console is useful.

4. **Check console for errors:**
   ```
   scripts/mcp-call.sh console-get-logs '{"maxEntries":20,"logTypes":"Error"}'
   ```
   Expected: No errors related to CombatFeedback, ParticleSystem, missing material, or prefab.

5. **If possible, trigger a hit by instructing the user to:** (skip if user isn't available)
   - Move toward the NPC dummy using WASD
   - Press the attack key (left click or assigned button)
   - Observe the gold-orange burst of particles at the hit point

   Otherwise, just check errors from initialization (CombatFeedback.OnTick runs every FixedUpdate).

6. **Exit play mode:**
   ```
   scripts/mcp-call.sh editor-application-set-state '{"isPlaying":false}'
   ```

7. **Save scene:**
   ```csharp
   using UnityEditor.SceneManagement;
   using UnityEngine.SceneManagement;
   public class Script { public static string Main() {
       var scene = SceneManager.GetActiveScene();
       EditorSceneManager.SaveScene(scene);
       return "OK: saved";
   }}
   ```

**Report file:** `.superpowers/sdd/task-9-report.md`

Write the report with:
- Play mode entered successfully?
- Any errors in console
- If user tested visually, what they observed
- Exit play mode cleanly?
- Scene saved?

**Return status:** DONE / NEEDS_CONTEXT / BLOCKED and one-line summary.
