---
name: sloparena-combat-engine
description: "SlopArena sim-authoritative combat engine: ServerAbility class system (LmbCombo, MankiAerosolFlame, MankiRoundBomb), HitboxEvent, combo chaining, ActiveSlot pipeline, SpellResolver collision, targeted projectile system with explosion-on-impact, server-side warp + cone check, lunge/per-stage movement. Client renders via PlayerRenderer.ApplyServerState (no client FSM)."
version: 3.0.0
author: OMP Agent
license: MIT
platforms: [linux]
metadata:
  omp:
    tags: [sloparena, combat, hitbox, netcode, simulation, projectiles, explosions, fsm, abilities, server-abilities]
    related_skills: [sloparena-netcode, sloparena-character-workflow]
---

# SlopArena Combat Engine

Sim-authoritative combat system. All attack timing, hitbox spawning, and state transitions are controlled by the server simulation via polymorphic `ServerAbility` subclasses. The client only renders — `PlayerRenderer.ApplyServerState(state)` plays clips from the sim state; there are no client Ability or FSM classes.

All abilities use the **ServerAbility system** — the old data-driven `AbilityExecutor` path has been removed. Simple attacks (LMB, E, R, F) and complex abilities (RoundBomb, AerosolFlame) all go through the same `OnStart/Tick/OnEnd` lifecycle.

## Ability Data Model

Abilities are defined as **`AbilitySpec` instances** in `Shared/` — instantiated inline in character data files. No separate spec structs per ability type — the base `AbilitySpec` class with its `Params` dictionary IS the data mechanism.

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `string` | Display name |
| `CooldownTicks` | `ushort` | Cooldown in ticks (0 = none) |
| `Stages` | `AttackStage[]` | Hitbox timing, lunge, chain windows |
| `ChargedStages` | `AttackStage[]?` | Hold-to-charge variant |
| `ChargeHoldTicks` | `ushort` | Ticks to hold for charged variant |
| `AnimationNames` | `string[]` | Animations indexed by AnimIndex |
| `Params` | `Dictionary<string, float>` | Tunable named float parameters |

**Design principle**: character data instantiates the spec class directly. The `Params` dictionary holds ability-specific values (damage, timings, thresholds) that are read via `ServerAbility.GetParam()`.

```csharp
// MankiData.cs — single source of truth:
LMB = new AbilitySpec {
    Name = "Monkey Combo",
    CooldownTicks = 0,
    Stages = [...],
    AnimationNames = new[] { "monkey_lmb_1", "monkey_lmb_2", "monkey_lmb_3" },
    Params = new() { ["lunge_duration"] = 10f },
};
```

`AbilityTypeId` is **deprecated** — slot-based mapping via `AbilityFactory.CreateServer(CharacterClass, byte slot, bool airborne)` makes global type IDs unnecessary. The field still exists on `AbilitySpec` but is no longer used for dispatch.

## AbilityFactory

### Server-Side Factory

Dispatch is now by `(CharacterClass, byte slot, bool airborne)` — no global type ID:

```csharp
// AbilityFactory.CreateServer(CharacterClass, byte slot, bool airborne)
return characterClass switch
{
    CharacterClass.Manki => CreateMankiAbility(slot, airborne),
    _ => null,
};

// Slot: 0=LMB, 1=RMB, 2=Q, 3=E, 4=R, 5=F
private static ServerAbility? CreateMankiAbility(byte slot, bool airborne) => (slot, airborne) switch
{
    (0, false) => new MankiLmbCombo(),     // LMB
    (1, false) => new MankiAerosolFlame(), // RMB
    (2, _)     => new MankiRoundBomb(),    // Q (same ground/air)
    _          => null,                    // No ServerAbility = data-driven fallback
};
```

`InitFromSpec` populates metadata after construction:

```csharp
AbilityFactory.InitFromSpec(ability, spec, slot);
// Sets: Slot, Cooldown (from spec.CooldownTicks), AnimationNames (from spec.AnimationNames)
```

## ServerAbility Class System

Server-side abilities are pure C# classes in `Shared/Abilities/`, controlled by `AbilityFactory.CreateServer`. Each ability is a fresh instance per activation.

### File Layout

```
Shared/Abilities/
│   ServerAbility.cs       — abstract base (OnStart/Tick/OnEnd lifecycle)
│   AbilityFactory.cs      — (CharacterClass, slot, airborne) dispatch
├── LmbCombo.cs / AirLmbCombo.cs — LMB: 3-hit melee chain with forward lunge (shared across characters)
├── MankiRoundBomb.cs      — Q: parabolic arc projectile, release-to-throw
└── MankiAerosolFlame.cs   — RMB: hold-to-charge flamethrower cone
```

### ServerAbility Base Class

```csharp
public abstract class ServerAbility
{
    // ── Lifecycle ──
    public abstract void OnStart(ref CharacterState s, CharacterDefinition def);
    public abstract void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def);
    public virtual void OnEnd(ref CharacterState s) { }

    // ── Metadata (set by factory) ──
    public byte Slot { get; set; }
    public ushort Cooldown { get; set; }

    // ── Animation ──
    public byte AnimIndex { get; protected set; }          // into AnimationNames[]
    public string[] AnimationNames { get; set; }

    // ── Context ──
    public ISpellResolver Resolver { get; set; }            // set by simulation before first Tick

    // ── Helpers ──
    protected void SpawnHitbox(ref CharacterState s, HitboxEvent evt);
    protected void SetVelocity(ref CharacterState s, float vx, float vy, float vz);
    protected void SetVelocityInFacing(ref CharacterState s, float forwardSpeed, float vertical = 0f);
    protected void EndAbility(ref CharacterState s);        // calls OnEnd, sets Idle, clears combo
    protected float GetParam(CharacterDefinition def, string key, float fallback = 0f);
}
```

**Lifecycle**:
1. `OnStart` → called once when ability activates. Sets `s.State = Attacking`, `AnimLockTicks`, `AnimIndex`, applies lunge.
2. `Tick` → called every sim tick while active. Spawns hitboxes at trigger ticks, handles chain/buffer input, applies per-tick movement, ends when duration expires.
3. `OnEnd` → called on **natural completion only** (`EndAbility` called). NOT called on interruption (hitstun, death) — velocity persists for momentum-granting abilities.
4. **Interruption**: Simulation drops the instance without calling OnEnd. The `_activeAbilities` pool entry is removed.

### Integration in ServerSimulation

`ServerSimulation` manages activation and ticking:

```
pre-sim:  Activate server abilities from inputs
          → calls ability.OnStart + applies cooldown
post-sim: TickAbilities()
          → calls ability.Tick() for each active ability
          → if ability ended (EndAbility called), apply cooldown
          → copies AnimIndex to CharacterState.AnimIndex each tick
```

The simulation sets `CharacterState.IsServerAbility = true` when a ServerAbility is active.

### AnimIndex

`ServerAbility.AnimIndex` (byte, synced in `CharacterStatePacket`) tells the client which animation to play. The client reads `AnimIndex` as an index into `AbilitySpec.AnimationNames[]`:

```csharp
s.AnimIndex = _stage;  // LmbCombo advances AnimIndex per combo stage
```

No string matching — the client just does `animName = spec.AnimationNames[state.AnimIndex]`.

### Concrete Classes

**LmbCombo** (slot 0, ground LMB — shared by all characters; airborne variant `AirLmbCombo`):
- 3-hit melee combo chain reading from `AttackStage[]`
- Tracks `_stage` and `_stageTicks` internally
- Applies lunge velocity for first `lunge_duration` ticks of each stage (from `Params["lunge_duration"]`)
- Spawns hitboxes via `SpawnHitbox()` at each stage's `HitboxEvent.TriggerTick`
- Chains to next stage when `input.ActiveSlot == slot+1` and within chain window
- Calls `EndAbility` when `_stageTicks >= stage.DurationTicks`

```csharp
// Key tick logic:
foreach (var evt in stage.HitboxEvents)
    if (evt.TriggerTick == _stageTicks)
        SpawnHitbox(ref s, evt);

if (input.ActiveSlot == (Slot + 1)
    && _stageTicks >= stage.DurationTicks - stage.ChainWindowTicks
    && _stage < stages.Length - 1)
{
    input.ActiveSlot = 0;    // consume buffered input
    _stage++; _stageTicks = 0;
    s.AnimIndex = _stage;
    s.AnimLockTicks = stages[_stage].DurationTicks;
    if (stages[_stage].LungeForce > 0f)
        SetVelocityInFacing(ref s, stages[_stage].LungeForce);
}
```

**MankiRoundBomb** (slot 2, Q):
- Spawns a parabolic-arc projectile via `Resolver.Spawn()` at `throw_trigger_tick`
- Reads distance from `s.AimTargetDistance` (set from client InputState each tick)
- Computes ballistic launch velocity via `CombatMath.ComputeProjectileLaunch`
- Parameters: `throw_duration`, `throw_trigger_tick`, `max_range`, `launch_angle`, `gravity`, `hitbox_radius`, `damage`, `knockback_force`, `knockback_upward`, `stun_ticks`, `max_flight_ticks`, `explosion_*` params
- Sets `s.IsAiming = true` on start, clears on projectile spawn
- Ends when `s.AttackElapsedTicks >= s.AnimLockTicks`

**MankiAerosolFlame** (slot 1, RMB):
- Hold-to-charge flamethrower cone
- Checks `s.ChargeTicks >= charge_threshold` to select charged vs normal variant
- Charged variant uses different params (duration, trigger tick, off_z, radius, damage, knockback)
- Spawns capsule-shaped hitbox at trigger tick in front of character
- Uses different `AnimIndex` for charged (1) vs normal (0)
- Parameters all read via `GetParam(def, "normal_damage", 14f)` / `GetParam(def, "charged_damage", 28f)` pattern

### Adding a New ServerAbility

1. Create a file in `Shared/Abilities/` extending `ServerAbility`
2. Register in `AbilityFactory.CreateServer()` — add a case to the appropriate character's private method
3. Add any params via `Params["key"] = value` on the `AbilitySpec` in character data
4. No changes to `ServerSimulation` — the `ServerAbility` activation path is generic

## Core Data Model

### HitboxEvent (AttackData.cs)
```csharp
public struct HitboxEvent {
    public ushort TriggerTick;    // Frame from attack start when hitbox spawns
    public ushort DurationTicks;  // Active frames of the hitbox
    public HitboxShape Shape;     // Sphere=0, Capsule=1
    public float Radius;
    public float OffX, OffY, OffZ;
    public float EndOffX, EndOffY, EndOffZ;
    public float Damage;
    public float KnockbackForce;
    public float KnockbackUpward;
    public ushort StunTicks;
    public bool Interruptible;    // false = SuperArmor (persists even if hit)
}
```

### InputState
```csharp
public struct InputState {
    public byte ActiveSlot;    // 1-6 = slot press, 0 = none
    public ushort Buttons;     // Jump, Dash, Crouch flags
    public short FacingYaw;    // deg x100, movement-facing
    public short AimYaw;       // deg x100, combat-facing for projectiles
    public ushort AimDistance; // cm-scaled (0-6500), target distance for throw abilities
    public bool Crouch;
    public bool IsAiming;
}
```
Size: 16 bytes. AimDistance flows: client RoundBomb (or any hold-to-aim Ability) → BuildInputState → SimulateTick → CharacterState.AimTargetDistance.

### FacingYaw vs AimYaw
- **FacingYaw**: movement direction, set by Atan2(VX,VZ) in ProcessNormalMovement. Client sends it but sim overwrites.
- **AimYaw**: combat direction, used for projectile velocity when ProjectileConfig is set. Stays as sent by client.
- **PITFALL**: Projectile velocity MUST use state.AimYaw, NOT state.FacingYaw. FacingYaw tracks movement, not aim.

Rotation formula (Z-axis-centered for Atan2 convention):
```csharp
hx = PX + (OffX*cos + OffZ*sin);
hz = PZ + (-OffX*sin + OffZ*cos);
```

## Attack Flow (server)
1. Client `InputController.Poll()` → `BuildInputState()` → `input.ActiveSlot = X` (edge-detected slot press)
2. `input.ActiveSlot = X` + AimYaw/AimDistance from the client aim system
3. ServerSimulation.Tick() → `PreSimulate(ref input)`:
   - If `input.ActiveSlot != 0` and no active ServerAbility for this slot:
     - `AbilityFactory.CreateServer(characterClass, slot, airborne)` → creates ability
     - `ServerSimulation.StartAbility(entityId, ability, ref state, def)` → calls `OnStart()`, stores instance
   - If an active ServerAbility exists: `ability.Tick(ref state, ref input, def)` handles hitboxes, chains, EndAbility
4. Hitboxes spawned from within `ability.Tick()` via `SpawnHitbox()` helper or `Resolver.Spawn()`
5. Client `PlayerRenderer.UpdateAnimationState()`: on a change of `(AttackSlot, ComboStage)`, plays `spec.AnimationNames[state.AnimIndex]` via Animancer, speed-modulated to `DurationTicks`

## Client Rendering

The client only renders — there are no client Ability or FSM classes. `Runtime/` contains `ProjectileVFXManager`, `AimHandler`, `AimIndicator`, `TargetIndicator`, `CombatFeedback` — nothing ability/FSM-shaped. `PlayerRenderer.ApplyServerState(state)` plays the clip for `(AttackSlot, ComboStage, AnimIndex)` via Animancer with `frameCount / DurationTicks` speed; projectile visuals are prefab-driven via `ProjectileVFXManager`. Hit detection, projectile trajectories, and explosions stay server-side.

## Projectile System

Projectiles are server-authoritative: a `Hitbox` spawned with velocity (`VX/VY/VZ`), optional `Gravity`, and an optional `Explosion` config. `SpellResolver` owns the physics — no client-side trajectory code.

### Lifecycle

```
ServerSimulation.Tick():
1. Ability (e.g. MankiRoundBomb, NilusVoidRift) computes launch velocity via
   CombatMath.ComputeProjectileLaunch(targetDistance, angle, gravity, heightOffset,
   out speed, out hSpeed, out vSpeed) and calls Resolver.Spawn(hitbox with VX/VY/VZ)
2. SpellResolver.Tick(): moves the hitbox (X += VX * TickDt), applies gravity (VY -= Gravity * TickDt)
3. Entity collision: sphere vs hurtboxes; one hit per hitbox (zones pulse via RehitIntervalTicks)
4. Ground collision: SpellResolver.CheckGroundCollision(arena) samples the heightmap at the
   projectile's XZ — on contact, queues the explosion at ground level
5. Deactivation (hit, expire, or ground contact): if the hitbox carries a ProjectileExplosion,
   it is queued in _pendingExplosions → ServerSimulation.ProcessProjectileExplosions() drains
   the queue and spawns the explosion hitbox (sphere, Radius/Damage/Knockback/Stun)
```

### Data

```csharp
// Hitbox projectile fields (Shared/Hitbox.cs):
public float VX, VY, VZ;              // velocity — nonzero = projectile
public float Gravity;                 // per-tick gravity (use sim gravity 35 for consistency)
public ProjectileExplosion? Explosion; // spawned on deactivation (hit/expire/ground)
public bool CanHitOwner;              // mine-jump style self-hits
public ushort RehitIntervalTicks;     // 0 = one-hit-then-die; > 0 = lingering zone that pulses
public bool IgnoresEntities;          // no body scan — ages/expires, still explodes

// ProjectileConfig on the ability spec (Shared/AttackData.cs):
public float Gravity;                 // m/s²
public float MaxRange;                // clamp for AimTargetDistance
public KnockbackData Knockback;       // resolved at spawn time
public ushort StunTicks;
public ushort MaxLifetimeTicks;       // 600 = 10s
public ProjectileExplosion? Explosion;
```

### Explosion System

Explosion config is **baked at spawn time** — buff bonuses are applied by the ability before `Resolver.Spawn` if desired (NilusVoidRift does; MankiBazooka/MankiRoundBomb buff only the direct projectile hit).

```csharp
public struct ProjectileExplosion {
    public float Radius;
    public float Damage;
    public KnockbackData Knockback;      // profile resolved at explosion spawn
    public ushort StunTicks;
    public ushort DurationTicks;
    public bool CanHitOwner;
    public ushort RehitIntervalTicks;    // > 0 = lingering explosion zone
}
```

`ServerSimulation.ProcessProjectileExplosions()`: calls `CheckGroundCollision(_arena)`, then for each drained `(x, y, z, explosion, ownerId)` spawns a sphere hitbox with the explosion's radius/damage/knockback/stun. Explosions are ordinary hitboxes — same collision rules as everything else. Multiple explosions in the same tick are queued and resolved together.

### Networking

No projectile packets exist — projectiles are purely server-side. Clients render impact/explosion visuals via `ProjectileVFXManager` driven by server ability events (`SpecialEffectKeys`).

## Lunge/Stages Movement

### Lunge Application
Lunge is applied in `ServerAbility.OnStart()` / `Tick()` via `SetVelocityInFacing()`:

```csharp
// LmbCombo.OnStart:
if (stage.LungeForce > 0f)
    SetVelocityInFacing(ref s, stage.LungeForce);

// LmbCombo.Tick — reapply during lunge window:
if (_stageTicks <= _lungeDuration && stage.LungeForce > 0f)
    SetVelocityInFacing(ref s, stage.LungeForce);
```

### Interruptibility
- Each stage's first hitbox has `Interruptible` flag
- If `Interruptible = false`, lunge velocity is preserved even when hit (SuperArmor frame)
- When `Interruptible = true`, hitstun zeroes velocity and applies knockback

### Movement Constraints Throughout Attack
- **During lunge**: Velocity is set on frame 0 of the stage, then the character coasts at that velocity unless interrupted or the next stage starts
- **Between stages**: coasting continues until the next stage applies a new velocity. No deceleration between chain stages — the lunge velocity from stage 1 persists until stage 2's LungeForce overwrites it.
- **Air combat**: AirLMB/AirRMB use `GravityMultiplier` during attack (set in `AbilitySpec`). Default = 0.2f (reduced gravity during air attacks). On interrupt → full gravity resumes.
- **Charging**: hold-to-charge abilities (MankiAerosolFlame, MankiRoundBomb) keep `IsAiming`/`ChargeTicks` on the server while held; movement is constrained while `State == Attacking`. On release, the attack fires with its stage velocity.

### Stage Properties (AttackStage)
```csharp
public struct AttackStage {
    public ushort DurationTicks;        // ticks this stage lasts
    public ushort ChainWindowTicks;     // ticks before chain input is accepted
    public HitboxEvent[] HitboxEvents;  // all hitboxes in this stage
    public float LungeForce;            // forward velocity on start
    public float MoveX, MoveY, MoveZ;  // per-tick velocity (world space)
    public bool CanTurn;               // can character rotate during this stage
}
```

## Warp Movement

Warp (auto-dash toward target before attacking) is now fully server-side via `Simulation.ProcessWarp()`. The client-side `AttackWarping.cs` has been deleted.

### Warp Parameters
Warp is controlled by `CharacterState` fields, not a dedicated state:
```csharp
// CharacterState warp fields:
public float WarpTargetX, WarpTargetY, WarpTargetZ;
public float WarpSpeed;  // 0 = no warp active
public float WarpAttackRange;  // stop warping when this close
```

The `ActionState.Warping` state was removed — warp is now a velocity override that applies during any state. When `WarpSpeed > 0`, the simulation interpolates position toward the warp target each tick.

### Usage in ServerAbility
```csharp
// In OnStart:
s.WarpTargetX = s.PX + (s.FacingX * 5f);
s.WarpTargetY = s.PY;
s.WarpTargetZ = s.PZ + (s.FacingZ * 5f);
s.WarpSpeed = 0.3f;  // 30% per tick
```

The sim handles interpolation and collision. The ability just sets the target and speed.

## Animation & Client Rendering

Client clip selection is pure C# in `PlayerRenderer.UpdateAnimationState()` — no state machine classes:

- **Non-combat:** idle ↔ run (crossfade by speed threshold), jump, fall; double jump overrides fall with the jump clip on upward impulse
- **Attacking:** lookup by `(AttackSlot, ComboStage)` → `AnimationNames[]` → clip, speed = `frameCount / DurationTicks`
- **Dashing / hitstun:** dash clip (0s crossfade); `hit_small`/`hit_medium`/`hit_hard` by `HitstunLevel`

`AnimIndex` is synced in `CharacterStatePacket` — the client plays `spec.AnimationNames[state.AnimIndex]`. No string matching.

### AnimLockTicks (server)
```csharp
s.AnimLockTicks = stage.DurationTicks;  // sets on attack start
```
During attack, movement input is ignored. On tickdown, when `AnimLockTicks` reaches 0, the character can move again if state is Idle.

## CharacterStatePacket

The `CharacterStatePacket` is **48 bytes** (`src/Shared/CharacterStatePacket.cs`). Key fields:

```csharp
public struct CharacterStatePacket
{
    public uint TickNumber;
    public float PositionX, PositionY, PositionZ;
    public float VelocityX, VelocityY, VelocityZ;
    public byte CurrentActionState;
    public bool IsGrounded;
    public ushort StateDurationFrames;
    public byte AttackSlot;
    public byte ComboStage;
    public byte AnimIndex;        // ← animation index into spec.AnimationNames[]
    public float FacingYaw;
    public MatchState MatchState;
    public ushort BuffRemainingTicks;
    public byte BuffActiveFlags;
    public byte HitstunLevel;
    public float AimPitch;
}
```

`AnimIndex` replaces string-based animation matching on the client. The former `BufferedChain`, `HeavyHoldTicks`, `HeavyCharged` fields were removed from `CharacterState`.

## Hitbox Visual Feedback & Impact Effects

### Hit markers
- Client-side hitmarker: `HitConfirm` sound played on confirmed hit (from server in `DamagePacket`)
- `DamageNumbers` from `Game.DamageNumberEffect` instantiated on hit confirmation
- `Worldspace-text` for damage/poise break (white = normal, yellow = crit, red = lethal)

### Screen shake
- `Camera.Shake(amplitude, duration)` called from `CameraShakeComponent.ProcessDamageShake()` when `DamagePacket` received
- Shake amplitude scales with damage (`min(shakeIntensity, maxShake)`)
- Duration capped at `maxShakeDuration` (0.3s)

### Decals
- `DecalManager.SpawnBulletHole(position, normal)` — applied to terrain/wall hits
- `BloodSplat` VFX on entity hits (pooled decal instances)

### Explosion FX
Each `ExplosionConfig` carries per-ability visual data:
```csharp
public struct ExplosionConfig {
    public float Radius;
    public float Damage;      
    public float Impulse;     
    public string SoundPath;  // ← Sound effect for this explosion type
    public Color Color;       // ← Tint for explosion particles
}
```
`SoundPath` is now data-driven (previously hardcoded in explosion handler).

## Implementation History

### April 2026
- Initial implementation: HitboxSystem and AttackData
  
### May 2026
- EffectSystem refactor: removed string-based effect lookup → `AbilityRegistry.Execute(key, combat)`
- SimpleAttack consolidation: collapsed 5 near-identical instant abilities into one class
- HitboxSpawnData consolidation: merged projectile/explosion configs into one struct
- Removed SpellResolver.GetVisualForHitbox (unused with data-driven prefab lookup); consolidated to five (5 total) exported fields

### June 2026
- Introduced `ServerAbility` class system with `LmbCombo`, `MankiRoundBomb`, `MankiAerosolFlame`
- Removed old `AbilityExecutor`, `GenericMelee`, `MeleeCombo`, `BackflipRoll`, `RoundBombSpec`, `AerosolFlameSpec`
- `AbilityFactory.CreateServer()` now dispatches by `(CharacterClass, byte slot, bool airborne)`
- Removed client-side `AttackWarping.cs` — warp is server-side via `Simulation.ProcessWarp()`
- `AnimIndex`: byte-indexed animation selection replacing string matching
- `AbilitySpec.AbilityTypeId` deprecated; `Params` dictionary is the data mechanism
- `CharacterStatePacket` now 48 bytes with `AnimIndex` field
- Warp is velocity override (`WarpSpeed > 0`) not a separate state, gated by the facing cone check
- Projectile/explosion system: `ProjectileConfig` + `ProjectileExplosion` on ability specs, resolved through `SpellResolver` pending-explosion queue
- Comprehensive documentation in `docs/systems/ability-architecture.md`

## Related Skills

- `sloparena-netcode` skill
