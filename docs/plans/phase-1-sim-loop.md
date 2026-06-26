# Phase 1 Subplan — Match Loop + Local Sim

> **Deliverable:** Manki moves + jumps + faces camera direction in a clean sim loop. No server process. Dummy NPC stands idle. No VFX, no UI, no network.
> **Godot reference:** `Scripts/World/TrainingMatch.cs` (tick loop), `Scripts/Server/LocalServerBridge.cs` (sim wrapper)

---

## Files to Create

### Task 1.1 — `LocalSimulationBridge.cs`

**Path:** `client/Unity/Assets/Scripts/Runtime/Simulation/LocalSimulationBridge.cs`

**Purpose:** Thin wrapper around `ServerSimulation`. Same role as Godot's `LocalServerBridge` but zero Unity or Godot types — pure C# talking to `Shared/`.

**API:**
```csharp
public class LocalSimulationBridge
{
    private readonly ServerSimulation _server;
    private readonly ArenaDefinition _arena;

    public LocalSimulationBridge(ArenaDefinition arena);
    public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null);
    public void Tick(Dictionary<ulong, InputState> inputs);
    public CharacterState GetState(ulong id);
    public Dictionary<ulong, CharacterState> GetAllStates();
    public SpellResolver Resolver { get; }  // exposed for debug draw later
}
```

**Tick() details:** Calls `_server.Tick(inputs)`. No event routing (that's Phase 2). Simple pass-through.

---

### Task 1.2 — `MatchBase.cs`

**Path:** `client/Unity/Assets/Scripts/Runtime/World/MatchBase.cs`

**Purpose:** Abstract base for TrainingMatch and future PvPMatch. Holds shared infrastructure: loading arena data, loading baked skeleton data.

**API:**
```csharp
public abstract class MatchBase : MonoBehaviour
{
    // Children override
    protected abstract void OnMatchStart();
    protected abstract void OnMatchFixedUpdate();

    // Shared helpers (static, no state needed)
    protected static ArenaDefinition LoadArena(string path);
    protected static BakedAnimationData? LoadBakedData(CharacterDefinition def);
}
```

**LoadArena():** Uses `ArenaBinaryFormat.LoadFromFile()`. Resolves path relative to project root (same pattern as current `GameManager.LoadArenaDef()`).

**LoadBakedData():** Reads `def.BakedDataPath`, loads the `.bin` file with `File.ReadAllBytes()`, calls `BakedAnimationData.LoadFromBin()`. Returns null if path is empty or file missing. Ported from `Scripts/World/TrainingMatch.cs:137-158`.

---

### Task 1.3 — `TrainingMatch.cs`

**Path:** `client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs`

**Purpose:** Concrete training match. Creates local `ServerSimulation`, registers player + NPCs, runs tick loop each `FixedUpdate`.

**Fields:**
```csharp
[Header("Entities")]
[SerializeField] private PlayerRenderer _playerRenderer;
[SerializeField] private PlayerRenderer[] _npcRenderers;

[Header("Input")]
[SerializeField] private InputController _inputController;
[SerializeField] private CameraMount _cameraMount;

[Header("Arena")]
[SerializeField] private string _arenaPath = "Island_arena";  // key, not path

// Runtime state
private ServerSimulation _localSim;
private CharacterDefinition _charDef;
private const ulong PlayerEntityId = 1;
private const ulong NpcBaseId = 100;
```

**Start():**
1. Load arena via `ArenaRegistry.Get(_arenaPath)` (or `LoadArena` from file)
2. Create `_localSim = new ServerSimulation(arena)`
3. Load `_charDef = CharacterRegistry.Get(CharacterClass.Manki)` (hardcoded for now)
4. Load baked data: `_playerBakedData = LoadBakedData(_charDef)`
5. Register player entity 1 at spawn point 0: `new CharacterState { PX=spawn.X, PY=spawn.Y+5f, PZ=spawn.Z, FacingYaw=spawn.Yaw, JumpsLeft=_charDef.Movement.MaxJumps }`
6. For each NPC renderer: register entity 100+i at spawn point i+1: `new CharacterState { PX=spawn.X, PY=spawn.Y+1f, PZ=spawn.Z, FacingYaw=spawn.Yaw+PI }`
7. Set `_playerRenderer` transform to spawn position + (0, 5, 0)
8. Set camera target to player transform

**FixedUpdate():**
1. If `_localSim == null || _playerRenderer == null` → return
2. `_inputController.Poll()`
3. `byte slot = _inputController.ConsumePendingSlotPress()`
4. `var (input, moveDir, snappedDir) = _inputController.BuildInputState(_cameraMount, _playerRenderer.transform.eulerAngles.y, false, false, slot, null, null, null)`
5. `_localSim.Tick(new Dictionary<ulong, InputState> { { PlayerEntityId, input } })`
6. `_playerRenderer.ApplyServerState(_localSim.GetState(PlayerEntityId))`
7. For each NPC: `_npcRenderers[i].ApplyServerState(_localSim.GetState(NpcBaseId + (ulong)i))`

**Notes:**
- NPCs get NO input — they just stand idle (that's Phase 3)
- No warp sync yet (Phase 2+)
- No baked data for NPCs (simplification — the sim handles hurtboxes even without baked anim data)
- Arena visual: skip for now — just a flat plane. Arena loading visuals is Phase 9.
- No heightmap generation

---

## Files to Modify

### Task 1.4 — `GameManager.cs` — Strip hacky offline

**Path:** `client/Unity/Assets/Scripts/Runtime/World/GameManager.cs`

**Remove:**
- `_netClient` field (moved to NetworkSimulationBridge in Phase 6)
- `_serverProcess` field
- `_tick` field
- `_offlineMode` field
- `_playerRenderer` / `_opponentRenderer` serialized fields
- `Player` / `Opponent` properties
- `_arenaDef` / `_charDef` fields
- `Start()` — delete entirely (was the hacky launch-server path)
- `FixedUpdate()` — delete entirely
- `BuildInputState()` — delete entirely (InputController handles this now)
- `LaunchServer()` — delete entirely
- `KillProcessOnPort()` — delete entirely
- `LoadArenaDef()` — move to `MatchBase`

**Keep:**
- `Awake()` — singleton pattern + DontDestroyOnLoad
- `OnDestroy()` — clean up singleton (remove server process kill)
- `Instance` — static accessor

**After refactor, GameManager should be ~20 lines:**
```csharp
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
```

---

## Verification

After all tasks:
1. Open `Arena_Offline.unity` scene (or a test scene with TrainingMatch + CameraMount + Player + Dummy + InputController)
2. Press Play
3. WASD moves Manki in camera-relative 8 directions
4. Space jumps (double-jump works)
5. Mouse orbits camera
6. Dummy NPC stands idle at spawn position
7. No terminal window with `dotnet run` opens
8. Console has no errors

**Smoke test command:** Open Unity, press Play, move around for 10 seconds. Should feel smooth.

---

## Execution Order

Tasks 1.1, 1.2 can run in parallel.
Task 1.3 depends on 1.1.
Task 1.4 depends on 1.3 (deletes code TrainingMatch replaces).

**Recommended:** single subagent does all four sequentially — they're tightly coupled.

---

## Current Status (2026-06-27)

**Completed:**
- ✅ Phase 1 core implemented and functional
- Manki moves with WASD (camera-relative 8-direction)
- Jump with JumpSquat state (6 ticks grounded prep)
- Double jump works
- Mouse orbits camera (InputAxisController)
- HardLookAt aim component keeps camera facing player
- Frame-by-frame animation driving (sim controls animation frame, not triggers)
- Jump/dash input uses manual edge detection (not `wasPressedThisFrame`)
- NPC spawns and stands idle
- Bake skeleton tool ported to Unity editor
- Hurtbox debug visualization via Gizmos

**In flight / known issues:**
- Jump input detection: `isPressed` edge tracking works, but jump may still fail if
  `JumpsLeft` resets 1 tick after landing (step 5.75 runs before ProcessGroundMovement).
  Impact: first grounded tick rejects jump if both jumps were used. Imperceptible (~16ms)
  unless compounded with other issues.
- Jump animation timing: frame-by-frame driving is in, but baked data frame counts
  are fallback 30 frames when baked data is unavailable.
- Arena ground collision: heightmap sampling works but terrain bake may need refresh.

**Server refactor needed:**
`Simulation.cs` is growing complex — the tick method is ~260 lines with cross-cutting
state (JumpSquat expiry, jump detection, ground collision, gravity, position update)
interleaved with state machine logic. Future additions (berserk mode, debuffs, item state,
online sync) will compound this. Suggested direction:
- Extract jump/jumpsquat into dedicated static methods (like `ProcessDash`, `ProcessKnockback`)
- Move ground collision into a method (called from both normal sim and knockback paths)
- Consider a tick-phase pipeline (array of `ISimPhase`) if state interactions grow beyond
  the current linear flow
