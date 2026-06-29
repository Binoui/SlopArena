# Unity-MCP (gamedev-mcp-server)

Unity MCP tools for AI-driven Unity Editor operations. Server: `gamedev-mcp-server` at `http://localhost:26356/mcp`.

## Config

```json
{
  "mcpServers": {
    "unity-game-dev": {
      "type": "sse",
      "url": "http://localhost:26356/mcp"
    }
  }
}
```

File: `.omp/mcp.json`. Server auto-starts when Unity is open with the AI Game Developer plugin.

## Tool Categories

### Scene & Hierarchy
- `gameobject-find` — find GameObjects by name/component/InstanceID. Returns `instanceID`.
- `gameobject-create` — create GameObject at position/rotation/scale
- `gameobject-destroy` — destroy GameObject recursively
- `gameobject-modify` — modify transform/component fields
- `gameobject-set-parent` — reparent GameObjects
- `gameobject-component-add` — add component (needs `gameObjectRef` with `instanceID` or `name`)
- `gameobject-component-get` — get component data
- `gameobject-component-modify` — modify component fields
- `gameobject-component-destroy` — remove components
- `gameobject-component-list-all` — list all C# component types

### Assets
- `assets-find` — search assets by name/label/type. `t:classname` to filter by type
- `assets-get-data` — inspect asset serialized fields
- `assets-modify` — modify asset fields
- `assets-prefab-instantiate` — instantiate prefab into scene
- `assets-prefab-create` — create prefab from scene GameObject
- `assets-material-create` — create new material
- `assets-refresh` — refresh AssetDatabase (call after file changes)

### Scripting
- `script-execute` — **PRIMARY tool for arbitrary Unity operations.** Compiles+executes C# via Roslyn.
  - Two modes: `isMethodBody: false` (full class) or `isMethodBody: true` (body only, void return)
  - Full mode creates a class with a static `Main()` returning `string` for results:
    ```
    using UnityEngine; public class X { public static string Main() { ... return result; } }
    ```
  - Body mode auto-generates boilerplate, `return` disallowed (void method)
  - Parameters: `{"typeName":"System.String","name":"goName","value":"Player"}`
- `script-read` — read C# script file content
- `script-update-or-create` — write/modify C# script file

### Editor & Debugging
- `editor-application-get-state` — check playmode state (Playing/Paused/Compiling)
- `editor-application-set-state` — start/stop/pause playmode
- `console-get-logs` — get Unity console logs (filter by log type, max entries)
- `console-clear-logs` — clear console
- `screenshot-isolated` — render GameObject in isolation (composite 2x2 view)
- `screenshot-game-view` — capture Game View
- `screenshot-scene-view` — capture Scene View
- `tests-run` — run EditMode/PlayMode tests

### Shaders
- `assets-shader-list-all` — list all available shaders (URP, Built-in, custom)
- `assets-shader-get-data` — inspect shader properties/subshaders/passes

## Common Patterns

### Get scene info
```
script-execute (isMethodBody=false):
  using UnityEngine; using UnityEngine.SceneManagement;
  public class S { public static string Main() {
    var sb = new System.Text.StringBuilder();
    foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())
      sb.AppendLine(go.name);
    return sb.ToString();
  }}
```

### Find and modify a component
1. `gameobject-find` with name to get instanceID
2. `gameobject-component-get` with instanceID + component type
3. `gameobject-component-modify` with instanceID + component data

### Wire a serialized field at runtime
```
script-execute (isMethodBody=false):
  using UnityEngine; using System.Reflection;
  public class W { public static string Main() {
    var tm = GameObject.Find("TrainingMatch").GetComponent<TrainingMatch>();
    var ai = tm.GetComponent<AimIndicator>();
    var f = typeof(TrainingMatch).GetField("_aimIndicator", BindingFlags.NonPublic|BindingFlags.Instance);
    f.SetValue(tm, ai);
    UnityEditor.EditorUtility.SetDirty(tm);
    return "ok";
  }}
```

### Create material with URP shader
```
script-execute body:
  var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
  mat.SetFloat("_Surface", 1f); // Transparent
  mat.color = new Color(1f, 0.4f, 0.1f, 0.6f);
```

## Important Notes

- **Session ID**: Each `initialize` returns a `Mcp-Session-Id` header. ALL subsequent calls MUST include the session: `Mcp-Session-Id: <token>`. Session expires when Unity restarts.
- **`shader.Find("Standard")` returns null in URP/HDRP projects**. Use `"Sprites/Default"` for simple colored transparent materials, or `"Universal Render Pipeline/Lit"` with `_Surface=1`.
- **`Camera` and `Input` classnames collide with `SlopArena.Client.Camera` and `SlopArena.Client.Input` namespaces**. Always fully qualify: `UnityEngine.Camera.main`, `UnityEngine.Input.mousePosition`.
- **`gameobject-find`** returns empty string if no match — use script-execute for complex queries.
- **Script-execute full mode** expects `public static string Main()` returning the result string. Body mode auto-generates void method.

## Related
- Repo: https://github.com/IvanMurzak/Unity-MCP
- The MCP config lives in `.omp/mcp.json` — add additional servers there
