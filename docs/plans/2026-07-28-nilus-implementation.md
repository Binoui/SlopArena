# Nilus (The Void Stalker) Implementation Plan

> **Status: EXECUTED.** All seven tasks shipped on `feat/nilus-void-stalker`; per-task outcomes,
> measurements and deviations are in `.superpowers/sdd/task-{1..7}-report.md`. This file is kept
> as the record of intent — **where it disagrees with the code, the code is right.** Known
> deviations from the plan as written:
>
> - Task 4 gave `ServerAbility` an injected `ArenaDefinition?`, so the "an ability cannot sample
>   the heightmap" constraint below no longer holds (corrected in place).
> - Task 5 found the plan's `pull_force = 14` yanked ~8 m against a spec contract of ~4 m; it
>   ships at **9.5** (~4.1 m measured). Task 4 found the plan's terrain premise wrong and
>   replaced the single destination test with a path trace. Task 6 found the plan's F code
>   spawned the drag pulse alongside the detonation, which erased the blast entirely.
>   See the Task 4, 5 and 6 reports.
> - Task 7 additionally wired `AttackStage.MoveX/MoveY/MoveZ` into `AirRmbAttack`: Nilus'
>   Collapse is the only stage that declares the field and nothing read it, so the slam did not
>   descend. Behaviour-neutral for every other character.
> - Task 7 shipped 8 golden scenarios, not the 6 listed under Task 7 (Q and F were missing), and
>   every `SnapshotTick` was re-measured — several in the plan landed on idle frames.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Nilus, the 4th playable character — a void-themed close/mid-range controller whose signature is a placed lingering rift — fully playable and tunable in the Shared simulation with placeholder art.

**Architecture:** Server-authoritative as always: everything lives in `src/Shared/` (pure C#, zero Unity types). One genuinely new sim capability is added first (a hitbox that re-hits on an interval, which is what makes a placed rift possible at all), then Nilus is registered, then each of the four bespoke abilities is built and tested independently. The client needs **no** code change — character select is enum-driven — but `src/Shared` must be rebuilt so Unity picks up the new DLL.

**Tech Stack:** C# netstandard2.1 (`src/Shared`), xUnit 2.9.0 + FsCheck.Xunit 3.3.4 (`tests/Shared.Tests`, net8.0), plain `Assert.*` (no FluentAssertions).

**Spec:** `docs/characters/nilus.md` — read it before starting. All damage/knockback/cooldown numbers come from there.

## Global Constraints

- **No Unity types in `src/Shared`.** No `UnityEngine.*` import, ever.
- **Server simulation is the only source of truth.** No client-side gameplay logic.
- **All tick durations are `ushort`.**
- **Knockback uses `KnockbackProfile`/`KnockbackData`**, never raw base/growth/upward triples in new spec data. `Custom` is allowed with explicit `Angle`/`BaseKnockback`/`KnockbackGrowth`.
- **`CharacterClass` ordinals are positional** — `CharacterRegistry.Get` indexes `All[(int)c]` (`CharacterDefinition.cs:180`). Append `Nilus` last and add its `BuildNilus()` at the matching index, or every lookup silently returns the wrong character.
- **Ability instances are one-shot.** A fresh instance per activation; per-activation state lives in instance fields. An instance is discarded without `OnEnd` the moment `state.State != ActionState.Attacking` (`ServerSimulation.cs:143`).
- ~~**A `ServerAbility` has no `ArenaDefinition`** — it cannot sample the heightmap.~~ **Corrected during Task 4:** `ServerAbility.Arena` is an injected `ArenaDefinition?` (`ServerAbility.cs:94`, set by `ServerSimulation.ActivateAbility`), so an ability *can* sample the heightmap. It still must not write ground-*snapping* logic — resolution stays in `Simulation`; reading the surface to validate a destination (as `NilusRiftwalk.TraceDistance` does) is the sanctioned use.
- **Velocity written onto another entity is erased during hitstun** — `ProcessHitstun` overwrites `VX`/`VZ` from `KVX`/`KVZ` every tick (`Simulation.cs:467`). To move another entity, use `Simulation.ApplyKnockback` (`Simulation.cs:906`).
- **No formatters, linters, or full-suite runs inside a task.** Each task runs only its own tests. The full suite runs once, in the final task.
- **Placeholder art is intentional** for this plan: `ModelResourcePath = "Characters/FightGuy"`, `BakedDataPath = ""`. Do not author models, prefabs, or animation configs.

---

### Task 1: Rehit-interval hitboxes (the lingering-zone primitive)

Today every hitbox dies on its first contact (`SpellResolver.cs:236`: `hb.Active = false; break;`). A placed rift must keep damaging for seconds after the cast ends, and it cannot live inside the ability (see Global Constraints). So the hitbox layer — which is already detached from ability lifetime and aged centrally — gains one field.

Semantics: `RehitIntervalTicks == 0` is today's behaviour, unchanged. `> 0` makes the hitbox a **zone**: it tests collisions only on pulse ticks (`AgeTicks % RehitIntervalTicks == 0`), hits *every* overlapping entity on that tick, and survives until `DurationTicks` expires.

**Files:**
- Modify: `src/Shared/Hitbox.cs:19-62`
- Modify: `src/Shared/AttackData.cs:107-117`
- Modify: `src/Shared/SpellResolver.cs:166-258`
- Modify: `src/Shared/ServerSimulation.cs:754-767`
- Test: `tests/Shared.Tests/RehitZoneTests.cs` (create)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: `Hitbox.RehitIntervalTicks` (`ushort`) and `ProjectileExplosion.RehitIntervalTicks` (`ushort`). Task 3 sets `ProjectileExplosion.RehitIntervalTicks = 30` to build the rift.

- [ ] **Step 1: Write the failing test**

Create `tests/Shared.Tests/RehitZoneTests.cs`:

```csharp
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Hitbox.RehitIntervalTicks: 0 = legacy one-hit-then-die, >0 = lingering zone
/// that pulses every N ticks and survives contact.
/// Knockback is zeroed in these fixtures so the target stays inside the zone.
/// </summary>
public class RehitZoneTests
{
    private static ServerSimulation SimWithNpc(out CharacterState npc)
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.MankiDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.MankiDef, player);

        npc = TestHelpers.NpcState(0f, 0f);
        npc.PY = TestHelpers.CombatGroundPY;
        TestHelpers.RegisterNpc(sim, TestHelpers.CombatDef, npc);
        return sim;
    }

    private static void Idle(ServerSimulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });
    }

    private static Hitbox Zone(CharacterState npc, float damage, ushort duration, ushort rehit) => new()
    {
        X = npc.PX, Y = npc.PY, Z = npc.PZ,
        Radius = 2f, Shape = HitboxShape.Sphere,
        EndX = npc.PX, EndY = npc.PY, EndZ = npc.PZ,
        Damage = damage,
        BaseKnockback = 0f, KnockbackGrowth = 0f, KnockbackAngle = 0,
        StunTicks = 0,
        DurationTicks = duration,
        OwnerId = 1,
        RehitIntervalTicks = rehit,
    };

    [Fact]
    public void ZeroInterval_HitsExactlyOnce()
    {
        var sim = SimWithNpc(out var npc);
        sim.Resolver.Spawn(Zone(npc, 5f, duration: 120, rehit: 0));

        Idle(sim, 90);

        Assert.Equal((ushort)5, sim.GetState(100).DamagePercent);
    }

    [Fact]
    public void Interval30_HitsOnEveryPulse()
    {
        var sim = SimWithNpc(out var npc);
        sim.Resolver.Spawn(Zone(npc, 3f, duration: 91, rehit: 30));

        Idle(sim, 91);

        // Pulses at AgeTicks 0, 30, 60, 90 => 4 hits x 3 damage
        Assert.Equal((ushort)12, sim.GetState(100).DamagePercent);
    }

    [Fact]
    public void Zone_ExpiresAfterDuration()
    {
        var sim = SimWithNpc(out var npc);
        sim.Resolver.Spawn(Zone(npc, 3f, duration: 31, rehit: 30));

        Idle(sim, 200);

        // Pulses at 0 and 30 only; the zone is gone long before tick 200.
        Assert.Equal((ushort)6, sim.GetState(100).DamagePercent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~RehitZoneTests --nologo`
Expected: FAIL — compile error, `'Hitbox' does not contain a definition for 'RehitIntervalTicks'`.

- [ ] **Step 3: Add the field to `Hitbox`**

In `src/Shared/Hitbox.cs`, immediately after the `CanHitOwner` field (line 61):

```csharp
        /// <summary>If true, this hitbox can hit the entity that spawned it.</summary>
        public bool CanHitOwner;

        /// <summary>
        /// 0 = one-hit-then-die (default melee/projectile behaviour).
        /// &gt; 0 = lingering zone: tests collisions only when AgeTicks % RehitIntervalTicks == 0,
        /// hits every overlapping entity on that pulse, and survives until DurationTicks expires.
        /// </summary>
        public ushort RehitIntervalTicks;
```

- [ ] **Step 4: Add the field to `ProjectileExplosion`**

In `src/Shared/AttackData.cs`, inside `ProjectileExplosion` after `CanHitOwner` (line 116):

```csharp
        /// <summary>If true, this explosion can hit its spawner (mine jump, etc.).</summary>
        public bool CanHitOwner;
        /// <summary>Propagated to Hitbox.RehitIntervalTicks — makes the explosion a lingering zone.</summary>
        public ushort RehitIntervalTicks;
```

- [ ] **Step 5: Implement the pulse behaviour in `SpellResolver.Tick`**

In `src/Shared/SpellResolver.cs`, the entity loop currently starts at line 189 with `foreach (var entity in entities)` and ends at line 239. Wrap it in a pulse gate and change the post-hit lines.

Insert immediately before the `foreach` (after the gravity block at line 186):

```csharp
                // Zone hitboxes pulse on an interval and survive contact; everything
                // else keeps the legacy one-hit-then-die behaviour.
                bool isZone = hb.RehitIntervalTicks > 0;
                bool pulse = !isZone || (hb.AgeTicks % hb.RehitIntervalTicks == 0);

                if (pulse)
```

…then wrap the existing entity loop in it. Precisely: the `foreach (var entity in entities)` currently spanning `SpellResolver.cs:189-239` becomes the body of `if (pulse) { … }` — open the brace immediately before that `foreach` and close it immediately after the `foreach`'s own closing `}` on line 239, i.e. directly above the `// Age / expire` comment on line 241. Do not move, reorder, or reindent anything else in the method. Inside the `if (hit)` block, replace these two lines (currently `SpellResolver.cs:236-237`):

```csharp
                        hb.Active = false; // one-hit per hitbox
                        break;
```

with:

```csharp
                        if (isZone)
                            continue;   // zone survives and keeps scanning other entities
                        hb.Active = false; // one-hit per hitbox
                        break;
```

Nothing else in the method changes — ageing, expiry, explosion queuing and write-back at lines 241-254 already work for zones.

- [ ] **Step 6: Propagate the field when an explosion becomes a hitbox**

In `src/Shared/ServerSimulation.cs`, in `ProcessProjectileExplosions`, add one line to the `Spawn` initialiser (after `CanHitOwner = explosion.CanHitOwner,` at line 766):

```csharp
					CanHitOwner = explosion.CanHitOwner,
					RehitIntervalTicks = explosion.RehitIntervalTicks,
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~RehitZoneTests --nologo`
Expected: PASS — 3 tests.

- [ ] **Step 8: Verify no existing behaviour regressed**

Run: `dotnet test tests/Shared.Tests/ --nologo`
Expected: PASS — every pre-existing test still green. `RehitIntervalTicks` defaults to `0`, so all current hitboxes keep one-hit semantics. If anything fails here, the pulse gate was mis-nested; re-check Step 5's braces.

- [ ] **Step 9: Commit**

```bash
git add src/Shared/Hitbox.cs src/Shared/AttackData.cs src/Shared/SpellResolver.cs src/Shared/ServerSimulation.cs tests/Shared.Tests/RehitZoneTests.cs
git commit -m "feat(sim): rehit-interval hitboxes for lingering zones"
```

---

### Task 2: Register Nilus and its data-driven slots

Registers the class and authors `NilusData.cs` with movement plus all 8 `AbilitySpec`s. Four slots are fully playable at the end of this task (LMB, AirLMB, RMB, AirRMB) because they need no bespoke code. The four bespoke slots (Q/E/R/F) get their spec data here and their classes in Tasks 3-6; until then their factory arms return the placeholder `null` noted in Step 6.

This task also renames `KistuChargeAttack` → `LungeChargeAttack`. Reason: `ChargeAttackAbility` is abstract (`ChargeAttackAbility.cs:21`) and `KistuChargeAttack`'s entire body is `if (stage.LungeForce != 0f) SetVelocityInFacing(ref s, stage.LungeForce);` — behaviour that is not Kistu-specific. Nilus' RMB is the second consumer, so the class is renamed rather than duplicated.

**Files:**
- Modify: `src/Shared/CharacterDefinition.cs:7-13` (enum), `:182-191` (registry)
- Create: `src/Shared/Characters/NilusData.cs`
- Modify: `src/Shared/Abilities/AbilityFactory.cs:15-24` (dispatch arm), `:52-63` (Kistu arms after rename)
- Rename: `src/Shared/Abilities/KistuChargeAttack.cs` → `src/Shared/Abilities/LungeChargeAttack.cs`
- Modify: `tests/Shared.Tests/TestHelpers.cs:13` (add `NilusDef`)
- Modify: `tests/Shared.Tests/KitScenarioTests.cs:127` (add `NilusGpy`)
- Test: `tests/Shared.Tests/NilusAbilityTests.cs` (create)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `CharacterClass.Nilus`; `CharacterRegistry.Get(CharacterClass.Nilus)`; `TestHelpers.NilusDef`; `KitScenarioTests.NilusGpy`; `LungeChargeAttack`. Tasks 3-6 each replace one `null` factory arm and read `Params` keys from the specs authored here.

- [ ] **Step 1: Write the failing test**

Create `tests/Shared.Tests/NilusAbilityTests.cs`:

```csharp
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Behaviour tests for Nilus' kit. Task 2 covers registration + the four
/// data-driven slots; Tasks 3-6 append tests for Q/E/R/F.
/// </summary>
public class NilusAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.NilusDef);
    private static CharacterDefinition Def => TestHelpers.NilusDef;

    private static ServerSimulation SimWithPlayer()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, Def, player);
        return sim;
    }

    [Fact]
    public void Registry_ReturnsNilus()
    {
        Assert.Equal(CharacterClass.Nilus, Def.Class);
        Assert.Equal("Nilus", Def.DisplayName);
    }

    [Theory]
    [InlineData((byte)1)] // LMB
    [InlineData((byte)2)] // RMB
    public void DataDrivenGroundSlot_Activates(byte slot)
    {
        var sim = SimWithPlayer();
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot, aiming: true), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal(slot, t0.AttackSlot);
    }

    [Theory]
    [InlineData((byte)1)] // AirLMB
    [InlineData((byte)2)] // AirRMB
    public void AirSlot_Activates(byte slot)
    {
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 5f;
        s.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, s);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal(slot, t0.AttackSlot);
    }

    [Fact]
    public void Lmb_DamagesEnemyInClawRange()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 1.2f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: FAIL — compile error, `'CharacterClass' does not contain a definition for 'Nilus'`.

- [ ] **Step 3: Rename the shared charge-lunge class**

Use the language server so both `AbilityFactory` call sites are updated:

```json
{"action":"rename","file":"src/Shared/Abilities/KistuChargeAttack.cs","line":17,"symbol":"KistuChargeAttack","new_name":"LungeChargeAttack","apply":true}
```

Then rename the file itself:

```bash
git mv src/Shared/Abilities/KistuChargeAttack.cs src/Shared/Abilities/LungeChargeAttack.cs
```

Update the class doc comment in the renamed file so it is no longer Kistu-specific:

```csharp
/// <summary>
/// Shared charge-lunge slot behaviour (Kistu RMB/E, Nilus RMB).
///
/// Reuses the ChargeAttackAbility hold-to-charge lifecycle. The only behaviour is
/// applying the chosen attack stage's LungeForce as a forward burst on release:
///   - tap    -> Stages[1]        (short poke / reposition)
///   - charge -> ChargedStages[0] (committed heavy)
///
/// All damage/knockback/hitbox geometry is data-driven from the spec.
/// </summary>
public sealed class LungeChargeAttack : ChargeAttackAbility
```

- [ ] **Step 4: Add the enum member**

In `src/Shared/CharacterDefinition.cs:7-13` — append last, ordinals are positional:

```csharp
public enum CharacterClass : byte
{
    None,
    Manki,
    FightGuy,
    Kistu,
    Nilus
}
```

- [ ] **Step 5: Add the registry entry**

In `src/Shared/CharacterDefinition.cs:182-191`, add `BuildNilus()` at index 4:

```csharp
    private static CharacterDefinition[] BuildRegistry()
    {
        return new CharacterDefinition[]
        {
            default,            // None (placeholder)
            BuildManki(),       // Manki
            BuildFightGuy(),    // FightGuy
            BuildKistu(),       // Kistu
            BuildNilus(),       // Nilus
        };
    }
```

- [ ] **Step 6: Add the factory dispatch**

In `src/Shared/Abilities/AbilityFactory.cs`, add the arm at line 21 and the new method after `CreateKistuAbility`. The four `null`s are filled in by Tasks 3-6.

```csharp
            CharacterClass.Kistu => CreateKistuAbility(slot, airborne),
            CharacterClass.Nilus => CreateNilusAbility(slot, airborne),
```

```csharp
    private static ServerAbility? CreateNilusAbility(byte slot, bool airborne) => (slot, airborne) switch
    {
        (0, false) => new LmbCombo(),          // LMB — rift claws
        (0, true) => new AirLmbCombo(),        // AirLMB — void rake
        (1, false) => new LungeChargeAttack(), // RMB — entropy lance (tap/charged)
        (1, true) => new AirRmbAttack(),       // AirRMB — collapse
        (2, _) => null,                        // Q — NilusVoidRift (Task 3)
        (3, _) => null,                        // E — NilusRiftwalk (Task 4)
        (4, _) => null,                        // R — NilusNetherGrasp (Task 5)
        (5, _) => null,                        // F — NilusEventHorizon (Task 6)
        _ => null,
    };
```

- [ ] **Step 7: Create `src/Shared/Characters/NilusData.cs`**

```csharp
namespace SlopArena.Shared;

/// <summary>
/// ═══════════════════════════════════════
/// NILUS — The Void Stalker (close/mid-range void controller)
/// ═══════════════════════════════════════
/// In-your-face controller: shortest reach on the roster, blinks in, denies the
/// retreat with a placed rift, kills with ordinary knockback (charged RMB / F).
/// Placeholder art: reuses the FightGuy prefab + empty baked data (capsule
/// hurtboxes) so the kit is fully playable in sim before its own assets exist.
/// Numbers are first-pass — see docs/characters/nilus.md.
/// </summary>
public static partial class CharacterRegistry
{
    private static CharacterDefinition BuildNilus()
    {
        return new CharacterDefinition
        {
            Class = CharacterClass.Nilus,
            DisplayName = "Nilus",
            CapsuleRadius = 0.33f,
            CapsuleHeight = 1.65f,
            HipHeight = 0.8f,
            HurtboxRadius = 1f,
            Movement = new MovementStats
            {
                WalkSpeed = 10f,
                SprintSpeed = 13f,
                DashSpeed = 32f,
                AirAcceleration = 17f,
                JumpForce = 12f,
                Gravity = 34f,
                AirFloatGravity = 0f,
                DashDurationTicks = 15,
                DashCooldownTicks = 48,
                GroundFriction = 15f,
                AirFriction = 0.45f,
                MaxFallSpeed = 46f,
                MaxJumps = 2,
                JumpSquatTicks = 5,
                FloatWindowTicks = 40,
                FallRampDuration = 12,
            },

            // No baked skeleton yet → capsule hurtbox fallback (placeholder).
            HurtboxBoneDefs = null,
            HurtboxCapsules = new HurtboxCapsule[]
            {
                new(0f, 0.2f, 0f, 0f, 0.9f, 0f, 0.3f),
                new(0f, 1.2f, 0f, 0f, 1.2f, 0f, 0.22f),
                new(0.3f, 0.8f, 0f, 0.6f, 0.6f, 0.2f, 0.12f),
                new(-0.3f, 0.8f, 0f, -0.6f, 0.6f, 0.2f, 0.12f),
                new(0.15f, 0f, 0f, 0.15f, -0.8f, 0f, 0.16f),
                new(-0.15f, 0f, 0f, -0.15f, -0.8f, 0f, 0.16f),
            },
            VisualScale = 1f,
            HurtboxBoneScale = 1.0f,
            ModelSoleOffset = 0f,
            AutoModelYOffset = true,
            ModelYOffset = 0f,
            ModelResourcePath = "Characters/FightGuy", // placeholder stand-in prefab
            BakedDataPath = "",                        // empty → capsule hurtboxes

            // ═══ ABILITIES ═══

            // LMB — Rift Claws (3 hits; 1-2 deliberately low base KB = "sticky", 3rd launches)
            LMB = new AbilitySpec
            {
                Name = "Rift Claws",
                Description = "Three-hit claw chain. The first two barely move the target; the third launches.",
                IconName = "lmb",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 28, ChainWindowTicks = 10, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.42f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.4f,
                                    Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 12, BaseKnockback = 1.5f, KnockbackGrowth = 1f },
                                    StunTicks = 16, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                    new() { DurationTicks = 28, ChainWindowTicks = 10, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.42f,
                                    OffX = 0, OffY = 0.7f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.4f,
                                    Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 12, BaseKnockback = 1.5f, KnockbackGrowth = 1f },
                                    StunTicks = 18, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                    new() { DurationTicks = 38, ChainWindowTicks = 0, LungeForce = 7f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 9, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.5f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.6f,
                                    Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Launcher },
                                    StunTicks = 28, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" },
                Params = new() { ["lunge_duration"] = 6f },
            },

            // AirLMB — Void Rake (2-hit juggle glue)
            AirLMB = new AbilitySpec
            {
                Name = "Void Rake",
                Description = "Two-hit aerial claw rake. Keeps enemies airborne for juggles.",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 24, ChainWindowTicks = 9, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.42f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.4f,
                                    Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light },
                                    StunTicks = 16, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                    new() { DurationTicks = 30, ChainWindowTicks = 0, LungeForce = 4f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.48f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.5f,
                                    Damage = 5f, Knockback = new() { Profile = KnockbackProfile.Launcher },
                                    StunTicks = 26, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_air_1", "spell_lmb_air_2" },
            },

            // RMB — Entropy Lance: tap = poke, charged = the kill move
            RMB = new AbilitySpec
            {
                Name = "Entropy Lance",
                Description = "Hold to charge a void spear. Tap = quick poke; charged = blast-zone kill.",
                IconName = "rmb",
                CooldownTicks = 60,
                Behavior = AbilityBehavior.ChargeAttack,
                ChargeHoldTicks = 50,
                Stages = new AttackStage[]
                {
                    // Stage 0: hold/charge phase (safety net)
                    new() { DurationTicks = 300, ChainWindowTicks = 0, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                    // Stage 1: tap poke
                    new() { DurationTicks = 30, ChainWindowTicks = 0, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.45f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 2.2f,
                                    Damage = 9f, Knockback = new() { Profile = KnockbackProfile.Medium },
                                    StunTicks = 22, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    // Charged: piercing void spear, kill knockback
                    new() { DurationTicks = 44, ChainWindowTicks = 0, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 7, Shape = HitboxShape.Capsule, Radius = 0.6f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 2.2f,
                                    Damage = 15f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 15, BaseKnockback = 18, KnockbackGrowth = 10 },
                                    StunTicks = 40, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_rmb_loop", "spell_rmb_attack" },
            },

            // AirRMB — Collapse (downward spike)
            AirRMB = new AbilitySpec
            {
                Name = "Collapse",
                Description = "Committed downward void slam. Spikes the target toward the floor.",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 36, ChainWindowTicks = 0, MoveY = -14f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 8, Shape = HitboxShape.Sphere, Radius = 0.8f,
                                    OffX = 0, OffY = 0.1f, OffZ = 0.4f,
                                    Damage = 10f, Knockback = new() { Profile = KnockbackProfile.Spike },
                                    StunTicks = 30, Interruptible = true } },
                            AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_rmb_air" },
            },

            // Q — Void Rift (signature; lobbed seed → grounded lingering rift). Class: NilusVoidRift (Task 3)
            Q = new AbilitySpec
            {
                Name = "Void Rift",
                Description = "Lob a void seed. Where it lands, a rift lingers for 4s, damaging anything inside.",
                IconName = "q",
                CooldownTicks = 600,
                Behavior = AbilityBehavior.AimedProjectile,
                AimMode = AimMode.GroundCursor,
                ChargeHoldTicks = 180,             // 3s max aim, same as Manki's Q
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40, ChainWindowTicks = 0, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_q", "spell_q", "spell_q" },
                Params = new()
                {
                    ["charge_hold_ticks"] = 180f,
                    ["throw_trigger_tick"] = 10f,
                    ["throw_duration"] = 40f,
                    ["max_range"] = 12f,
                    ["launch_angle"] = 30f,
                    ["gravity"] = 30f,
                    ["launch_offset_y"] = 1.2f,
                    ["hitbox_radius"] = 0.5f,
                    ["seed_damage"] = 0f,          // the seed itself is inert; the rift does the work
                    ["max_flight_ticks"] = 90f,
                    ["rift_radius"] = 3f,
                    ["rift_damage"] = 3f,
                    ["rift_duration_ticks"] = 240f,
                    ["rift_rehit_ticks"] = 30f,
                    ["rift_stun_ticks"] = 6f,
                    ["rift_kb_angle"] = 15f,
                    ["rift_kb_base"] = 2f,
                    ["rift_kb_growth"] = 1f,
                },
            },

            // E — Riftwalk (2-charge blink; primary recovery). Class: NilusRiftwalk (Task 4)
            E = new AbilitySpec
            {
                Name = "Riftwalk",
                Description = "Blink 6m through the void, bursting on arrival. Two charges — also your only recovery.",
                IconName = "e",
                CooldownTicks = 0, // limited by the charge pool, not a flat cooldown
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 8, ChainWindowTicks = 0, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_e" },
                Params = new()
                {
                    ["max_charges"] = 2f,
                    ["charge_regen_ticks"] = 300f,
                    ["blink_distance"] = 6f,
                    ["burst_tick"] = 4f,
                    ["burst_radius"] = 1.6f,
                    ["burst_damage"] = 4f,
                    ["burst_stun_ticks"] = 12f,
                },
            },

            // R — Nether Grasp (aimed claw, yanks target inward). Class: NilusNetherGrasp (Task 5)
            R = new AbilitySpec
            {
                Name = "Nether Grasp",
                Description = "Void claw that seizes a target and drags them to you.",
                IconName = "r",
                CooldownTicks = 480,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 34, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 10, Shape = HitboxShape.Capsule, Radius = 0.6f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 8f,
                                    Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 0f, KnockbackGrowth = 0f },
                                    StunTicks = 20, Interruptible = true } },
                            AttackRange = 9f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                },
                AnimationNames = new[] { "spell_r" },
                Params = new()
                {
                    ["pull_force"] = 14f,
                    ["pull_angle"] = 8f,
                    ["pull_stun_ticks"] = 20f,
                },
            },

            // F — Event Horizon (ult: telegraph → drag → kill detonation). Class: NilusEventHorizon (Task 6)
            F = new AbilitySpec
            {
                Name = "Event Horizon",
                Description = "Tear open a rift that drags everything inward, then detonates.",
                IconName = "f",
                CooldownTicks = 540,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 132, ChainWindowTicks = 0, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_f", "spell_f" },
                Params = new()
                {
                    ["windup_ticks"] = 72f,        // 1.2s telegraph
                    ["drag_duration_ticks"] = 60f,
                    ["drag_radius"] = 6f,
                    ["drag_force"] = 3f,
                    ["drag_interval_ticks"] = 10f,
                    ["drag_damage"] = 3f,
                    ["detonation_damage"] = 18f,
                    ["detonation_kb_angle"] = 40f,
                    ["detonation_kb_base"] = 16f,
                    ["detonation_kb_growth"] = 9f,
                    ["detonation_stun_ticks"] = 40f,
                },
            },
        };
    }
}
```

- [ ] **Step 8: Add the test helpers**

In `tests/Shared.Tests/TestHelpers.cs`, beside the existing defs (line 13):

```csharp
    public static CharacterDefinition NilusDef => CharacterRegistry.Get(CharacterClass.Nilus);
```

In `tests/Shared.Tests/KitScenarioTests.cs`, after `FightGuyGpy` (line 127):

```csharp
    /// <summary>
    /// Ground-level PY for Nilus with floor at 0.
    /// </summary>
    protected static float NilusGpy => TestHelpers.GroundPY(TestHelpers.NilusDef);
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: PASS — 6 tests (1 registry + 2 ground + 2 air + 1 LMB damage).

- [ ] **Step 10: Verify the rename broke nothing**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~Kistu --nologo`
Expected: PASS — Kistu's suite is unaffected by `KistuChargeAttack` → `LungeChargeAttack`.

- [ ] **Step 11: Commit**

```bash
git add src/Shared/CharacterDefinition.cs src/Shared/Characters/NilusData.cs src/Shared/Abilities/AbilityFactory.cs src/Shared/Abilities/LungeChargeAttack.cs tests/Shared.Tests/TestHelpers.cs tests/Shared.Tests/KitScenarioTests.cs tests/Shared.Tests/NilusAbilityTests.cs
git commit -m "feat(nilus): register class, author kit data, share LungeChargeAttack"
```

---

### Task 3: Q — Void Rift

The signature. A lobbed seed follows `MankiRoundBomb`'s aim-and-throw lifecycle; the difference is that its `ProjectileExplosion` carries `RehitIntervalTicks` from Task 1, so on ground contact it becomes a rift that keeps damaging for 4 seconds after the cast has ended.

The seed is inert (`seed_damage = 0`) — the rift does all the work. Ground placement is free: `SpellResolver.CheckGroundCollision` already samples the heightmap for any hitbox with `Gravity > 0` and an `Explosion`, and spawns the explosion at ground level (`SpellResolver.cs:124-146`).

**Files:**
- Create: `src/Shared/Abilities/NilusVoidRift.cs`
- Modify: `src/Shared/Abilities/AbilityFactory.cs` (Nilus arm `(2, _)`)
- Modify: `tests/Shared.Tests/NilusAbilityTests.cs` (append)

**Interfaces:**
- Consumes: `Hitbox.RehitIntervalTicks` and `ProjectileExplosion.RehitIntervalTicks` (Task 1); the `Q` spec `Params` keys authored in Task 2.
- Produces: `NilusVoidRift : ServerAbility`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Shared.Tests/NilusAbilityTests.cs`:

```csharp
    // ── Q: Void Rift ──

    [Fact]
    public void Q_Activates_AndEntersAimingState()
    {
        var sim = SimWithPlayer();
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3, aiming: true), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
        Assert.True(t0.IsAiming);
    }

    [Fact]
    public void Q_RiftDamagesRepeatedly_AfterTheCastEnds()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 4f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        // Hold to aim at ~4m, then release.
        // aimDistance is in CENTIMETRES on InputState (400 = 4m); the sim converts
        // it to CharacterState.AimTargetDistance in metres.
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 400);
        sim.Tick(new() { { 1, aim }, { 100, default } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, aim }, { 100, default } });

        // Release keeps aimDistance set — Simulation.SimulateTick refreshes
        // s.AimTargetDistance from input.AimDistance every tick and runs BEFORE
        // TickAbilities, so dropping it here would make the ability cache 0 on
        // release. Repo convention: CombatPipelineTests.cs:182 ("Release pull
        // (IsAiming=false, aimDistance still set)").
        var release = TestHelpers.Input(activeSlot: 0, aiming: false, aimDistance: 400);
        for (int i = 0; i < 60; i++) sim.Tick(new() { { 1, release }, { 100, default } });

        ushort afterCast = sim.GetState(100).DamagePercent;
        Assert.True(afterCast > 0, $"rift should have ticked at least once, got {afterCast}");

        // Nilus is out of Attacking, yet the rift keeps damaging.
        Assert.NotEqual(ActionState.Attacking, sim.GetState(1).State);
        for (int i = 0; i < 120; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > afterCast,
            "rift must keep ticking after the ability instance is gone");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: FAIL — `Q_Activates_AndEntersAimingState` fails because the `(2, _)` factory arm returns `null`, so no ability starts.

- [ ] **Step 3: Create `src/Shared/Abilities/NilusVoidRift.cs`**

```csharp
using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Nilus' Q — Void Rift (signature).
///
/// Hold to aim a ground target, release to lob an inert void seed on a parabolic arc.
/// When the seed reaches the ground, SpellResolver.CheckGroundCollision spawns its
/// ProjectileExplosion at ground level — and because that explosion carries
/// RehitIntervalTicks, the result is a LINGERING RIFT that damages everything inside
/// on an interval for rift_duration_ticks.
///
/// The rift deliberately outlives the cast: it lives in the hitbox layer, which is
/// aged by SpellResolver and is not tied to this ability instance (ServerSimulation
/// discards ability instances as soon as the caster leaves ActionState.Attacking).
///
/// Params: charge_hold_ticks, throw_trigger_tick, throw_duration, max_range,
/// launch_angle, gravity, launch_offset_y, hitbox_radius, seed_damage,
/// max_flight_ticks, rift_radius, rift_damage, rift_duration_ticks,
/// rift_rehit_ticks, rift_stun_ticks, rift_kb_angle, rift_kb_base, rift_kb_growth.
/// </summary>
public sealed class NilusVoidRift : ServerAbility
{
    private bool _seedSpawned;
    private float _cachedAimDistance;
    private float _cachedAimYaw;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _seedSpawned = false;
        _cachedAimDistance = 0f;
        _cachedAimYaw = 0f;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.IsAiming = true;
        s.AnimLockTicks = 8;
        s.ChargeTicks = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        ushort maxHoldTicks = (ushort)GetParam(def, "charge_hold_ticks", 180f);

        // ── Aim phase ──
        if (s.ComboStage == 0)
        {
            if (s.AttackElapsedTicks > 8 && AnimIndex != 1)
                AnimIndex = 1;

            bool released = !input.IsAiming || (maxHoldTicks > 0 && s.ChargeTicks >= maxHoldTicks);
            if (s.AttackElapsedTicks > 8 && released)
            {
                _cachedAimDistance = s.AimTargetDistance;
                _cachedAimYaw = s.AimYaw;
                s.ComboStage = 1;
                AnimIndex = 2;
                s.AttackElapsedTicks = 0;
            }
            return;
        }

        // ── Throw phase ──
        ushort throwTick = (ushort)GetParam(def, "throw_trigger_tick", 10f);
        if (!_seedSpawned && s.AttackElapsedTicks >= throwTick)
        {
            _seedSpawned = true;
            s.IsAiming = false;
            SpawnSeed(ref s, def);
        }

        ushort duration = (ushort)GetParam(def, "throw_duration", 40f);
        if (s.AttackElapsedTicks >= duration)
            EndAbility(ref s);
    }

    private void SpawnSeed(ref CharacterState s, CharacterDefinition def)
    {
        float distance = Math.Clamp(_cachedAimDistance, 0.5f, GetParam(def, "max_range", 12f));
        float launchAngleDeg = GetParam(def, "launch_angle", 30f);
        float g = GetParam(def, "gravity", 30f);
        float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
        float dY = (-def.CapsuleHeight * 0.5f) - launchOffsetY;

        CombatMath.ComputeProjectileLaunch(distance, launchAngleDeg * (MathF.PI / 180f), g, dY,
            out float _, out float hSpeed, out float vSpeed);

        float aimCos = MathF.Cos(_cachedAimYaw);
        float aimSin = MathF.Sin(_cachedAimYaw);

        float riftDamage = GetParam(def, "rift_damage", 3f);
        float riftRadius = GetParam(def, "rift_radius", 3f);
        ApplyBuffBonuses(ref s, ref riftDamage, ref riftRadius);

        Resolver.Spawn(new Hitbox
        {
            X = s.PX,
            Y = s.PY + launchOffsetY,
            Z = s.PZ,
            VX = hSpeed * aimSin,
            VY = vSpeed,
            VZ = hSpeed * aimCos,
            Radius = GetParam(def, "hitbox_radius", 0.5f),
            Shape = HitboxShape.Sphere,
            EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
            // The seed is inert — the rift is the payload.
            Damage = GetParam(def, "seed_damage", 0f),
            BaseKnockback = 0f,
            KnockbackGrowth = 0f,
            KnockbackAngle = 0,
            StunTicks = 0,
            DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 90f),
            OwnerId = s.EntityId,
            Gravity = g,
            Explosion = new ProjectileExplosion
            {
                Radius = riftRadius,
                Damage = riftDamage,
                Knockback = new()
                {
                    Profile = KnockbackProfile.Custom,
                    Angle = (sbyte)GetParam(def, "rift_kb_angle", 15f),
                    BaseKnockback = GetParam(def, "rift_kb_base", 2f),
                    KnockbackGrowth = GetParam(def, "rift_kb_growth", 1f),
                },
                StunTicks = (ushort)GetParam(def, "rift_stun_ticks", 6f),
                DurationTicks = (ushort)GetParam(def, "rift_duration_ticks", 240f),
                RehitIntervalTicks = (ushort)GetParam(def, "rift_rehit_ticks", 30f),
            },
        });
    }
}
```

- [ ] **Step 4: Wire the factory arm**

In `src/Shared/Abilities/AbilityFactory.cs`, in `CreateNilusAbility`:

```csharp
        (2, _) => new NilusVoidRift(),         // Q — void rift
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: PASS — 8 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Shared/Abilities/NilusVoidRift.cs src/Shared/Abilities/AbilityFactory.cs tests/Shared.Tests/NilusAbilityTests.cs
git commit -m "feat(nilus): Q Void Rift — lobbed seed leaves a lingering rift"
```

---

### Task 4: E — Riftwalk

A 2-charge blink that is both the approach and the only recovery. The charge pool is entirely data-driven: `ServerSimulation` gates activation on `max_charges` and increments `ChargeStockSpent` itself (`ServerSimulation.cs:409-434`), and `Simulation` regenerates on `charge_regen_ticks` (`Simulation.cs:429-439`). This ability writes no charge logic at all.

Do **not** attempt terrain tracing — an ability has no `ArenaDefinition`. Writing `PX`/`PZ` is correct and safe: the next tick's ground resolution force-snaps Nilus up if he landed below a surface (`Simulation.cs:348-353`), and off-stage samples return `float.MinValue`, so he falls. That fall is the intended risk.

**Files:**
- Create: `src/Shared/Abilities/NilusRiftwalk.cs`
- Modify: `src/Shared/Abilities/AbilityFactory.cs` (Nilus arm `(3, _)`)
- Modify: `tests/Shared.Tests/NilusAbilityTests.cs` (append)

**Interfaces:**
- Consumes: the `E` spec `Params` from Task 2.
- Produces: `NilusRiftwalk : ServerAbility`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Shared.Tests/NilusAbilityTests.cs`:

```csharp
    // ── E: Riftwalk ──

    [Fact]
    public void E_BlinksForwardBySpecDistance()
    {
        var sim = SimWithPlayer();
        float startZ = sim.GetState(1).PZ;

        // FacingYaw 0 => +Z forward.
        for (int i = 0; i < 12; i++)
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 4) : default } });

        float travelled = sim.GetState(1).PZ - startZ;
        TestHelpers.AssertNear(6f, travelled, 0.75f);
    }

    [Fact]
    public void E_SpendsChargeAndBlocksWhenPoolEmpty()
    {
        var sim = SimWithPlayer();

        // First blink
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default } });
        Assert.Equal((byte)1, sim.GetState(1).ChargeStockSpent);

        // Second blink
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default } });
        Assert.Equal((byte)2, sim.GetState(1).ChargeStockSpent);

        // Third is blocked — pool exhausted
        float beforeZ = sim.GetState(1).PZ;
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default } });

        Assert.Equal((byte)2, sim.GetState(1).ChargeStockSpent);
        TestHelpers.AssertNear(beforeZ, sim.GetState(1).PZ, 0.2f);
    }

    [Fact]
    public void E_ArrivalBurstDamagesNearbyEnemy()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 6f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 16; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: FAIL — no blink happens; the `(3, _)` arm is still `null`.

- [ ] **Step 3: Create `src/Shared/Abilities/NilusRiftwalk.cs`**

```csharp
using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Nilus' E — Riftwalk. A short blink in the facing direction that also works
/// airborne, making it his primary recovery AND his primary approach.
///
/// Position is written directly; no terrain trace is possible or needed (a
/// ServerAbility has no ArenaDefinition). The next tick's ground resolution
/// force-snaps him up if he landed inside a surface (Simulation.cs:348-353),
/// and blinking past the stage edge simply drops him — the intended risk.
///
/// The charge pool is data-driven: ServerSimulation blocks activation when
/// ChargeStockSpent >= max_charges and spends the charge itself; Simulation
/// regenerates on charge_regen_ticks. This class contains no charge logic.
///
/// Params: blink_distance, burst_tick, burst_radius, burst_damage, burst_stun_ticks.
/// </summary>
public sealed class NilusRiftwalk : ServerAbility
{
    private ushort _ticks;
    private bool _blinked;
    private bool _burst;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;
        _blinked = false;
        _burst = false;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;

        var spec = def.GetSlotAbility(Slot, airborne: false);
        s.AnimLockTicks = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)8;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        // Blink on the first tick: displace along facing, kill residual velocity so
        // the character does not keep sliding out of the arrival position.
        if (!_blinked)
        {
            _blinked = true;
            float distance = GetParam(def, "blink_distance", 6f);
            s.PX += MathF.Sin(s.FacingYaw) * distance;
            s.PZ += MathF.Cos(s.FacingYaw) * distance;
            s.VX = 0f;
            s.VZ = 0f;
        }

        // Arrival burst — a normal one-hit hitbox centred on the arrival point.
        ushort burstTick = (ushort)GetParam(def, "burst_tick", 4f);
        if (!_burst && _ticks >= burstTick)
        {
            _burst = true;

            float damage = GetParam(def, "burst_damage", 4f);
            float radius = GetParam(def, "burst_radius", 1.6f);
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            var (kbAngle, kbBase, kbGrowth) = new KnockbackData { Profile = KnockbackProfile.Light }.Resolve();

            Resolver.Spawn(new Hitbox
            {
                X = s.PX, Y = s.PY + 0.5f, Z = s.PZ,
                EndX = s.PX, EndY = s.PY + 0.5f, EndZ = s.PZ,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = damage,
                BaseKnockback = kbBase,
                KnockbackGrowth = kbGrowth,
                KnockbackAngle = kbAngle,
                StunTicks = (ushort)GetParam(def, "burst_stun_ticks", 12f),
                DurationTicks = 4,
                OwnerId = s.EntityId,
            });
        }

        if (_ticks >= s.AnimLockTicks)
            EndAbility(ref s);
    }
}
```

- [ ] **Step 4: Wire the factory arm**

```csharp
        (3, _) => new NilusRiftwalk(),         // E — riftwalk
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: PASS — 11 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Shared/Abilities/NilusRiftwalk.cs src/Shared/Abilities/AbilityFactory.cs tests/Shared.Tests/NilusAbilityTests.cs
git commit -m "feat(nilus): E Riftwalk — 2-charge blink with arrival burst"
```

---

### Task 5: R — Nether Grasp

The combo engine: a long reaching claw that drags the target to Nilus. The spec's `HitboxEvent` deliberately carries **zero** knockback — all displacement happens in `OnHitEntity`, which calls `Simulation.ApplyKnockback` with the direction pointing from the target *toward* Nilus.

This must be knockback, not a velocity write: `ProcessHitstun` overwrites `VX`/`VZ` from `KVX`/`KVZ` every tick (`Simulation.cs:467`), so a plain `target.VX = …` is erased on the very next tick.

**Files:**
- Create: `src/Shared/Abilities/NilusNetherGrasp.cs`
- Modify: `src/Shared/Abilities/AbilityFactory.cs` (Nilus arm `(4, _)`)
- Modify: `tests/Shared.Tests/NilusAbilityTests.cs` (append)

**Interfaces:**
- Consumes: the `R` spec (`Stages[0].HitboxEvents`, `Params`) from Task 2.
- Produces: `NilusNetherGrasp : ServerAbility`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Shared.Tests/NilusAbilityTests.cs`:

```csharp
    // ── R: Nether Grasp ──

    [Fact]
    public void R_PullsTargetTowardNilus()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 6f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        float startDistance = sim.GetState(100).PZ - sim.GetState(1).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 40; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        float endDistance = sim.GetState(100).PZ - sim.GetState(1).PZ;

        Assert.True(sim.GetState(100).DamagePercent > 0, "grasp should damage");
        Assert.True(endDistance < startDistance - 1f,
            $"target should be dragged inward: {startDistance:F2}m -> {endDistance:F2}m");
    }

    [Fact]
    public void R_PullsAirborneTargetToo()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = GroundPY + 3f;
        npc.IsGrounded = false;
        TestHelpers.RegisterNpc(sim, Def, npc);

        float startDistance = sim.GetState(100).PZ - sim.GetState(1).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 40; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).PZ - sim.GetState(1).PZ < startDistance - 0.5f);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: FAIL — no damage and no displacement; the `(4, _)` arm is still `null`.

- [ ] **Step 3: Create `src/Shared/Abilities/NilusNetherGrasp.cs`**

```csharp
using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Nilus' R — Nether Grasp. A long reaching void claw; on connect the target is
/// dragged toward Nilus and stunned, setting up the claw string or a rift tick.
///
/// The drag is implemented as knockback pointed AT Nilus (Simulation.ApplyKnockback),
/// not as a velocity write: ProcessHitstun overwrites VX/VZ from KVX/KVZ every tick
/// (Simulation.cs:468), so knockback velocity is the only channel that survives.
/// The spec's HitboxEvent therefore carries zero knockback of its own.
///
/// Works identically on grounded and airborne targets — an airborne target is
/// pulled down-and-in, which is the intended anti-air answer.
///
/// Params: pull_force, pull_angle, pull_stun_ticks.
/// </summary>
public sealed class NilusNetherGrasp : ServerAbility
{
    private ushort _ticks;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.VX = 0f;
        s.VZ = 0f;

        var spec = def.GetSlotAbility(Slot, airborne: false);
        s.AnimLockTicks = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)34;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        // Spawn the reaching claw hitbox from the spec.
        var spec = def.GetSlotAbility(Slot, airborne: false);
        if (spec?.Stages is { Length: > 0 })
        {
            foreach (var evt in spec.Stages[0].HitboxEvents)
            {
                if (evt.TriggerTick == _ticks)
                    SpawnHitbox(ref s, evt);
            }
        }

        if (_ticks >= s.AnimLockTicks)
            EndAbility(ref s);
    }

    /// <summary>Drag the target inward. Knockback aimed at Nilus, so hitstun preserves it.</summary>
    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, ref float damage, ref float knockbackForce)
    {
        float dx = attacker.PX - target.PX;
        float dz = attacker.PZ - target.PZ;
        float dist = MathF.Sqrt((dx * dx) + (dz * dz));
        if (dist < 0.01f) return;

        var spec = attackerDef.GetSlotAbility(Slot, airborne: false);
        float force = 14f, angle = 8f, stun = 20f;
        if (spec?.Params != null)
        {
            if (spec.Params.TryGetValue("pull_force", out float f)) force = f;
            if (spec.Params.TryGetValue("pull_angle", out float a)) angle = a;
            if (spec.Params.TryGetValue("pull_stun_ticks", out float st)) stun = st;
        }

        Simulation.ApplyKnockback(ref target, dx / dist, dz / dist,
            (sbyte)angle, force, 0f, (ushort)stun);
    }
}
```

- [ ] **Step 4: Wire the factory arm**

```csharp
        (4, _) => new NilusNetherGrasp(),      // R — nether grasp
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: PASS — 13 tests.

If the drag distance is short of ~4m, tune `pull_force` in `NilusData.cs` (magnitude decays under `KnockbackDrag`); do not add per-tick pulling.

- [ ] **Step 6: Commit**

```bash
git add src/Shared/Abilities/NilusNetherGrasp.cs src/Shared/Abilities/AbilityFactory.cs tests/Shared.Tests/NilusAbilityTests.cs
git commit -m "feat(nilus): R Nether Grasp — inward knockback yank"
```

---

### Task 6: F — Event Horizon

The ult. Three phases in one ability: a 1.2s telegraph, a 60-tick inward drag with damage pulses, then a Kill-knockback detonation. Because Nilus stays locked in `ActionState.Attacking` for the whole duration, the instance survives and can run its own per-tick loop over `SimulationStates` — this is the `FightGuyTempest` pattern (`FightGuyTempest.cs:59-78`), and it is only valid *because* the caster is locked.

**Files:**
- Create: `src/Shared/Abilities/NilusEventHorizon.cs`
- Modify: `src/Shared/Abilities/AbilityFactory.cs` (Nilus arm `(5, _)`)
- Modify: `tests/Shared.Tests/NilusAbilityTests.cs` (append)

**Interfaces:**
- Consumes: the `F` spec `Params` from Task 2.
- Produces: `NilusEventHorizon : ServerAbility`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Shared.Tests/NilusAbilityTests.cs`:

```csharp
    // ── F: Event Horizon ──

    [Fact]
    public void F_LocksCasterInPlaceDuringWindup()
    {
        var sim = SimWithPlayer();
        float startZ = sim.GetState(1).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });
        // Try to walk out of it — the lock must hold.
        var walking = TestHelpers.Input(activeSlot: 0);
        walking.MoveY = 1f;
        for (int i = 0; i < 40; i++) sim.Tick(new() { { 1, walking } });

        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        TestHelpers.AssertNear(startZ, sim.GetState(1).PZ, 0.3f);
    }

    [Fact]
    public void F_DragsThenDetonates()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        float startDistance = sim.GetState(100).PZ - sim.GetState(1).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });
        for (int i = 0; i < 140; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        // Drag pulses plus the detonation: 3/tick x pulses + 18.
        Assert.True(sim.GetState(100).DamagePercent > 18,
            $"expected drag ticks + detonation, got {sim.GetState(100).DamagePercent}");
        Assert.True(sim.GetState(100).PZ - sim.GetState(1).PZ < startDistance,
            "target should have been dragged inward before detonation");
        Assert.NotEqual(ActionState.Attacking, sim.GetState(1).State);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: FAIL — the ult never activates; the `(5, _)` arm is still `null`.

- [ ] **Step 3: Create `src/Shared/Abilities/NilusEventHorizon.cs`**

```csharp
using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Nilus' F — Event Horizon (ultimate). Three phases:
///   1. Windup (windup_ticks): telegraph, caster locked, no hitbox.
///   2. Drag (drag_duration_ticks): every drag_interval_ticks, pull everything within
///      drag_radius toward the centre and spawn a small damage pulse on it.
///   3. Detonation: one Kill-knockback hitbox, then the ability ends.
///
/// Nilus is locked in place for the whole ability (VX = VZ = 0 every tick). That lock
/// is what keeps him in ActionState.Attacking, which is what keeps this instance alive
/// — ServerSimulation drops ability instances the moment the state leaves Attacking
/// (ServerSimulation.cs:142). The per-tick SimulationStates loop is only legal here
/// for that reason (same pattern as FightGuyTempest).
///
/// Params: windup_ticks, drag_duration_ticks, drag_radius, drag_force,
/// drag_interval_ticks, drag_damage, detonation_damage, detonation_kb_angle,
/// detonation_kb_base, detonation_kb_growth, detonation_stun_ticks.
/// </summary>
public sealed class NilusEventHorizon : ServerAbility
{
    private ushort _ticks;
    private ushort _windupTicks;
    private ushort _dragDuration;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;
        _windupTicks = (ushort)GetParam(def, "windup_ticks", 72f);
        _dragDuration = (ushort)GetParam(def, "drag_duration_ticks", 60f);

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.AnimLockTicks = (ushort)(_windupTicks + _dragDuration);

        s.VX = 0f;
        s.VZ = 0f;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        // Locked in place for the entire ability.
        s.VX = 0f;
        s.VZ = 0f;

        // ── Phase 1: windup / telegraph ──
        if (_ticks <= _windupTicks)
            return;

        AnimIndex = 1;
        ushort dragElapsed = (ushort)(_ticks - _windupTicks);

        // ── Phase 2: drag pulses ──
        ushort dragInterval = (ushort)GetParam(def, "drag_interval_ticks", 10f);
        float dragRadius = GetParam(def, "drag_radius", 6f);
        float dragForce = GetParam(def, "drag_force", 3f);

        if (dragInterval > 0 && dragElapsed % dragInterval == 0 && SimulationStates != null)
        {
            foreach (var kvp in SimulationStates)
            {
                ulong otherId = kvp.Key;
                if (otherId == s.EntityId) continue;

                var other = kvp.Value;
                float dist = CombatMath.HorizontalDistance(s.PX, s.PZ, other.PX, other.PZ);
                if (dist > dragRadius) continue;

                CombatMath.CalculateKnockback(s.PX, s.PZ, other.PX, other.PZ,
                    dragForce, 0, out float kx, out float _, out float kz);
                other.VX += kx;
                other.VZ += kz;
                SimulationStates[otherId] = other;
            }

            // Small damage pulse riding the drag.
            float pulseDamage = GetParam(def, "drag_damage", 3f);
            float pulseRadius = dragRadius;
            ApplyBuffBonuses(ref s, ref pulseDamage, ref pulseRadius);

            Resolver.Spawn(new Hitbox
            {
                X = s.PX, Y = s.PY + 0.5f, Z = s.PZ,
                EndX = s.PX, EndY = s.PY + 0.5f, EndZ = s.PZ,
                Radius = pulseRadius,
                Shape = HitboxShape.Sphere,
                Damage = pulseDamage,
                BaseKnockback = 0f,
                KnockbackGrowth = 0f,
                KnockbackAngle = 0,
                StunTicks = 4,
                DurationTicks = 2,
                OwnerId = s.EntityId,
            });
        }

        // ── Phase 3: detonation ──
        if (dragElapsed >= _dragDuration)
        {
            float damage = GetParam(def, "detonation_damage", 18f);
            float radius = dragRadius;
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            Resolver.Spawn(new Hitbox
            {
                X = s.PX, Y = s.PY + 0.5f, Z = s.PZ,
                EndX = s.PX, EndY = s.PY + 0.5f, EndZ = s.PZ,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = damage,
                BaseKnockback = GetParam(def, "detonation_kb_base", 16f),
                KnockbackGrowth = GetParam(def, "detonation_kb_growth", 9f),
                KnockbackAngle = (sbyte)GetParam(def, "detonation_kb_angle", 40f),
                StunTicks = (ushort)GetParam(def, "detonation_stun_ticks", 40f),
                DurationTicks = 5,
                OwnerId = s.EntityId,
            });

            EndAbility(ref s);
        }
    }
}
```

- [ ] **Step 4: Wire the factory arm**

```csharp
        (5, _) => new NilusEventHorizon(),     // F — event horizon
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusAbilityTests --nologo`
Expected: PASS — 15 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Shared/Abilities/NilusEventHorizon.cs src/Shared/Abilities/AbilityFactory.cs tests/Shared.Tests/NilusAbilityTests.cs
git commit -m "feat(nilus): F Event Horizon — telegraph, drag, kill detonation"
```

---

### Task 7: Golden regression, full suite, DLL rebuild, docs

Nothing in the test suite enumerates `CharacterClass` — every harness is registered per character by hand — so Nilus is adopted by the golden-snapshot regression only if we add it. This task adds that file, generates its goldens, runs the whole suite, rebuilds the Shared DLL so the Unity client can select Nilus, and flips the spec's status line.

**Files:**
- Create: `tests/Shared.Tests/NilusKitRegressionTests.cs`
- Create: `tests/Shared.Tests/Golden/Nilus_*.json` (generated, not hand-written)
- Modify: `docs/characters/nilus.md:5` (status)

**Interfaces:**
- Consumes: `TestHelpers.NilusDef`, `KitScenarioTests.NilusGpy` (Task 2), and all four ability classes (Tasks 3-6).
- Produces: golden snapshots pinning Nilus' kit against regressions.

- [ ] **Step 1: Write the regression scenarios**

Create `tests/Shared.Tests/NilusKitRegressionTests.cs`:

```csharp
using Xunit;

namespace SlopArena.Shared.Tests;

public class NilusKitRegressionTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.NilusDef;
    private static float Gpy => NilusGpy;

    [Fact]
    public void LMB_FullCombo_ChainsThroughAllStages()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus LMB Full Combo",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 1).Press(10, 1).Press(40, 1),
            Assert = s => Assert.Equal((byte)3, s.ComboStage),
            SnapshotTick = 45,
            TotalTicks = 200,
        });
    }

    [Fact]
    public void LMB_Stage1_HitConfirm()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus LMB Hit Confirm",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 1),
            Assert = s => Assert.Equal(ActionState.Attacking, s.State),
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.2f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = n => Assert.Equal((ushort)3, n.DamagePercent),
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 9,
            TotalTicks = 80,
        });
    }

    [Fact]
    public void RMB_Uncharged_Pokes()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus RMB Uncharged",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 2),
            Assert = s => Assert.Equal(ActionState.Attacking, s.State),
            SnapshotTick = 20,
            TotalTicks = 120,
        });
    }

    [Fact]
    public void AirRMB_Spikes()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus Air RMB Collapse",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PY = Gpy + 5f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 2),
            Assert = s => Assert.True(s.VY < 0f, $"Collapse should drive Nilus downward, VY={s.VY}"),
            SnapshotTick = 12,
            TotalTicks = 120,
        });
    }

    [Fact]
    public void E_Riftwalk_Blinks()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus E Riftwalk",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 4),
            Assert = s => Assert.Equal((byte)1, s.ChargeStockSpent),
            SnapshotTick = 6,
            TotalTicks = 120,
        });
    }

    [Fact]
    public void R_NetherGrasp_PullsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus R Nether Grasp",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 5),
            Assert = s => Assert.Equal(ActionState.Attacking, s.State),
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 6f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = n => Assert.True(n.PZ < 5.5f, $"grasp should drag the NPC inward from 6m, PZ={n.PZ}"),
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 20,
            TotalTicks = 120,
        });
    }
}
```

- [ ] **Step 2: Run them to confirm they fail on missing goldens**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusKitRegressionTests --nologo`
Expected: FAIL — golden file not found for each scenario. This proves the goldens are actually being consulted.

- [ ] **Step 3: Generate the goldens**

Run: `REGENERATE_GOLDENS=1 dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusKitRegressionTests --nologo`
Expected: PASS, and six new files appear in `tests/Shared.Tests/Golden/` (`Nilus_LMB_Full_Combo.json`, `Nilus_LMB_Hit_Confirm.json`, `Nilus_RMB_Uncharged.json`, `Nilus_Air_RMB_Collapse.json`, `Nilus_E_Riftwalk.json`, `Nilus_R_Nether_Grasp.json`).

- [ ] **Step 4: Inspect the goldens before trusting them**

Read each generated file and sanity-check it against the spec — a golden that pins *wrong* behaviour is worse than no golden. Specifically confirm:
- `Nilus_LMB_Hit_Confirm.json` — NPC `DamagePercent` is 3 (stage 1 damage).
- `Nilus_E_Riftwalk.json` — player `PZ` is ~6 (the blink landed).
- `Nilus_R_Nether_Grasp.json` — NPC `PZ` is meaningfully below 6 (dragged inward) and `DamagePercent` is 8.

If any value contradicts the spec, fix the implementation and regenerate — do not accept the golden.

- [ ] **Step 5: Re-run without the env var to prove they verify**

Run: `dotnet test tests/Shared.Tests/ --filter FullyQualifiedName~NilusKitRegressionTests --nologo`
Expected: PASS — 6 tests, now comparing against the committed goldens.

- [ ] **Step 6: Run the entire suite**

Run: `dotnet test tests/Shared.Tests/ --nologo`
Expected: PASS — everything, including all pre-existing Manki / FightGuy / Kistu goldens and the FsCheck fuzz properties.

- [ ] **Step 7: Rebuild the Shared DLL for Unity**

Unity consumes `src/Shared` as a prebuilt DLL copied by an MSBuild target, so the client cannot see Nilus until this runs:

Run: `dotnet build src/Shared/ --nologo`
Expected: `Build succeeded`, and `client/Unity/Assets/Plugins/SlopArena.Shared/SlopArena.Shared.dll` has a fresh timestamp.

No client code change is needed — character select enumerates `CharacterClass` via `Enum.GetValues` (`CharSelectController.cs:27`), so Nilus appears in the roster automatically with the FightGuy placeholder model.

- [ ] **Step 8: Flip the spec status**

In `docs/characters/nilus.md`, line 5:

```yaml
status: "Implemented (sim) — art/anim pending"
```

- [ ] **Step 9: Commit**

```bash
git add tests/Shared.Tests/NilusKitRegressionTests.cs tests/Shared.Tests/Golden/ docs/characters/nilus.md
git commit -m "test(nilus): golden kit regression + mark spec implemented in sim"
```

- [ ] **Step 10: Manual smoke test (human)**

Launch the Unity client, pick **Nilus** in character select, and in the training arena verify by feel:
1. LMB chains 3 claw hits; the first two barely move the dummy, the third launches it.
2. RMB tapped pokes; held ~1s produces a visibly bigger hit that sends the dummy far.
3. Q lobs a seed; where it lands, the dummy standing there takes repeated damage for ~4s **after** Nilus is free to act again.
4. E blinks ~6m twice, then refuses until a charge regenerates (~5s).
5. R yanks the dummy toward Nilus from range.
6. F locks Nilus, drags the dummy in, then launches it.

Animations will be wrong or T-posed (placeholder prefab) — that is expected and out of scope.

---

## Deferred (explicitly out of scope)

- **Zone visibility in networked PvP.** `NetworkSimulationBridge.cs:51` returns a null resolver, so a remote client cannot render the rift. Local/training renders it through `ProjectileVFXManager`. Fixing this means putting active zones on the wire alongside `CharacterStatePacket`.
- **Nilus in dedicated-server PvP.** `ServerApp/Program.cs:47` and `src/Server/MatchInstance.cs:79-80` hardcode `CharacterClass.Manki`, and `CharacterClass` is not carried in any packet.
- **Art, animation, prefab, AnimConfig, baked skeleton.** A separate pass; see `docs/characters/nilus.md` → Animation Needs.
- **VFX.** Rift, blink and claw trails.
