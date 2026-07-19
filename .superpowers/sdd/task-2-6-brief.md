# Tasks 2-6: Configure All Particle Modules

**Goal:** Configure every module on the `HitSpark_Debug` GameObject's ParticleSystem according to the spec.

**Context:** GameObject `HitSpark_Debug` exists in the active Unity scene with InstanceID `-3946`. MCP session SID is `9fRQp5HsSB5ZChKmJlahqQ`. The ParticleSystem has no material assigned (renders pink), all over-lifetime modules disabled, 10 max particles, sphere shape, 0.3s lifetime.

**Pre-requisites already done:**
- MCP session initialized (SID above)
- HitSpark_Debug instantiated in scene
- All module configs below use `gameObjectRef: {"instanceID": -3946}`

---

## Module Configurations

### Main Module (Task 2)

```
scripts/mcp-call.sh particle-system-modify \
  '{"gameObjectRef":{"instanceID":-3946},"main":{"startLifetime":{"minMaxState":0,"scalar":0.4},"startSpeed":{"minMaxState":0,"scalar":3},"startSize":{"minMaxState":0,"scalar":0.3},"maxParticles":50}}'
```

Changes: lifetime 0.3→0.4, speed 2→3, size 0.15→0.3, maxParticles 10→50.

### Emission Module (Task 3)

```
scripts/mcp-call.sh particle-system-modify \
  '{"gameObjectRef":{"instanceID":-3946},"emission":{"rateOverTime":{"minMaxState":0,"scalar":0},"bursts":[{"time":0,"count":{"minMaxState":0,"scalar":35},"cycleCount":1,"repeatInterval":0.01}]}}'
```

Changes: single burst of 35 particles at time 0 (was 0-8 random).

### Shape Module (Task 4)

The actual current shape is **Sphere**, not Cone as the plan assumed. Set it to Cone with wide angle for a directional fan burst.

```
scripts/mcp-call.sh particle-system-modify \
  '{"gameObjectRef":{"instanceID":-3946},"shape":{"shapeType":0,"angle":90,"radius":0.1}}'
```

`shapeType: 0` = Cone in Unity's ParticleSystemShape enum.

### Size over Lifetime (Task 5)

Enable and set a curve: particle holds full size for first 10% of life, then shrinks to near-zero.

```
scripts/mcp-call.sh particle-system-modify \
  '{"gameObjectRef":{"instanceID":-3946},"sizeOverLifetime":{"enabled":true}}'
```

Then use script-execute for the custom curve:
```csharp
using UnityEngine;
public class S { public static string Main() {
    var go = GameObject.Find("HitSpark_Debug");
    var ps = go.GetComponent<ParticleSystem>();
    var module = ps.sizeOverLifetime;
    module.enabled = true;
    var curve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.1f, 0.9f),     // hold near full size briefly
        new Keyframe(0.4f, 0.3f),     // shrink
        new Keyframe(1f, 0.05f)       // nearly gone at end
    );
    module.size = new ParticleSystem.MinMaxCurve(1f, curve);
    return "OK: size curve set";
}}
```

### Color over Lifetime (Task 6)

Enable and set a gradient: gold → orange → transparent.

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
            new GradientColorKey(new Color(1f, 0.84f, 0f), 0f),        // #FFD700 gold
            new GradientColorKey(new Color(1f, 0.27f, 0f), 0.4f),      // #FF4500 orange
            new GradientColorKey(new Color(0.5f, 0f, 0f), 0.8f)        // dark red
        },
        new GradientAlphaKey[] {
            new GradientAlphaKey(1f, 0f),    // full opaque
            new GradientAlphaKey(0.8f, 0.3f),
            new GradientAlphaKey(0f, 1f)     // transparent at end
        }
    );
    module.color = new ParticleSystem.MinMaxGradient(gradient);
    return "OK: color gradient set";
}}
```

### Renderer Module — Material (NEW, not in original plan)

CRITICAL: The ParticleSystemRenderer has no material. We need to find or create a suitable material.

Option A: Use URP's built-in `Default-Particle` material if it exists:
```csharp
using UnityEditor;
using UnityEngine;
public class S { public static string Main() {
    var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Default-Particle.mat");
    if (mat != null) return "Found at: " + AssetDatabase.GetAssetPath(mat);
    // Search for any built-in particle material
    var guids = AssetDatabase.FindAssets("Default-Particle t:Material");
    if (guids.Length > 0)
        return "Found: " + AssetDatabase.GUIDToAssetPath(guids[0]);
    return "NOT FOUND";
}}
```

If no default material exists, create one:
```csharp
using UnityEditor;
using UnityEngine;
public class S { public static string Main() {
    var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
    mat.name = "HitSpark";
    // Set additive blending
    mat.SetInt("_BlendOp", 0);
    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
    mat.SetInt("_ZWrite", 0);
    mat.renderQueue = 3000;
    AssetDatabase.CreateAsset(mat, "Assets/Art/Materials/HitSpark.mat");
    AssetDatabase.SaveAssets();
    return "Created: Assets/Art/Materials/HitSpark.mat";
}}
```

Then assign it to the renderer:
```csharp
using UnityEditor;
using UnityEngine;
public class S { public static string Main() {
    var go = GameObject.Find("HitSpark_Debug");
    var renderer = go.GetComponent<ParticleSystemRenderer>();
    var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/HitSpark.mat");
    if (mat == null) return "ERROR: material not found";
    renderer.material = mat;
    return "OK: material assigned";
}}
```

---

**Report file:** `.superpowers/sdd/task-2-6-report.md`

Write the report with:
- Each module change attempted and its result
- Any errors encountered (MCP call failures, API differences)
- Whether material was found or created
- Final verification: use `particle-system-get` to confirm all changes stuck
- The GameObject instanceID (-3946) for downstream tasks

**Return status:** DONE / NEEDS_CONTEXT / BLOCKED and one-line summary.
