# Match Architecture Plan

## Problem

`TrainingMatch` does two unrelated jobs:

1. **Boilerplate that every match type needs:** arena load, renderer wiring, camera/HUD/AimHandler
   init, sim tick loop, applying states to renderers.
2. **Training-specific logic:** NPC AI, debug tick log, hitbox visualization.

Right now (1) and (2) are interleaved. When we add a second scene — online PvP, a character
select, a stage select — we either copy all of (1) or we factor it out. Factoring it out later
is always harder than factoring it out at the natural seam. This plan defines that seam.

---

## The Key Abstraction: `ISimulationSource`

Every match type answers the same question each tick: *"what are the current entity states?"*.
How that question is answered differs:

| Match type       | Source                                                           |
|------------------|------------------------------------------------------------------|
| Training (local) | `ServerSimulation.Tick(inputs)` → read back states              |
| Online PvP       | `NetworkClient.ReceiveStates()` (server is authoritative)        |
| Replay viewer    | `ReplayReader.ReadFrame(tick)` (pre-recorded states)             |
| Spectator        | Same as online — received states, no local input sent            |

Define this interface in `SlopArena.Client.Simulation`:

```csharp
public interface ISimulationSource
{
    /// <summary>Submit local player input for this tick.</summary>
    void SubmitInput(ulong entityId, InputState input);

    /// <summary>Advance one tick (no-op for network/replay sources).</summary>
    void Tick();

    /// <summary>Read back all entity states after this tick.</summary>
    IReadOnlyDictionary<ulong, CharacterState> GetStates();

    /// <summary>Resolver for hitbox gizmos — null on network sources.</summary>
    SpellResolver? Resolver { get; }
}
```

`LocalSimulationBridge` (already exists, currently unused) becomes the local implementation.
`NetworkMatch` wires `NetworkClient` as the source.

---

## Target Structure

```
MatchBase (abstract MonoBehaviour)
│
│  Owns: arena load, renderer pool, camera, AimHandler, HUD,
│        CombatFeedback, TargetIndicator, sim tick loop,
│        ApplyServerState calls, OnGUI crosshair
│
│  Abstract:
│    ISimulationSource CreateSource(ArenaDefinition arena)
│    void RegisterEntities(ISimulationSource source, ArenaDefinition arena)
│    InputState? GetExtraInputs(ulong entityId)  // NPC AI, bot inputs, etc.
│
├── TrainingMatch
│     CreateSource  → LocalSimulationBridge
│     RegisterEntities → player + NPC entity registration
│     GetExtraInputs → NPC AI (BuildNpcInput)
│     Debug hitbox viz (UNITY_EDITOR only, stays here)
│
└── NetworkMatch (future)
      CreateSource  → NetworkClientSource wrapping NetworkClient
      RegisterEntities → player entity only (server owns NPC/opponent)
      GetExtraInputs → null (no extra entities to drive locally)
```

---

## What Moves to `MatchBase`

These blocks in `TrainingMatch.OnMatchStart` are identical in every match type and move up:

```
Arena load (LoadArenaFromFile / ArenaRegistry.Get)   → already in MatchBase as static helper
Player renderer wiring (model, capsule, baked data)  → MatchBase.SetupRenderer(PlayerRenderer, CharacterDefinition)
Camera setup (SetTarget, ResetView)                  → MatchBase.SetupCamera()
AimHandler.Init                                      → MatchBase.OnMatchStart
HUDManager.Initialize + SetSlotMaxCooldown           → MatchBase.OnMatchStart
CombatFeedback.SetSimulation                         → MatchBase.OnMatchStart
TargetIndicator.Init                                 → MatchBase.OnMatchStart
_mainCamera assignment                               → MatchBase field
```

`MatchBase.OnMatchFixedUpdate` owns:
```
_inputController.Poll (already in Update, stays there)
_aimHandler.Evaluate → AimContext
_inputController.BuildInputState for local player
source.SubmitInput + source.Tick
GetExtraInputs() loop (drives NPC / bots)
source.GetStates() → ApplyServerState for each renderer
_combatFeedback.OnTick
_hudManager.Refresh
_showCrosshair from _aimHandler.ShowCrosshair
```

`TrainingMatch.OnMatchFixedUpdate` becomes only:
```
(nothing — base handles it all)
```
`TrainingMatch` overrides only `CreateSource`, `RegisterEntities`, `GetExtraInputs`.

---

## `LocalSimulationBridge` as `ISimulationSource`

`LocalSimulationBridge` already proxies `ServerSimulation`. Implement the interface:

```csharp
public class LocalSimulationBridge : ISimulationSource
{
    private readonly Dictionary<ulong, InputState> _inputs = new();

    public void SubmitInput(ulong entityId, InputState input)
        => _inputs[entityId] = input;

    public void Tick()
    {
        _server.Tick(_inputs);
        _inputs.Clear();
    }

    public IReadOnlyDictionary<ulong, CharacterState> GetStates()
        => _server.GetAllStates();

    public SpellResolver? Resolver => _server.Resolver;
}
```

---

## `NetworkMatch` sketch (future — not implementing now)

```csharp
public class NetworkMatch : MatchBase
{
    [SerializeField] private NetworkClient _networkClient;
    [SerializeField] private CharacterClass _playerClass;

    protected override ISimulationSource CreateSource(ArenaDefinition arena)
        => new NetworkClientSource(_networkClient);

    protected override void RegisterEntities(ISimulationSource source, ArenaDefinition arena)
    {
        // Server owns entity registration; client just registers a local renderer placeholder
        // so MatchBase can call ApplyServerState on the right renderer.
    }

    protected override InputState? GetExtraInputs(ulong entityId) => null;
}
```

---

## What Stays in `TrainingMatch` Forever

- NPC AI (`BuildNpcInput`, `BuildAttackInput`, `BuildIdleInput`) — training-specific
- `_npcAiMode` inspector toggle (Attack / Idle)
- `_npcLastDeaths` tracking + `OnDeath()` call
- Debug tick log every 120 ticks
    - `DrawHitboxGizmos` / `DrawHitboxDebug` / `DebugDrawWireSphere` (hitboxes from `GetActiveHitboxes()`)
    - `DrawHurtboxGizmos` / `DrawHurtboxDebug` (hurtboxes from `GetLastEntityData()`)
- `_showHitboxes` inspector flag

These are dev/training tools. They never belong in another match type.

---

## `PickScreenTarget` — where it lives

Currently in `TrainingMatch`. It's match-agnostic (any match with multiple renderers needs
soft-lock targeting). Move to `MatchBase` as a protected helper. It only needs the renderer
list and a camera — both available in `MatchBase`.

---

## `OnGUI` crosshair — where it lives

Currently in `TrainingMatch`. The crosshair is driven by `_aimHandler.ShowCrosshair`, which
is already in `AimHandler`. The `OnGUI` draw itself is 8 lines that belong in `MatchBase`
since any match type with aimed abilities needs it.

---

## Migration Sequence

**Do NOT do this now.** Do it when adding the second scene (online PvP or character select).
At that point the duplication becomes concrete and the right boundaries are confirmed.

When the time comes:

1. Add `ISimulationSource` interface to `SlopArena.Client.Simulation`
2. Implement it on `LocalSimulationBridge`
3. Move shared wiring from `TrainingMatch.OnMatchStart` into `MatchBase.OnMatchStart`
   (extract `SetupRenderer` helper)
4. Move tick loop from `TrainingMatch.OnMatchFixedUpdate` into `MatchBase.OnMatchFixedUpdate`
5. Add abstract `CreateSource` / `RegisterEntities` / `GetExtraInputs` to `MatchBase`
6. `TrainingMatch` overrides only those three methods + keeps NPC/debug code
7. Write `NetworkMatch` against the same base

Estimated scope: ~2 hours, ~150 lines moved (not rewritten). `TrainingMatch` shrinks to
~80 lines.

---

## What This Unlocks

| Feature                       | Without this plan          | With this plan               |
|-------------------------------|----------------------------|------------------------------|
| Online 1v1 match              | Copy 150 lines of wiring   | Override 3 methods           |
| Replay viewer                 | Copy 150 lines of wiring   | Override 3 methods           |
| 2nd character in training     | Nothing to change          | Nothing to change            |
| Spectator mode                | Copy 150 lines of wiring   | Override 3 methods           |
| HUD in online match           | Re-wire from scratch       | Already works (delegate)     |
| AimHandler in online match    | Re-wire from scratch       | Already works (in MatchBase) |
