# C# AnimationTree Builder — Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Replace .tscn-based AnimationTree setup with a C# builder that generates the
`AnimationNodeStateMachine` from `CharacterDefinition` data. No more hand-editing
`.tscn` state machines for new characters.

**Architecture:** Two new files (`AnimationTreeBuilder.cs`, `AnimationClipConfig` in Shared),
small additions to `CharacterDefinition` (pure C# string fields, no Godot deps), and
per-character data updates. The builder produces the exact same AnimationNodeStateMachine
structure currently encoded in `.tscn` — same state names, same TimeScale parameters,
same transitions. Nothing in the FSM or C# states changes.

**Tech Stack:** Godot 4.6.3 C#, pure-C# Shared/ library (no Godot references)

---

## Design Constraints

### Shared/ MUST stay pure C#
No `Godot.*` imports. No `AnimationNode*` types. No `AnimationTree` references.
The `AnimationClipConfig` struct uses only `string`, `float?`, `bool?`, and a
`ClipLoopMode : byte` enum — all valid in `System`-only context.

### Builder lives in Scripts/Animation/
Only the `AnimationTreeBuilder` touches Godot types. It reads `CharacterDefinition`
and produces `AnimationNodeStateMachine`.

### State names MUST match current .tscn
The builder produces states with the same names the FSM calls via `Travel()`:
`idle`, `run`, `air`, `land`, `dash`, `jump`, `hit_small`, `hit_medium`,
`hit_hard`, `spell_lmb_1`, `spell_lmb_2`, etc.

### TimeScale parameter paths MUST stay identical
`ApplyAnimationTimeScales()` calls `animTree.Set("parameters/{name}/TimeScale/scale", value)`.
The builder must create `AnimationNodeTimeScale` nodes so these paths exist.

---

## What Changes vs What Stays

| Layer | Changes | Unchanged |
|-------|---------|-----------|
| Shared/ | Add locomotion/hit-reaction anim name fields + `AnimationClipConfig` struct | `AbilitySpec.AnimationNames` — already exists |
| Scripts/Animation/ | New `AnimationTreeBuilder.cs` | `StateMachine.cs`, all State files — zero changes |
| Scripts/Entities/ | `PlayerController._Ready()` calls builder instead of loading .tscn sub-resources | `ApplyAnimationTimeScales()`, FSM init, everything else |
| Data files | MankiData.cs, BunnyData.cs — add new string fields | `AbilitySpec.AnimationNames` — already populated |
| .tscn files | Strip all `[sub_resource]` blocks + `tree_root` + `parameters/*/TimeScale/scale` | AnimationTree node, FSM tree, model — all preserved |

---

## New Data Structures

### ClipLoopMode (Shared/)
```csharp
namespace SlopArena.Shared
{
    /// <summary>Matches Godot's Animation.LoopModeEnum without importing Godot.</summary>
    public enum ClipLoopMode : byte
    {
        None = 0,
        Linear = 1,
        PingPong = 2,
    }
}
```

### AnimationClipConfig (Shared/)
```csharp
namespace SlopArena.Shared
{
    public struct AnimationClipConfig
    {
        /// <summary>Animation name (must match a GLB animation + AnimationTree state name).</summary>
        public string Name;

        /// <summary>Overrides the default loop mode for this clip.</summary>
        public ClipLoopMode? LoopMode;

        /// <summary>Custom timeline start offset in seconds (e.g., 0.49 for landing).</summary>
        public float? StartOffset;

        /// <summary>Custom timeline length in seconds. When set, StretchTimeScale is implied.</summary>
        public float? TimelineLength;

        /// <summary>Stretch the clip to fill the custom timeline. Default: false.</summary>
        public bool StretchTimeScale;
    }
}
```

### New fields on CharacterDefinition (Shared/)
```csharp
// ── Animation catalog (defaults match Mixamo naming) ──

/// <summary>Idle animation clip name. Default: "idle"</summary>
public string IdleAnim = "idle";

/// <summary>Run animation clip name. Default: "run"</summary>
public string RunAnim = "run";

/// <summary>Dash animation clip name. Default: "dash"</summary>
public string DashAnim = "dash";

/// <summary>Jump animation clip (BlendSpace1D position -1). Default: "jump"</summary>
public string JumpAnim = "jump";

/// <summary>Fall animation clip (BlendSpace1D position +1). Default: "fall"</summary>
public string FallAnim = "fall";

/// <summary>Small hit reaction clip. Default: "small_hit"</summary>
public string HitSmallAnim = "small_hit";

/// <summary>Medium hit reaction clip. Default: "medium_hit"</summary>
public string HitMediumAnim = "medium_hit";

/// <summary>Hard hit reaction clip. Default: "hard_hit"</summary>
public string HitHardAnim = "hard_hit";

/// <summary>Landing uses JumpAnim clip with this start offset (seconds). Default: 0.49f</summary>
public float LandStartOffset = 0.49f;

/// <summary>Per-clip overrides for non-default timeline/loop settings.</summary>
public AnimationClipConfig[]? ClipOverrides;
```

Defaults cover all Mixamo characters (Manki). Bunny needs:
- `HitSmallAnim = "hit_small"` (not "small_hit") — verify from GLB
- All other defaults likely match

No changes to AbilitySpec, AerosolFlameSpec, or RoundBombSpec. `ChargeAnimName` and
`LoopAnimName` are already there.

---

## AnimationTreeBuilder Architecture

```
AnimationTreeBuilder.Build(animPlayer, charDef) → AnimationNodeStateMachine

Build() calls:
  1. CreateStateMachine(name)
       → new AnimationNodeStateMachine()
  2. AddLocomotionStates(sm, charDef)
       → idle, run, dash (Animation + TimeScale + BlendTree wrapper)
  3. AddAirBlendSpace(sm, charDef)
       → "air" = BlendSpace1D with jump(-1) + fall(+1)
       → "jump" = standalone TimeScale-wrapped jump clip
  4. AddLandingState(sm, charDef)
       → "land" = JumpAnim clip with start_offset=LandsStartOffset
  5. AddHitReactionStates(sm, charDef)
       → hit_small, hit_medium, hit_hard
  6. AddAttackStates(sm, charDef)
       → reads all AbilitySpec.AnimationNames from LMB/AirLMB/RMB/AirRMB/Q/E/R/F
       → creates (Animation + TimeScale + BlendTree) per unique animation name
  7. AddChargeLoopStates(sm, charDef)
       → reads AerosolFlameSpec.ChargeAnimName, RoundBombSpec.LoopAnimName
  8. AddTransitions(sm, charDef)
       → generates all from→to transitions from rules
```

### Helper: CreateWrappedState(animPlayer, clipName, config?)
Creates the standard triplet:
```
AnimationNodeBlendTree (output)
  ├── AnimationNodeTimeScale
  └── AnimationNodeAnimation (clipName, loop_mode, timeline, start_offset)
```

Returns the BlendTree (which is the state node added to the StateMachine).

### BlendSpace1D for Air
```csharp
var blendAir = new AnimationNodeBlendSpace1D();
blendAir.BlendMode = AnimationNodeBlendSpace1D.BlendModeEnum.Discrete; // blend_mode=1
blendAir.Snap = 0.14f;
blendAir.AddBlendPoint(CreateSimpleAnim(animPlayer, charDef.JumpAnim), -1f);
blendAir.AddBlendPoint(CreateSimpleAnim(animPlayer, charDef.FallAnim), 1f);
```

Note: `AnimationNodeBlendSpace1D.AddBlendPoint()` creates internal `AnimationRootNode`
references — the animations don't need TimeScale wrappers inside a BlendSpace
(BlendSpace1D doesn't support them anyway — confirmed in the godot-animation-tree skill).

### Transition Rules
The builder generates transitions from these rules:
```csharp
var transitions = new List<(string from, string to, bool atEnd)>();

// Core movement
Add(transitions, "Start", "idle", atEnd: false);
Add(transitions, "idle", "run", atEnd: false);    // xfade_015
Add(transitions, "run", "idle", atEnd: false);    // xfade_015

// Air transition
Add(transitions, "idle", "air", atEnd: false);    // xfade_01
Add(transitions, "run", "air", atEnd: false);     // xfade_01
Add(transitions, "air", "land", atEnd: false);    // xfade_015

// Landing → idle/run (at end of animation)
Add(transitions, "land", "idle", atEnd: true);    // xfade_at_end
Add(transitions, "land", "run", atEnd: false);    // xfade_015

// Dash
Add(transitions, "idle", "dash", atEnd: false);
Add(transitions, "run", "dash", atEnd: false);
Add(transitions, "air", "dash", atEnd: false);
Add(transitions, "dash", "End", atEnd: false);

// Jump (standalone state for single-frame takeoff)
Add(transitions, "idle", "jump", atEnd: false);
Add(transitions, "run", "jump", atEnd: false);
Add(transitions, "land", "jump", atEnd: false);
Add(transitions, "jump", "End", atEnd: false);

// Hit reactions → End only (FSM handles entry)
Add(transitions, "hit_small", "End", atEnd: false);
Add(transitions, "hit_medium", "End", atEnd: false);
Add(transitions, "hit_hard", "End", atEnd: false);

// Attack states: idle/run/air → state, state → End
foreach (var animName in allAttackAnimNames)
{
    Add(transitions, "idle", animName, atEnd: false);
    Add(transitions, "run", animName, atEnd: false);
    Add(transitions, "air", animName, atEnd: false);
    Add(transitions, animName, "End", atEnd: false);
}

// Charge loop states: idle/run/air → loop, loop → End
foreach (var loopName in chargeLoopAnimNames)
{
    Add(transitions, "idle", loopName, atEnd: false);
    Add(transitions, "run", loopName, atEnd: false);
    Add(transitions, "air", loopName, atEnd: false);
    Add(transitions, loopName, "End", atEnd: false);
}

// Special: charge loop → attack (e.g., spell_rmb_loop → spell_rmb_attack)
if (hasRmbChargeLoop && hasRmbAttack)
    Add(transitions, rmbChargeLoopName, rmbAttackName, atEnd: false);

// Special: spell_q_loop → spell_q (Manki only, if both exist)
if (hasQLoop && hasQAttack)
    Add(transitions, qLoopName, qAttackName, atEnd: false);
```

Crossfade values:
- `atEnd=false` → 0.15s crossfade (`xfade_015`)
- `atEnd=true` → `AnimationNodeStateMachineTransition { Reset = false }` (waits for clip end)
- `xfade_01` (0.1s) not used in current .tscn for movement-to-attack transitions — 0.15s everywhere

All crossfades use 0.15s. The current .tscn uses `xfade_01` (0.1s) for some cases
and `xfade_015` for others. Consolidating to 0.15s everywhere simplifies the builder
and is imperceptible.

### Clip Override Application
The builder reads `ClipOverrides[]` after creating each animation node:

```csharp
void ApplyOverrides(AnimationNodeAnimation node, string clipName, CharacterDefinition def)
{
    if (def.ClipOverrides == null) return;
    foreach (var cfg in def.ClipOverrides)
    {
        if (cfg.Name != clipName) continue;
        if (cfg.LoopMode.HasValue) node.LoopMode = (Animation.LoopModeEnum)(int)cfg.LoopMode.Value;
        if (cfg.StartOffset.HasValue) { node.UseCustomTimeline = true; node.StartOffset = cfg.StartOffset.Value; }
        if (cfg.TimelineLength.HasValue) { node.UseCustomTimeline = true; node.TimelineLength = cfg.TimelineLength.Value; }
        if (cfg.StretchTimeScale) node.StretchTimeScale = true;
        break;
    }
}
```

---

## Task List

### Phase 0: Data Structures (Shared/ — no Godot deps)

#### Task 0.1: Add ClipLoopMode enum
**Files:** Create `Shared/AnimationClipConfig.cs` (new file)

```csharp
namespace SlopArena.Shared
{
    /// <summary>Matches Godot's Animation.LoopModeEnum without importing Godot.</summary>
    public enum ClipLoopMode : byte
    {
        None = 0,
        Linear = 1,
        PingPong = 2,
    }
}
```

**Verify:** `dotnet build Shared/SlopArena.Shared.csproj` — passes.

---

#### Task 0.2: Add AnimationClipConfig struct
**Files:** Modify `Shared/AnimationClipConfig.cs` (add below enum)

```csharp
    public struct AnimationClipConfig
    {
        public string Name;
        public ClipLoopMode? LoopMode;
        public float? StartOffset;
        public float? TimelineLength;
        public bool StretchTimeScale;
    }
}
```

**Verify:** `dotnet build Shared/` — passes.

---

#### Task 0.3: Add animation catalog fields to CharacterDefinition
**Files:** Modify `Shared/CharacterDefinition.cs` — add fields BEFORE the `LMB` field (around line 100)

```csharp
        // ── Animation catalog ──
        public string IdleAnim = "idle";
        public string RunAnim = "run";
        public string DashAnim = "dash";
        public string JumpAnim = "jump";
        public string FallAnim = "fall";
        public string HitSmallAnim = "small_hit";
        public string HitMediumAnim = "medium_hit";
        public string HitHardAnim = "hard_hit";
        public float LandStartOffset = 0.49f;
        public AnimationClipConfig[]? ClipOverrides;
```

**Verify:** `dotnet build Shared/` + `dotnet build` at root — passes.

---

#### Task 0.4: Set animation names in MankiData.cs
**Files:** Modify `Shared/Characters/MankiData.cs` — inside `BuildManki()`, after `AutoModelYOffset = true` line

No changes needed for most fields (defaults match Mixamo). Only add:
- ClipOverrides for spell_q_loop (pingpong loop, timeline=3.0)
- ClipOverrides for spell_q (start_offset=0.5, timeline=2.0)

```csharp
            ClipOverrides = new AnimationClipConfig[]
            {
                new() { Name = "spell_q_loop", LoopMode = ClipLoopMode.PingPong, TimelineLength = 3.0f },
                new() { Name = "spell_q", StartOffset = 0.5f, TimelineLength = 2.0f },
            },
```

Note: `HitSmallAnim = "small_hit"`, `HitMediumAnim = "medium_hit"`, `HitHardAnim = "hard_hit"` are defaults — Manki GLB uses these names.

**Verify:** `dotnet build` — passes.

---

#### Task 0.5: Set animation names in BunnyData.cs
**Files:** Modify `Shared/Characters/BunnyData.cs` — inside `BuildBunny()`, after `AutoModelYOffset = true` line

Bunny GLB animation names need verification. For now, match the .tscn:
```csharp
            // Bunny GLB uses "hit_small" etc. (not "small_hit" like Manki)
            HitSmallAnim = "hit_small",
            HitMediumAnim = "hit_medium",
            HitHardAnim = "hit_hard",
```

No ClipOverrides needed (Bunny has no Q charge loop, no custom timelines).

**Verify:** `dotnet build` — passes.

---

### Phase 1: Builder Core (Scripts/ — Godot-side)

#### Task 1.1: Create AnimationTreeBuilder.cs skeleton
**Files:** Create `Scripts/Animation/AnimationTreeBuilder.cs`

```csharp
using Godot;
using SlopArena.Shared;

namespace SlopArena;

public static class AnimationTreeBuilder
{
    public static AnimationNodeStateMachine Build(AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        var sm = new AnimationNodeStateMachine();

        AddStartEnd(sm);
        AddLocomotionStates(sm, animPlayer, charDef);
        AddAirBlendSpace(sm, animPlayer, charDef);
        AddJumpState(sm, animPlayer, charDef);
        AddLandingState(sm, animPlayer, charDef);
        AddHitReactionStates(sm, animPlayer, charDef);
        AddAttackStates(sm, animPlayer, charDef);
        AddChargeLoopStates(sm, animPlayer, charDef);
        AddTransitions(sm, charDef);

        return sm;
    }
    // ... helper methods below
}
```

**Verify:** `dotnet build` — compiles (methods can be stubs returning void).

---

#### Task 1.2: Implement CreateWrappedState helper
**Files:** Modify `Scripts/Animation/AnimationTreeBuilder.cs`

```csharp
    /// <summary>
    /// Creates the standard TimeScale → BlendTree → output wrapper triplet.
    /// Returns the BlendTree root (what gets added as a state machine node).
    /// The TimeScale parameter path will be: parameters/{stateName}/TimeScale/scale
    /// </summary>
    private static AnimationNodeBlendTree CreateWrappedState(
        AnimationPlayer animPlayer, string clipName, CharacterDefinition charDef)
    {
        // 1. Animation node
        var animNode = new AnimationNodeAnimation { Animation = clipName };
        ApplyOverrides(animNode, clipName, charDef);

        // 2. TimeScale node
        var timeScale = new AnimationNodeTimeScale();

        // 3. BlendTree wrapper
        var blendTree = new AnimationNodeBlendTree();
        blendTree.AddNode("Animation", animNode, new Vector2(100, 100));
        blendTree.AddNode("TimeScale", timeScale, new Vector2(400, 100));
        blendTree.AddNode("output", new AnimationNodeOutput(), new Vector2(700, 100));
        blendTree.ConnectNode("output", 0, "TimeScale");
        blendTree.ConnectNode("TimeScale", 0, "Animation");

        return blendTree;
    }

    private static AnimationNodeAnimation CreateSimpleAnim(
        AnimationPlayer animPlayer, string clipName, CharacterDefinition charDef)
    {
        var animNode = new AnimationNodeAnimation { Animation = clipName };
        ApplyOverrides(animNode, clipName, charDef);
        return animNode;
    }

    private static void ApplyOverrides(
        AnimationNodeAnimation node, string clipName, CharacterDefinition def)
    {
        if (def.ClipOverrides == null) return;
        foreach (var cfg in def.ClipOverrides)
        {
            if (cfg.Name != clipName) continue;
            if (cfg.LoopMode.HasValue)
                node.LoopMode = (Animation.LoopModeEnum)(int)cfg.LoopMode.Value;
            if (cfg.StartOffset.HasValue)
            {
                node.UseCustomTimeline = true;
                node.StartOffset = cfg.StartOffset.Value;
            }
            if (cfg.TimelineLength.HasValue)
            {
                node.UseCustomTimeline = true;
                node.TimelineLength = cfg.TimelineLength.Value;
            }
            if (cfg.StretchTimeScale)
                node.StretchTimeScale = true;
            break;
        }
    }
```

**Verify:** `dotnet build` — passes.

---

#### Task 1.3: Implement AddStartEnd, AddLocomotionStates
**Files:** Modify `Scripts/Animation/AnimationTreeBuilder.cs`

```csharp
    private static void AddStartEnd(AnimationNodeStateMachine sm)
    {
        sm.AddNode("Start", new AnimationNodeAnimation(), new Vector2(50, 175));
        sm.AddNode("End", new AnimationNodeAnimation(), new Vector2(1600, 250));
    }

    private static void AddLocomotionStates(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        // Idle (looping)
        var idle = CreateWrappedState(animPlayer, charDef.IdleAnim, charDef);
        var idleAnim = idle.GetNode("Animation") as AnimationNodeAnimation;
        if (idleAnim != null) idleAnim.LoopMode = Animation.LoopModeEnum.Linear;
        sm.AddNode("idle", idle, new Vector2(200, 100));

        // Run (looping)
        var run = CreateWrappedState(animPlayer, charDef.RunAnim, charDef);
        var runAnim = run.GetNode("Animation") as AnimationNodeAnimation;
        if (runAnim != null) runAnim.LoopMode = Animation.LoopModeEnum.Linear;
        sm.AddNode("run", run, new Vector2(400, 100));

        // Dash (looping)
        var dash = CreateWrappedState(animPlayer, charDef.DashAnim, charDef);
        var dashAnim = dash.GetNode("Animation") as AnimationNodeAnimation;
        if (dashAnim != null) dashAnim.LoopMode = Animation.LoopModeEnum.Linear;
        sm.AddNode("dash", dash, new Vector2(600, 400));
    }
```

---

#### Task 1.4: Implement AddAirBlendSpace + AddJumpState + AddLandingState
**Files:** Modify `Scripts/Animation/AnimationTreeBuilder.cs`

```csharp
    private static void AddAirBlendSpace(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        var jumpAnim = CreateSimpleAnim(animPlayer, charDef.JumpAnim, charDef);
        jumpAnim.LoopMode = Animation.LoopModeEnum.None;

        var fallAnim = CreateSimpleAnim(animPlayer, charDef.FallAnim, charDef);
        fallAnim.LoopMode = Animation.LoopModeEnum.Linear;

        var blendAir = new AnimationNodeBlendSpace1D();
        blendAir.BlendMode = AnimationNodeBlendSpace1D.BlendModeEnum.Discrete;
        blendAir.Snap = 0.14f;
        blendAir.AddBlendPoint(jumpAnim, -1f);
        blendAir.AddBlendPoint(fallAnim, 1f);

        sm.AddNode("air", blendAir, new Vector2(200, 250));
    }

    private static void AddJumpState(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        // Standalone TimeScale-wrapped jump for the single-frame takeoff state
        var jump = CreateWrappedState(animPlayer, charDef.JumpAnim, charDef);
        sm.AddNode("jump", jump, new Vector2(200, 175));
    }

    private static void AddLandingState(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        var landAnim = new AnimationNodeAnimation
        {
            Animation = charDef.JumpAnim,
            UseCustomTimeline = true,
            TimelineLength = 1.0f,
            StretchTimeScale = true,
            StartOffset = charDef.LandStartOffset,
            LoopMode = Animation.LoopModeEnum.None,
        };

        var timeScale = new AnimationNodeTimeScale();
        var blendTree = new AnimationNodeBlendTree();
        blendTree.AddNode("Animation", landAnim, new Vector2(100, 100));
        blendTree.AddNode("TimeScale", timeScale, new Vector2(400, 100));
        blendTree.AddNode("output", new AnimationNodeOutput(), new Vector2(700, 100));
        blendTree.ConnectNode("output", 0, "TimeScale");
        blendTree.ConnectNode("TimeScale", 0, "Animation");

        sm.AddNode("land", blendTree, new Vector2(400, 250));
    }
```

**Note on AddBlendPoint API:** `AnimationNodeBlendSpace1D.AddBlendPoint()` takes an
`AnimationRootNode` and a float position. `AnimationNodeAnimation` extends
`AnimationRootNode`. The API signature in Godot 4.6 is:
`void AddBlendPoint(AnimationRootNode node, float pos, int atIndex = -1)`

---

#### Task 1.5: Implement AddHitReactionStates + AddAttackStates + AddChargeLoopStates
**Files:** Modify `Scripts/Animation/AnimationTreeBuilder.cs`

```csharp
    private static void AddHitReactionStates(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        var hits = new[]
        {
            ("hit_small", charDef.HitSmallAnim, new Vector2(600, 600)),
            ("hit_medium", charDef.HitMediumAnim, new Vector2(800, 600)),
            ("hit_hard", charDef.HitHardAnim, new Vector2(1000, 600)),
        };

        foreach (var (stateName, clipName, pos) in hits)
        {
            var node = CreateWrappedState(animPlayer, clipName, charDef);
            // Hit reactions use stretch_time_scale=true, no loop
            var animNode = node.GetNode("Animation") as AnimationNodeAnimation;
            if (animNode != null)
            {
                animNode.UseCustomTimeline = true;
                animNode.TimelineLength = 1.0f;
                animNode.StretchTimeScale = true;
                animNode.LoopMode = Animation.LoopModeEnum.None;
            }
            sm.AddNode(stateName, node, pos);
        }
    }

    private static HashSet<string> _allAttackAnimNames = new();
    private static HashSet<string> _chargeLoopAnimNames = new();

    private static void AddAttackStates(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        _allAttackAnimNames.Clear();
        var abilities = new[] { charDef.LMB, charDef.AirLMB, charDef.RMB, charDef.AirRMB,
                                charDef.Q, charDef.E, charDef.R, charDef.F };
        int col = 0;

        foreach (var ability in abilities)
        {
            if (ability?.AnimationNames == null) continue;
            foreach (var animName in ability.AnimationNames)
            {
                if (string.IsNullOrEmpty(animName)) continue;
                if (!_allAttackAnimNames.Add(animName)) continue; // dedupe

                var node = CreateWrappedState(animPlayer, animName, charDef);
                var pos = new Vector2(800 + col * 200, 100);
                sm.AddNode(animName, node, pos);
                col++;
            }
        }
    }

    private static void AddChargeLoopStates(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        _chargeLoopAnimNames.Clear();

        // RMB charge loop (AerosolFlameSpec)
        if (charDef.RMB is AerosolFlameSpec flame && !string.IsNullOrEmpty(flame.ChargeAnimName))
        {
            _chargeLoopAnimNames.Add(flame.ChargeAnimName);
            var node = CreateWrappedState(animPlayer, flame.ChargeAnimName, charDef);
            var animNode = node.GetNode("Animation") as AnimationNodeAnimation;
            if (animNode != null) animNode.LoopMode = Animation.LoopModeEnum.Linear;
            sm.AddNode(flame.ChargeAnimName, node, new Vector2(1000, 250));
        }

        // Q charge loop (RoundBombSpec)
        if (charDef.Q is RoundBombSpec bomb && !string.IsNullOrEmpty(bomb.LoopAnimName))
        {
            _chargeLoopAnimNames.Add(bomb.LoopAnimName);
            var node = CreateWrappedState(animPlayer, bomb.LoopAnimName, charDef);
            sm.AddNode(bomb.LoopAnimName, node, new Vector2(792, 413));
        }
    }
```

---

#### Task 1.6: Implement AddTransitions
**Files:** Modify `Scripts/Animation/AnimationTreeBuilder.cs`

```csharp
    private static void AddTransitions(
        AnimationNodeStateMachine sm, CharacterDefinition charDef)
    {
        var xfade = new AnimationNodeStateMachineTransition(); // default 0.15s
        var xfadeAtEnd = new AnimationNodeStateMachineTransition { Reset = false };

        // Core movement
        sm.AddTransition("Start", "idle", xfade);
        sm.AddTransition("idle", "run", xfade);
        sm.AddTransition("run", "idle", xfade);

        // Air
        sm.AddTransition("idle", "air", xfade);
        sm.AddTransition("run", "air", xfade);
        sm.AddTransition("air", "land", xfade);

        // Landing
        sm.AddTransition("land", "idle", xfadeAtEnd);
        sm.AddTransition("land", "run", xfade);

        // Dash
        sm.AddTransition("idle", "dash", xfade);
        sm.AddTransition("run", "dash", xfade);
        sm.AddTransition("air", "dash", xfade);
        sm.AddTransition("dash", "End", xfade);

        // Jump
        sm.AddTransition("idle", "jump", xfade);
        sm.AddTransition("run", "jump", xfade);
        sm.AddTransition("land", "jump", xfade);
        sm.AddTransition("jump", "End", xfade);

        // Hit reactions
        sm.AddTransition("hit_small", "End", xfade);
        sm.AddTransition("hit_medium", "End", xfade);
        sm.AddTransition("hit_hard", "End", xfade);

        // Attack states: idle/run/air → each attack, attack → End
        foreach (var animName in _allAttackAnimNames)
        {
            sm.AddTransition("idle", animName, xfade);
            sm.AddTransition("run", animName, xfade);
            sm.AddTransition("air", animName, xfade);
            sm.AddTransition(animName, "End", xfade);
        }

        // Charge loops: idle/run/air → loop, loop → End
        foreach (var loopName in _chargeLoopAnimNames)
        {
            sm.AddTransition("idle", loopName, xfade);
            sm.AddTransition("run", loopName, xfade);
            sm.AddTransition("air", loopName, xfade);
            sm.AddTransition(loopName, "End", xfade);
        }

        // RMB: charge loop → attack
        if (charDef.RMB is AerosolFlameSpec flame &&
            !string.IsNullOrEmpty(flame.ChargeAnimName) &&
            flame.AnimationNames is { Length: > 0 } &&
            !string.IsNullOrEmpty(flame.AnimationNames[0]))
        {
            sm.AddTransition(flame.ChargeAnimName, flame.AnimationNames[0], xfade);
        }

        // Q: charge loop → attack (Manki)
        if (charDef.Q is RoundBombSpec bomb &&
            !string.IsNullOrEmpty(bomb.LoopAnimName) &&
            bomb.AnimationNames is { Length: > 0 } &&
            !string.IsNullOrEmpty(bomb.AnimationNames[0]))
        {
            sm.AddTransition(bomb.LoopAnimName, bomb.AnimationNames[0], xfade);
        }
    }
```

---

### Phase 2: Integration

#### Task 2.1: Wire builder into PlayerController._Ready()
**Files:** Modify `Scripts/Entities/PlayerController.cs` — replace lines 311-315

**Old (lines 311-315):**
```csharp
        if (_playerModel != null)
        {
            var animTree = _playerModel.GetNodeOrNull<AnimationTree>("AnimationTree");
            if (animTree != null)
                _animationController.SetupAnimationTree(animTree);
```

**New:**
```csharp
        if (_playerModel != null)
        {
            var animTree = _playerModel.GetNodeOrNull<AnimationTree>("AnimationTree");
            if (animTree != null)
            {
                animTree.TreeRoot = AnimationTreeBuilder.Build(animPlayer!, _charDef);
                _animationController.SetupAnimationTree(animTree);
            }
```

**Verify:** `dotnet build` — passes.

---

### Phase 3: .tscn Cleanup

#### Task 3.1: Strip sub-resources from manki.tscn
**Files:** Modify `assets/characters/manki/manki.tscn`

**Remove:** All `[sub_resource ...]` blocks (lines 14-397 in current file).
These include all `AnimationNode*`, `AnimationNodeBlendTree`, `AnimationNodeBlendSpace1D`,
`AnimationNodeStateMachineTransition`, and `AnimationNodeStateMachine` sub-resources.

**Remove from AnimationTree node (lines 401-425):**
- Line 403: `tree_root = SubResource("sm_main")`
- Lines 405-425: All `parameters/*/TimeScale/scale = 1.0` entries

**Keep:**
- `[ext_resource ...]` block for scripts (FSM states)
- `[node name="Manki"]` root
- `[node name="AnimationTree"]` with `root_node` and `anim_player` (keep the node)
- `[node name="FSM"]` and all state children
- `[node name="manki"]` model instance and skeleton

**The AnimationTree node after cleanup looks like:**
```gdscript
[node name="AnimationTree" type="AnimationTree" parent="." unique_id=1656529102]
root_node = NodePath("../manki")
anim_player = NodePath("../manki/AnimationPlayer")
parameters/air/blend_position = 0.0
```

The `parameters/air/blend_position = 0.0` is preserved because it's the default value
for the BlendSpace1D parameter — the builder doesn't set default values, but keeping
this line causes no harm.

Actually: **remove all `parameters/` lines** from the AnimationTree node. The builder
creates all parameters dynamically. Leftover parameter lines in .tscn could conflict.

**Final AnimationTree node:**
```gdscript
[node name="AnimationTree" type="AnimationTree" parent="." unique_id=1656529102]
root_node = NodePath("../manki")
anim_player = NodePath("../manki/AnimationPlayer")
```

**Verify:** Open manki.tscn in Godot Editor — no parse errors. Run project — animations play.

---

#### Task 3.2: Strip sub-resources from bunny.tscn
**Files:** Modify `assets/characters/bunny/bunny.tscn`

Same operation as Task 3.1: remove all `[sub_resource ...]` blocks, remove
`tree_root = SubResource("sm_bunny")`, remove all `parameters/*/TimeScale/scale` lines.

Final AnimationTree node:
```gdscript
[node name="AnimationTree" type="AnimationTree" parent="." unique_id=1215559337]
root_node = NodePath("../bunny")
anim_player = NodePath("../bunny/AnimationPlayer")
```

**Verify:** Open bunny.tscn in Godot Editor — no parse errors. Run project — animations play.

---

### Phase 4: Verification

#### Task 4.1: Build and run
```bash
dotnet build
```

Run the project in Godot Editor. Verify:
- Manki idle/run/dash animations play
- Manki air blend (jump/fall) works — AirState drives the blend parameter
- Manki LMB combo (spell_lmb_1 → spell_lmb_2 → spell_lmb_3) chains correctly
- Manki RMB flamethrower (spell_rmb_loop → spell_rmb_attack) works
- Manki Q round bomb (spell_q_loop → spell_q) works
- Hit reactions (hit_small/hit_medium/hit_hard) play on damage
- Landing animation plays after fall
- Bunny has same behavior
- TimeScales are applied correctly (check debug prints from ApplyAnimationTimeScales)

#### Task 4.2: NPC test
Run TrainingMatch. Verify NPC Manki animations work (NPC uses the same builder path).

---

## Pitfalls

- **`AnimationNodeBlendSpace1D.AddBlendPoint()` signature**: Takes `AnimationRootNode` + `float` position. `AnimationNodeAnimation` inherits from `AnimationRootNode`. No TimeScale wrapper inside BlendSpace (BlendSpace1D doesn't support it — this is expected and matches current .tscn).

- **Duplicate animation names across abilities**: Bunny's RMB reuses `spell_lmb_1`. The `_allAttackAnimNames` HashSet handles deduplication — only one StateMachine state is created per unique animation name.

- **State name vs clip name**: The builder uses `AnimationNames` from AbilitySpec as BOTH the StateMachine state name AND the animation clip name. This matches the current .tscn where `states/spell_lmb_1/node = SubResource("bt_melee")` with `animation = &"spell_lmb_1"`. The FSM calls `Travel("spell_lmb_1")` which matches the state name.

- **`.tscn` `tree_root` removal**: After stripping `tree_root = SubResource("sm_main")`, the AnimationTree loads with `TreeRoot = null`. The builder assigns it in `_Ready()` before the deferred FSM init runs. If the builder somehow fails, the AnimationTree has no root and Travel() calls crash. Add a null check in the builder integration.

- **Landing animation uses JumpAnim clip**: `AddLandingState` creates a new `AnimationNodeAnimation` with `Animation = charDef.JumpAnim` and custom timeline settings. It does NOT use `CreateWrappedState` because the landing config is different from the jump state. But the TimeScale parameter path is still `parameters/land/TimeScale/scale` (from the BlendTree wrapper), so `ApplyAnimationTimeScales` won't break — it tries to set TimeScale on landing but silently catches the "parameter doesn't exist" case since landing uses custom timeline, not DurationTicks.

- **Bunny has no `spell_air_lmb` state in current .tscn**: Verify — the bunny.tscn transitions don't include `spell_air_lmb`. Check if BunnyData.cs has AirLMB.AnimationNames. If not, the builder won't create the state. If Bunny SHOULD have AirLMB, add it to BunnyData.cs.

- **`xfade_01` vs `xfade_015` consolidation**: The current .tscn uses both 0.1s and 0.15s crossfade. The builder consolidates to 0.15s everywhere. This is a deliberate simplification — the existing `AnimationNodeStateMachineTransition` default is 0.15s.

- **Godot MCP not needed**: All .tscn edits in this plan are structural (removing blocks, not modifying serialized node trees). The FSM tree + model skeleton are preserved as-is. No Godot Editor interaction required for the .tscn cleanup — direct text editing is safe because we're only removing sub-resource blocks and a few `tree_root`/`parameters/` lines. The remaining content (node tree, skeleton bone data) is untouched.

---

## Commit Plan

```
Phase 0: feat: add animation catalog fields to CharacterDefinition + AnimationClipConfig
Phase 1: feat: add AnimationTreeBuilder — generates StateMachine from CharacterDefinition
Phase 2: feat: wire AnimationTreeBuilder into PlayerController.Ready()
Phase 3: chore: strip AnimationTree sub-resources from character .tscn files
Phase 4: test: verify all animations play correctly for Manki + Bunny
```
