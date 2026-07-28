# Ability Architecture - ServerAbility System

## Overview

**Every ability** uses the `ServerAbility` pattern: polymorphic C# classes with data-driven parameters. There is no data-driven fallback — all slots have a `ServerAbility` subclass.

- **Logic:** `ServerAbility` subclasses (OnStart/Tick/OnEnd lifecycle)
- **Data:** `AbilitySpec.Params` dictionary (tunable without recompiling)
- **Server:** Authoritative execution in `ServerSimulation`
- **Client:** Renders predicted state, no ability logic

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  CharacterDefinition (MankiData.cs)                  │
│  ┌────────────────────────────────────────────────┐ │
│  │ LMB = new AbilitySpec {                        │ │
│  │   Name = "Monkey Combo",                       │ │
│  │   Params = {                                   │ │
│  │     ["lunge_duration"] = 10f,                  │ │
│  │   },                                           │ │
│  │   Stages = [...],  // hitbox timing/damage    │ │
│  │ }                                              │ │
│  └────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────┐
│  AbilityFactory.CreateServer(characterClass, slot)   │
│  ┌────────────────────────────────────────────────┐ │
│  │ Manki:                                         │ │
│  │  slot 0 ground → LmbCombo                      │ │
│  │  slot 0 air   → AirLmbCombo                    │ │
│  │  slot 1 ground → MankiAerosolFlame             │ │
│  │  slot 1 air   → AirRmbAttack                   │ │
│  │  slot 2       → MankiRoundBomb                 │ │
│  │  slot 3       → MankiGrapple                   │ │
│  │  slot 4       → MankiBazooka                   │ │
│  │  slot 5       → MankiOverclock                 │ │
│  └────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────┐
│  OnStart(ref state, def)  // called once (AnimIndex via property)   │
│  Tick(ref state, input)   // called per tick; set AnimIndex         │
│  OnEnd(ref state)         // natural end only; no interrupt callback │
│  OnHitEntity(...)         // hit-time effects (status, conditional) │
└─────────────────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────┐
│  ServerSimulation.TickAbilities()                    │
│  For each active ability:                            │
│    ability.Tick(ref state, ref input, def)          │
│  If ability ended: apply cooldown, deactivate       │
└─────────────────────────────────────────────────────┘
```

All abilities spawn their own hitboxes in `Tick()` via `SpawnHitbox(ref s, evt)` or `Resolver.Spawn()`. No data-driven `SpawnHitboxEvents` method.

## Complete Slot Mapping

| Slot | Manki | FightGuy | Kistu | Shared |
|------|-------|----------|-------|--------|
| LMB ground (0) | `LmbCombo` | `LmbCombo` | `LmbCombo` | Shared via StageChainAbility |
| LMB air (0) | `AirLmbCombo` | `AirLmbCombo` | `AirLmbCombo` | Shared via StageChainAbility |
| RMB ground (1) | `MankiAerosolFlame` | `FightGuyUppercut` | `LungeChargeAttack` | Per-character |
| RMB air (1) | `AirRmbAttack` | `AirRmbAttack` | `AirRmbAttack` | Shared single-hit spike |
| Q (2) | `MankiRoundBomb` | `FightGuyKiShot` | `KistuCounter` | Per-character |
| E (3) | `MankiGrapple` | `FightGuyCycloneKick` | `LungeChargeAttack` | Per-character |
| R (4) | `MankiBazooka` | `FightGuyDragonKick` | `KistuRisingSlash` | Per-character |
| F (5) | `MankiOverclock` | `FightGuyTempest` | `KistuUltFlurry` | Per-character |

## Key Patterns

### StageChainAbility (LMB combos)

`StageChainAbility` is an abstract subclass of `ServerAbility` for multi-stage melee combos. Shared by `LmbCombo` and `AirLmbCombo` — stages come from the character's `AbilitySpec.Stages[]`.

- Input buffered immediately on LMB press during any stage
- Chain fires when the current stage expires (or at `ChainWindowTicks` before expiry)
- Lunge velocity applied at stage start, cleared after `lunge_duration` ticks
- Hitboxes spawned at each stage's `TriggerTick` via `SpawnHitbox()`

### Hold-to-Charge Ability Pattern (RMB)

RMB uses per-character ServerAbility subclasses (`MankiAerosolFlame`, `FightGuyUppercut`) with a two-phase lifecycle:

```
Phase 0: AnimIndex=0 (spell_rmb_charged/loop) → hold to charge
Phase 1: AnimIndex=1 (spell_rmb_attack) → release, attack fires
```

**Phase 0 (Hold):**
- Internal `_chargeTicks` accumulates while `input.IsAiming`
- No lunge (Manki) or gentle lunge forward (FightGuy, from spec's `Stages[0].LungeForce`)

**Release conditions (checked each tick):**
- **Manual:** `!input.IsAiming` after 5-tick debounce
- **Auto:** `_chargeTicks >= ChargeHoldTicks` or 5s failsafe (300 ticks)

**Phase 1 (Attack):**
- If `_chargeTicks >= ChargeHoldTicks` → uses `ChargedStages[0]` (bigger damage/radius)
- Otherwise → uses `Stages[1]` (normal variant)
- Lunge force from the chosen stage
- Hitboxes spawned at `TriggerTick` via `SpawnHitbox()`

| Variant | Charge Time | Stage Source |
|---------|-------------|-------------|
| Normal | < threshold | `Stages[1]` |
| Charged | >= threshold | `ChargedStages[0]` |

**Per-character params:**

| Character | ChargeHoldTicks | Charge Anim | Lunge (charge) | Normal Damage | Charged Damage |
|-----------|----------------|-------------|----------------|---------------|----------------|
| Manki | 45 (0.75s) | `spell_rmb_charged` | 0 | 8 | 14 |
| FightGuy | 180 (3s) | `spell_rmb_loop` | 2 m/s | 6×3 hits | 14×3 hits |

**Client:** Both RMB abilities have `AimMode = AimMode.None` — camera follows freely during charge.

### Hold-to-Aim Ability Pattern (Q)

Manki RoundBomb and FightGuy KiShot use a three-phase aim pipeline:

```
spell_q_start (AnimIndex=0) → spell_q_loop (AnimIndex=1) → spell_q_end (AnimIndex=2)
```

1. **OnStart**: Enter attacking, short 8-tick startup
2. **Tick (hold phase)**: After 8 ticks, loop animation. Checks `input.IsAiming`:
   - If true: stays in loop, accumulates `s.ChargeTicks` (via simulation's charge-ticks block in `SimulateTick`)
   - If false: transitions to throw phase
3. **Tick (throw phase)**: At trigger tick, spawns projectile via cached aim data. Calls `EndAbility` after throw duration.

Aim data (`AimYaw`, `AimTargetDistance`, `AimPitch`) must be cached at transition time because `SimulateTick` overwrites them every tick.

## Creating a New Ability

1. **Create ServerAbility subclass** in `Shared/Abilities/`:
```csharp
public sealed class NewAbility : ServerAbility
{
    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        s.AnimLockTicks = (ushort)GetParam(def, "duration", 30f);
        AnimIndex = 0;
    }
    
    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        // Spawn hitbox at trigger tick
        if (s.AttackElapsedTicks == 10)
            SpawnHitbox(ref s, new HitboxEvent { ... });

        if (s.AttackElapsedTicks >= s.AnimLockTicks)
            EndAbility(ref s);
    }
}
```

2. **Register in AbilityFactory:**
```csharp
// Add to the appropriate character's CreateXAbility method
(3, _) => new NewAbility(),  // slot 3 = E
```

3. **Add spec to CharacterDefinition:**
```csharp
E = new AbilitySpec
{
    Name = "New Ability",
    Params = new() { ["duration"] = 30f },
    Stages = new[] { new AttackStage { DurationTicks = 30, ... } },
    AnimationNames = new[] { "spell_e" },
}
```

### AnimIndex — Set on Ability Instance (Not Struct Field)

`AnimIndex` is a **property on `ServerAbility`** (the base class). Set it in `OnStart` and `Tick`:
```csharp
AnimIndex = 2;  // sets the property on the ability instance
```

`ActivateAbility` and `TickAbilities` sync this to `CharacterState.AnimIndex` via `state.AnimIndex = ability.AnimIndex`. Writing `s.AnimIndex` directly is overwritten on the next sync.

### OnStart
- Called once when ability activates
- Set initial state, `AnimLockTicks`, and `AnimIndex = X` (property, synced to struct)
- Apply initial velocity if needed. Do NOT set `AttackSlot` — `ActivateAbility` handles it.

### OnEnd
- Called only on natural completion (NOT interruption)
- Override to apply lingering effects. Cooldown applied automatically.

### Interruption
- Hitstun, death, or new ability activation → `OnEnd` NOT called
- Ability dropped from `_activeAbilities`
- Velocity preserved (momentum-granting abilities work correctly)

### OnHitEntity — Hit-Time Effects

Called when this ability's hitbox connects with a target. Override for status effects, conditional damage, or secondary AoE:

```csharp
public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
    CharacterDefinition attackerDef, ref float damage, ref float knockbackForce)
{
    if ((target.StatusFlags & MARK_BIT) != 0)
    {
        target.StatusFlags &= ~MARK_BIT;
        Resolver.Spawn(AoE hitbox);
    }
}
```

### SpawnHitbox — Built-in Helper

`SpawnHitbox(ref state, hitboxEvent)` in the base class handles:
- Bone-attached hitbox positioning (when `HitboxEvent.BoneName` is set and `BakedData` is available)
- Facing-relative world-space positioning (default)
- Buff bonus application (Overclock adds +3 damage, +0.5 radius)
- Knockback profile resolution

**Call this in Tick() at the right TriggerTick** rather than manually calling `Resolver.Spawn()`.

## Params — Tunable Values

All tunable values live in `AbilitySpec.Params` and are read via `GetParam(def, key, fallback)`:

```csharp
Params = new()
{
    ["lunge_duration"] = 10f,
    ["explosion_damage"] = 25f,
    ["charge_threshold"] = 45f,
}
```

Benefits: designers tune without recompiling, same class with different params per character.

## Best Practices

1. **Keep logic in Tick(), data in Params**
2. **Use `_phaseticks++` or `_stageTicks++` for tick counters** (not duration -= delta)
3. **Read params in OnStart** for performance
4. **Spawn hitboxes via `SpawnHitbox()`** — handles facing rotation and buffs
5. **End explicitly** — call `EndAbility(ref s)` when done
6. **Don't use Unity/engine types** in `src/Shared/Abilities/`
7. **Sync internal state to `s.ChargeTicks`** for test/debug visibility if your ability tracks charge

## Test Coverage

All abilities have matching xUnit tests in `tests/Shared.Tests/`:

| Test file | What it covers |
|---|---|
| `AbilityLifecycleTests.cs` | Activation + lifecycle for all slots (LMB, AirLMB, RMB charged/normal, AirRMB, Q, E, R, F) |
| `AttackToIdleTests.cs` | State transitions back to idle after attacks |
| `AttackToIdleVelocityTests.cs` | Velocity zeroed on ability end |
| `AttackIdleReTriggerTests.cs` | Held-input guard, ability timer vs struct timer |
| `PhysicsTests.cs` | State transitions, hitstun knockback |
| `CombatIntegrationTests.cs` | Two-entity stability during attacks |
| `SpellResolverTests.cs` | Hitbox collision, CanHitOwner, explosions |
| `ServerSimulationTests.cs` | Ability lifetime, cooldown, self-hit prevention |
| `CombatMathTests.cs` | Knockback formulas, DI, projectile math |
| `MankiExplosiveMineTests.cs` | Mine placement, detonation, Overclock buff |
| `FightGuyAbilityTests.cs` | All FightGuy slots: activation, hitbox, damage, mark, homing, launcher |

**Run after every ability change:**
```bash
dotnet test tests/Shared.Tests/ --nologo
```
Build + test completes in <3s. See `docs/testing.md` for details.

## Related Docs

- `architecture-overview.md` - Codebase structure
- `attack-hitbox-system.md` - Hitbox spawning details
- `netcode-architecture.md` - Server-authoritative model
