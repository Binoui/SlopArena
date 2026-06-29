# Unity-MCP (gamedev-mcp-server)

Server: `gamedev-mcp-server` at `http://localhost:26356/mcp`. Configured in `.omp/mcp.json`.

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

## Session Management

Each session starts with an `initialize` request. The response includes a `Mcp-Session-Id` header — **every subsequent call needs it**:

```
Mcp-Session-Id: <token>
```

Pattern: `curl -s -D /tmp/headers.txt http://localhost:26356/mcp -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":1,"method":"initialize",...}'` then `SID=$(grep -i 'mcp-session' /tmp/headers.txt | awk '{print $2}' | tr -d '\r\n')` and pass `-H "Mcp-Session-Id: $SID"` on all further calls.

Session expires when Unity restarts or the MCP server restarts.

## Tool Reference

### Must-Know Patterns

| Goal | Tool | Notes |
|------|------|-------|
| Run arbitrary C# in Unity | `script-execute` (full mode) | `public static string Main()` returning `string` |
| Run simple statements | `script-execute` (body mode) | `isMethodBody: true`, `void` method, `Debug.Log()` for output |
| Read Unity console | `console-get-logs` | Filter by `logTypes: "Error"/"ScriptingLog"`, `maxEntries: N` |
| Enter/exit play mode | `editor-application-set-state` | `{ "isPlaying": true/false }` |
| Check editor state | `editor-application-get-state` | Returns `IsPlaying`, `IsCompiling`, `IsPaused` |
| Find GameObjects | `gameobject-find` | By name. Returns empty string if no match |
| Create GameObjects | `gameobject-create` | With `position`/`rotation`/`scale` |
| Add components | `gameobject-component-add` | Needs `gameObjectRef` with `instanceID` or `name` |
| Find assets | `assets-find` | `t:TypeName` filter, e.g. `t:InputActionAsset` |
| Refresh AssetDatabase | `assets-refresh` | Does NOT trigger C# recompilation (only asset reimport) |
| Inspect component fields | `script-execute` + reflection | `GetField("_fieldName", BindingFlags.Instance|BindingFlags.NonPublic)` |
| Take screenshot | `screenshot-isolated` | Render GameObject in isolation |

### Key Tools

**Scripting & Debugging:**
- `script-execute` — compile+run C# via Roslyn
- `script-update-or-create` — write a .cs file through Unity API (triggers recompile)
- `console-get-logs` / `console-clear-logs`
- `editor-application-get-state` / `editor-application-set-state`
- `tests-run` — run EditMode/PlayMode tests

**Scene:**
- `gameobject-find` / `gameobject-create` / `gameobject-destroy`
- `gameobject-component-add` / `gameobject-component-get` / `gameobject-component-modify`
- `scene-list-opened` / `scene-get-data` / `scene-save`

**Assets:**
- `assets-find` / `assets-get-data` / `assets-modify`
- `assets-prefab-instantiate` / `assets-prefab-create`
- `assets-shader-list-all` / `assets-shader-get-data`
- `assets-refresh` (asset reimport only)

**Extensions installed** (more MCP tools):
- AI InputSystem (`inputsystem-get`, `inputsystem-binding-add`, etc.)
- AI Cinemachine (`cinemachine-camera-get`, `cinemachine-set-aim`, etc.)

## Namespace Collisions (SlopArena Project)

Shared/ uses these namespaces that collide with Unity types:
- `SlopArena.Client.Camera` collides with `UnityEngine.Camera`
- `SlopArena.Client.Input` collides with `UnityEngine.Input`

**Always fully qualify**:
- `UnityEngine.Camera.main` not `Camera.main`
- `UnityEngine.Input.mousePosition` not `Input.mousePosition`

## C# Recompilation

**`assets-refresh` does NOT trigger C# script recompilation.** It only reimports non-script assets (textures, models, etc.).

To force script recompilation:
1. The real C# files are in `client/Unity/Assets/Scripts/Shared/`. `src/Shared/` contains symlinks. Writing through `src/Shared/` paths follows the symlink to the real file in Unity Assets.
2. After the file changes on disk, Unity's file watcher should detect it automatically (when not in play mode).
3. If it doesn't, use `script-execute` to call `UnityEditor.AssetDatabase.Refresh()` from inside Unity.
4. Entering play mode from a stopped state also triggers recompilation if needed.

## Common Script Templates

### Read scene hierarchy
```
isMethodBody: false
code:
  using UnityEngine; using UnityEngine.SceneManagement; using System.Text;
  public class S { public static string Main() {
    var sb = new StringBuilder();
    foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())
      sb.AppendLine(go.name);
    return sb.ToString();
  }}
```

### Inspect private field values
```
isMethodBody: false
code:
  using UnityEngine; using System.Reflection;
  public class Check { public static string Main() {
    var ai = GameObject.Find("TrainingMatch").GetComponentInChildren<AimIndicator>(true);
    var t = typeof(AimIndicator);
    var f = t.GetField("_isAiming", BindingFlags.Instance|BindingFlags.NonPublic);
    return $"aiming={f.GetValue(ai)}";
  }}
```

### Debug Log output
Body mode is void — use `Debug.Log()`, then read via `console-get-logs`:
```
isMethodBody: true
code: Debug.Log("my value=" + someVariable);
```

## Extensions

| Extension | Tools prefix | When to use |
|-----------|-------------|-------------|
| AI InputSystem | `inputsystem-*` | Inspect/modify Input Action assets and bindings |
| AI Cinemachine | `cinemachine-*` | Camera orbit control, virtual camera config |
