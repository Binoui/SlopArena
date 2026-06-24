---
name: sloparena-combat-engine
description: "SlopArena sim-authoritative combat engine: ServerAbility class system (MankiLmbCombo, MankiAerosolFlame, MankiRoundBomb), HitboxEvent, combo chaining, ActiveSlot pipeline, SpellResolver collision, targeted projectile system with explosion-on-impact, server-side warp, lunge/per-stage movement. Client Ability classes drive FSM states for animation + movement constraints."
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

Sim-authoritative combat system. All attack timing, hitbox spawning, and state transitions are controlled by the server simulation via polymorphic `ServerAbility` subclasses. The client renders FSM animations in response to ActionState changes, with Ability classes driving FSM states for movement constraints and charge animations.

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

### Client-Side Factory

Client abilities are created by `AbilityTypeId` for the old dispatch pattern (still alive for backward compat):

```csharp
public static Ability Create(int slotIndex, bool airborne, CharacterDefinition def)
{
    var spec = def.GetSlotAbility(slotIndex, airborne);
    return spec.AbilityTypeId switch
    {
        3 => new AerosolFlame { Data = spec },
        2 => new RoundBomb { Data = spec },
        _ => new SimpleAttack { Data = spec, SlotNumber = (byte)(slotIndex + 1) },
    };
}
```

The client reads ability data from `Data.Params.TryGetValue()` instead of casting to specialized specs.

## ServerAbility Class System

Server-side abilities are pure C# classes in `Shared/Abilities/`, controlled by `AbilityFactory.CreateServer`. Each ability is a fresh instance per activation.

### File Layout

```
Shared/Abilities/
│   ServerAbility.cs       — abstract base (OnStart/Tick/OnEnd lifecycle)
│   AbilityFactory.cs      — (CharacterClass, slot, airborne) dispatch
├── MankiLmbCombo.cs       — LMB: 3-hit melee chain with forward lunge
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
s.AnimIndex = _stage;  // MankiLmbCombo advances AnimIndex per combo stage
```

No string matching — the client just does `animName = spec.AnimationNames[state.AnimIndex]`.

### Concrete Classes

**MankiLmbCombo** (slot 0, ground LMB):
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
1. Client _UnhandledInput → `_pendingSlotPress = X` or activates an Ability class
2. BuildInputState → `input.ActiveSlot = X` + ability-overridden AimYaw/AimDistance
3. ServerSimulation.Tick() → `PreSimulate(ref input)`:
   - If `input.ActiveSlot != 0` and no active ServerAbility for this slot:
     - `AbilityFactory.CreateServer(characterClass, slot, airborne)` → creates ability
     - `ServerSimulation.StartAbility(entityId, ability, ref state, def)` → calls `OnStart()`, stores instance
   - If an active ServerAbility exists: `ability.Tick(ref state, ref input, def)` handles hitboxes, chains, EndAbility
4. Hitboxes spawned from within `ability.Tick()` via `SpawnHitbox()` helper or `Resolver.Spawn()`
5. Client _PhysicsProcess: State==Attacking && !fsm.IsInState("attack") → TransitionTo("attack") with animName from spec.AnimationNames[state.AnimIndex]

## Attack Flow (client — Ability classes)

### Architecture
Each ability slot is a standalone class (`Ability`) that owns its lifecycle instead of living in FSM states:

```
Scripts/Abilities/
│   Ability.cs              — abstract base (Name, SlotNumber, Data: AbilitySpec, OnActivate, Tick, OnDeactivate, OnInput, TriggerEffects)
├── AbilityInputState.cs   — Tick() return (AimYaw?, AimDistance?, ActiveSlot?)
├── AbilityFactory.cs       — type-switch by AbilityTypeId (3→AerosolFlame, 2→RoundBomb, else→SimpleAttack)
├── SimpleAttack.cs         — generic instant ability (LMB, AirLMB, AirRMB, E, R, F — all use this)
├── AerosolFlame.cs         — RMB: ground cone indicator, charge-on-hold, release-to-fire
└── RoundBomb.cs            — Q: parabolic arc + target circle, hold-to-aim, release-to-throw
```

**Instant abilities all use `SimpleAttack`.** MonkeyCombo, AirLMB, AirRMB, DynamiteJump, DiveBomb, BigBoom were identical (each ~20 lines: `_fired` + `TriggerEffects` + `ActiveSlot`) — collapsed into `SimpleAttack` which reads `Data.Name` for display and accepts `SlotNumber` as a property. Only `AerosolFlame` and `RoundBomb` have specialized logic. When adding a new instant ability, just register it in `AbilityFactory` returning `SimpleAttack` — don't create a new class.

**Data reads:** Client abilities read from `Data.Params.TryGetValue("key", out value)` instead of casting to specialized spec types (`RoundBombSpec`, `AerosolFlameSpec`) — those classes have been deleted.

### Lifecycle
1. `PlayerController._UnhandledInput` detects key press → creates ability class → calls `ActivateAbility(ability)`
2. `ActivateAbility`: calls `_activeAbility?.OnDeactivate()`, stores new ability, calls `ability.OnActivate(player)`
3. **In `OnActivate`**, charge/aim abilities may call `fsm.TransitionTo("aimed_charge")` to block movement and play a loop animation. The FSM state (`AimedChargeState`) provides `CanMove = false` and the charge animation — the ability class handles the actual timing and release detection.
4. `PlayerController._Process` calls `_activeAbility?.Tick(player, delta)` every frame
5. Tick returns `AbilityInputState?`:
   - **`null`** = ability is still active (charging, aiming, waiting). **Do NOT deactivate.** Keep the ability around for next frame's Tick().
   - **`{AimYaw, AimDistance}` (no ActiveSlot)** = ability is streaming aim data. Stored for BuildInputState. Ability stays active.
   - **`{ActiveSlot = X}`** = ability fired. `TriggerEffects(player)` called **first**, then `_pendingSlotPress` set, then `DeactivateAbility()`.
5. Deactivation only happens via one of:
   - Ability Tick returns with `ActiveSlot.HasValue == true` (intentional fire)
   - Ability Tick returns with `ActiveSlot.HasValue == false` but the ability explicitly calls it done — not currently supported; an ability that streams aim data without ever setting ActiveSlot stays active indefinitely
   - External interrupt (damage/knockback): `DeactivateAbility()` on the PlayerController side
6. `PlayerController._UnhandledInput` forwards events to `_activeAbility.OnInput(@event)` (mouse motion, etc.)
7. On external deactivation: `DeactivateAbility()` cleans up indicators

### TriggerEffects helper (Ability base class)
```csharp
protected void TriggerEffects(PlayerController player)
{
    if (Data.SpecialEffectKeys == null) return;
    var combat = player.GetCombatComponent();
    if (combat == null) return;
    foreach (var key in Data.SpecialEffectKeys)
        AbilityRegistry.Execute(key, combat);
}
```
Every ability that returns `ActiveSlot` from `Tick()` calls `TriggerEffects(player)` first.
This replaces the old pattern of PlayerController._PhysicsProcess firing effects on FSM transition.

### Data-Driven Client Visuals

The old approach of one `HitboxEvent` per visual is replaced by **`HitboxSpawnData`**: a data-driven struct that carries both the hitbox logic data AND the visual client data. The simulation spawns the hitbox; the `SpellResolver` instantiates the visual prefab referenced in `HitboxSpawnData`.

**HitboxSpawnData (Projectile/Explosion Configs)**
```csharp
// In AttackData.cs — refactored into one struct
public struct HitboxSpawnData : IHittable
{
    // — Shared — 
    public byte OwnerId;
    public Vector3 Position;
    public Vector3 Velocity;
    public AbilitySpec ConfigSpec;  // branch origin for fallback visuals
    public byte AnimHitboxIndex;    // index into spec's hitbox data (for anim-linked visuals)
    public bool IsLocalPlayer;

    // — Hitbox (melee) — 
    public HitboxShape Shape;
    public float Radius;
    public float OffX, OffY, OffZ;
    public float EndOffX, EndOffY, EndOffZ;
    public ushort DurationTicks;
    public ushort Damage;
    public float KnockbackForce;
    public bool Interruptible;

    // — Projectile — 
    public Action<Collider> OnHit;  // attached in SpawnPrefab
    public byte Type;               // ProjectileType enum

    // — Explosion — 
    public ExplosionConfig Explosion;
    public bool HasExplosion;

    // — Prefab lookup — 
    public string PrefabPath => Type switch {
        ProjectileType.RoundBomb => "res://projectiles/round_bomb.tscn",
        ProjectileType.ExplosionSmall => "res://fx/explosion_small.tscn",
        _ => Type >= (byte)ProjectileType.ProjectileStartIndex
            ? $"res://projectiles/projectile_{Type}.tscn"
            : $"res://fx/explosion_{Type}.tscn"
    };
}
```

**Prefab Lookup** uses `PrefabPath` computed from `Type` — no string matching at runtime. Fallback visuals derive from `ConfigSpec.Name` on the prefab side.

**SpellResolver** handles both melee and projectile spawning, with the visual attachment now happening at the prefab level:

```csharp
// SpellResolver.SpawnHitbox:
var spawnData = ...;
// Either spawn a projectile (with visual baked into the prefab path)
var projectile = ProjectilePool.GetOrSpawn(spawnData.PrefabPath);
// Or a melee hitbox (no visual, just a collider)
var hitbox = HitboxPool.GetOrSpawn();
```

**ProjectilePool** handles visual attachment:
- Each projectile scene is self-contained with its own `MeshInstance3D`, `CollisionShape3D`, and animation.
- `ExplosionSmall` prefab includes light, particles, and sound in one scene (no string-based VFX/audio triggers).
- `Projectile.Event_Hit` triggers `OnHit(collider)` which calls explosion or damage logic — The `OnHit` callback attached at spawn is the ONLY effect of impact.

## Projectile System

Projectiles are server-authoritative: the simulation handles trajectory, collision, and explosion. The `ProjectileType` enum defines all projectile kinds (RoundBomb, ThrowCar, AerosolFlame particles, ChomperBomb, etc.).

### Lifecycle

```
ServerSimulateTick:
1. ProjectileManager.SpawnProjectile(CreateProjectileData) 
   → stores projectile in list
2. ProjectileManager.UpdateProjectiles(dt) 
   → advances position via velocity * dt
   → checks ground collision (Projectile.Flags.HasFlag(Bounce) ? bounce : explode/destroy)
   → checks entity collision (OnTrigger for explosion zone)
3. On collision:
   → Projectile.Event_Hit(PhysicsHitEvent) 
   → calls OnHit(collider) delegate
   → for explosive projectiles: SpawnExplosion(Transform, config)
4. Projectile is destroyed or returned to pool
```

### Data
```csharp
public struct Projectile : IProjectile {
    public ProjectileType Type;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Gravity;
    public Vector3 ConstantForce;    // ← For AerosolFlameGrenade (constant upward/forward force while active)
    public float Lifetime;           // max 10s (client), max 30s (server)
    public ProjectileFlags Flags;    // Bounce, Explosive, Piercing
    public EntityRef Source;
    public HitboxEvent? HitboxSettings;   // for entity-hit damage/effect
    public Action<Collider> OnHit;   // ← Separate per-bullet callback (Explosion or ThrowableGrenade pickup)
    public ExplosionConfig ExplosionConfig;
    public byte Bounces;
    public int NetId;                // unique (incrementing ID, synchronized over network)
}
```

### Multi-Spawn Explosions

For explosions that spawn sub-projectiles (e.g. ChomperBomb's bomb-shower):
```csharp
// In ExplosionWeaponConfig:
public ProjectileType SubProjectileType;   // what each sub-projectile is
public int SubProjectileCount;             // 0 = no sub-projectiles
public float SpawnRadius;                  // ring radius for spawn positions
// In ExplosionHandler:
for (int i = 0; i < config.SubProjectileCount; i++) { 
    Vector3 dir = RandomRingPosition(config.SpawnRadius); 
    ProjectileManager.SpawnWithVelocity(owner, subProjType, pos, dir * speed);
}
```

### Networking

Projectiles are synced via:
- `ProjectileStatePacket` — sent to clients containing `Projectile.NetId`, `Position`, `Velocity`, Explosion flags
- `ProjectileDestroyPacket` — sent when projectile is destroyed or expired
- `ProjectileSpawnPacket` — initial state and type for new projectiles (lifetime, bounce flags, etc.)
- Client interpolates between state updates and handles explosion VFX/audio

## Explosion System

### ExplosionWeaponConfig
```csharp
public struct ExplosionWeaponConfig {
    public ExplosionConfig Explosion;
    public float Damage;
    public float Radius;
    public float Impulse;           // knockback force
    public bool ExplodeOnHit;       // detonate on entity impact
    public bool ExplodeOnTimer;     // detonate after lifetime
    public float TimerDuration;
    public int SubProjectileCount;  // 0 = no sub-projectiles
    public ProjectileType SubProjectileType;
}
```

### ExplosionHandler
- `ServerSimulation.ProcessExplosion()` checks damage and terrain with `OverlapSphere`
- `PreciseCollisionCheck` is disabled during `ExplosionPrecisionTimer` (post-explosion grace period)
- Explosions can be chained (AoE → child explosions for Multi-Spawn)
- Knockback + damage attenuation: `attenuation = math.max(0, 1 - distance/radius)`

### ApplyExplosion
```csharp
// Damage calculation:
float distance = Vector3.Distance(hitPoint, explosionCenter);
if (distance > config.Radius * 1.25f) return;  // slight buffer for edge-contact
float attenuation = math.max(0f, 1f - (distance / config.Radius));
float dmg = config.Damage * damageMultiplier;
```

### Collision behavior by projectile type:

| Type | OnHit terrain | OnExpire/Lifetime | OnHit entity |
|------|--------------|-------------------|---------------|
| RoundBomb | Explode | Explode | Explode |
| ThrowCar | Stop/Land | Destroy | Apply impact, bounce off |
| AerosolFlame | Explode | Explode | Explode |
| ChomperBody (MeatHook) | Destroy | Destroy | Attach, pull owner, explode |

### Explosion Visual Scaling
- Explosion particles scale with `ExplosionWeaponConfig.Radius` (set in prefab on spawn), retrieved from `ExplosionConfig`
- Sound FX use `hitSound` from the character's attack data (previously hardcoded path)
- Multiple explosions in same tick combine into one (stored in `pendingExplosions` list → multiple `OverlapSphere` calls each tick)

### Post-Explosion Precision Attack Window
- `ServerSimulation.PreciseCollisionCheck` flag: after an explosion, disabled for `ExplosionPrecisionTimer` ticks
- During this window, all collision detection is bypassed for the affected entities (simulation runs unchecked)
- After timer expires, `PreciseCollisionCheck` re-enabled (regular OverlapSphere/OverlapBox checks resume)

## Lunge/Stages Movement

### Lunge Application
Lunge is applied in `ServerAbility.OnStart()` / `Tick()` via `SetVelocityInFacing()`:

```csharp
// MankiLmbCombo.OnStart:
if (stage.LungeForce > 0f)
    SetVelocityInFacing(ref s, stage.LungeForce);

// MankiLmbCombo.Tick — reapply during lunge window:
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
- **Charging**: Charge abilities (AerosolFlame, RoundBomb) set `CanMove = false` via FSM state. On release, velocity is set from lunge + knockback.

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

## Animation & FSM Integration

### Client rendering
Client plays animations through a state machine (`StateMachine`):
```
StateMachine.States:
├── idle (CanMove = true)
├── walk (CanMove = true)
├── run (CanMove = true)
├── jump (CanMove = true)
├── fall (CanMove = true)
├── landing (CanMove = true)
├── attack (CanMove = false)
├── aimed_charge (CanMove = false)
├── hitstun (CanMove = false)
├── knockback (CanMove = false)
```

`PlayerController._PhysicsProcess`:
```
State == Attacking && !fsm.IsInState("attack")
  → TransitionTo("attack")
  → animName = spec.AnimationNames[state.AnimIndex]
```

### FSM state transitions
```
idle → walk (velocity > threshold)
idle → run (velocity > runThreshold)
idle → jump (was_on_floor && jump_pressed)
jump → fall (velocity.Y < 0)
fall → landing (was_on_floor)
idle → attack (state == Attacking)
attack → any (state changed from Attacking)
idle → aimed_charge (ability.OnActivate → fsm.TransitionTo("aimed_charge"))
aimed_charge → attack (ability.Tick returns ActiveSlot — ability fires)
any → hitstun (state == Hitstun)
any → knockback (state == Knockback)
hitstun/knockback → idle (stun ticks elapsed)
```

### Charging
Charge abilities (AerosolFlame, RoundBomb) use `AimedChargeState`:
1. `PlayerController._UnhandledInput` → `ActivateAbility(new AerosolFlame(...))`
2. `AerosolFlame.OnActivate(player)` → `fsm.TransitionTo("aimed_charge")`
3. `AimedChargeState.Enter`: plays `spec.ChargeAnimName` (loop anim) via `AnimationPlayer`
4. `AimedChargeState.Tick(delta)`: `CanMove = false`, calls `ability.Tick(player, delta)`
5. On release: ability Tick returns `ActiveSlot`, `TriggerEffects` called, `AimedChargeState` → `AttackState` → `fsm.TransitionTo("attack")` with the attack anim
6. The transition `aimed_charge → attack` happens on the SAME frame as `ability.Tick` returning `ActiveSlot`.
7. Charge duration is limited by `AbilitySpec.ChargeHoldTicks` on the server — if exceeded, the ability fires automatically (sim-side timeout).

### AnimLockTicks
```csharp
s.AnimLockTicks = stage.DurationTicks;  // sets on attack start
```
During attack, movement input is ignored. On tickdown (ServerSimulation.TickTimers), when `AnimLockTicks` reaches 0, the character can move again if state is Idle.

## CharacterStatePacket

The `CharacterStatePacket` is currently **40 bytes**. Key fields:

```csharp
public struct CharacterStatePacket
{
    public uint TickNumber;
    public float PositionX, PositionY, PositionZ;
    public float VelocityX, VelocityY, VelocityZ;
    public byte CurrentActionState;
    public ushort StateDurationFrames;
    public bool IsGrounded;
    public byte AttackSlot;
    public byte ComboStage;
    public byte AnimIndex;        // ← animation index into spec.AnimationNames[]
    public float FacingYaw;
    public MatchState MatchState;
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
- Introduced `ServerAbility` class system with `MankiLmbCombo`, `MankiRoundBomb`, `MankiAerosolFlame`
- Removed old `AbilityExecutor`, `GenericMelee`, `MeleeCombo`, `BackflipRoll`, `RoundBombSpec`, `AerosolFlameSpec`
- `AbilityFactory.CreateServer()` now dispatches by `(CharacterClass, byte slot, bool airborne)`
- Removed client-side `AttackWarping.cs` — warp is server-side via `Simulation.ProcessWarp()`
- `AnimIndex`: byte-indexed animation selection replacing string matching
- `AbilitySpec.AbilityTypeId` deprecated; `Params` dictionary is the data mechanism
- Client abilities read from `Data.Params.TryGetValue()` instead of specialized spec types
- `CharacterStatePacket` now 40 bytes with `AnimIndex` field
- Warp is velocity override (`WarpSpeed > 0`) not a separate state
- Explosion system overhaul: config-driven `ExplosionWeaponConfig`, SubProjectile system for multi-spawn
- Comprehensive documentation in `docs/systems/ability-architecture.md`

## Related Skills

- `sloparena-netcode` skill
