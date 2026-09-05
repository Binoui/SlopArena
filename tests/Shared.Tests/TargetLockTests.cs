using System;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Persistent target lock (ADR-0018 / issue #127): RMB edge toggles sim-authoritative
/// LockOn. The lock is PASSIVE — it keeps the resolved target fresh (for attack-stage
/// auto-face, the client lock camera + indicator) but does NOT steer facing while
/// moving: the fighter keeps normal movement facing (runs where it runs, sticky air
/// facing) and only turns toward the target during attacks (per-stage
/// RotateTowardTarget). The lock disengages on toggle-off, target beyond lock range
/// (10m), or an accepted LMB facing snap, and the owner's LockOn resets on death
/// (fresh respawn state). Target death re-targets through the resolver.
///
/// The golden scenarios pin the lock lifecycle (LockOn, positions, deaths). Facing
/// angles are deliberately excluded from the golden schema, so steering is asserted
/// behaviorally in companion tests.
/// </summary>
public class TargetLockTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.CombatDef;
    private static float Gpy => TestHelpers.CombatGroundPY;

    /// <summary>
    /// Test arena whose spawn point sits at feet-on-floor (Y = capsule half), so a
    /// void-death respawn lands grounded instead of sinking below the floor surface
    /// (TestArena's default spawn Y=0 is below groundY = 0.75 for this def).
    /// </summary>
    private static ArenaDefinition DeathArena()
    {
        var arena = TestHelpers.TestArena();
        arena.SpawnPoints = new[] { new SpawnPoint { X = 0, Y = Gpy, Z = 0, Yaw = 0 } };
        return arena;
    }

    // ────────────────────────── Golden scenarios ──────────────────────────

    [Fact]
    public void Golden_LockOn_Disengages_OutOfRange()
    {
        // Locked, then walks +X perpendicular to the NPC at +Z: separation passes 10m
        // (~t64 at WalkSpeed 9 m/s) → LockOn clears, player keeps moving. (+X keeps the
        // walk inside the test arena's heightmap grid; a -Z walk exits it.)
        var inputs = new InputSequence().Set(0, new InputState { ToggleLock = true });
        for (int t = 1; t < 200; t++) inputs.Set(t, new InputState { MoveX = 1f });
        AssertGoldenScenario(new KitScenario
        {
            Name = "Target Lock Out Of Range Disengage",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = inputs,
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 3f) with { PY = Gpy },
            NpcAssert = _ => { },
            NpcDef = Def,
            SnapshotTick = 120,
            TotalTicks = 200,
        });
    }

    [Fact]
    public void Golden_LockOn_LmbSnapExitsLock()
    {
        // Locked at t0; LMB facing snap at t10 (AimYaw 18000 = PI, away from the NPC).
        // The snap is accepted (idle, unlocked gates) → LockOn clears and facing snaps.
        AssertGoldenScenario(new KitScenario
        {
            Name = "Target Lock LMB Exits Lock",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f },
            Inputs = new InputSequence()
                .Set(0, new InputState { ToggleLock = true })
                .Set(10, new InputState { FaceToCamera = true, AimYaw = 18000 }),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 3f) with { PY = Gpy },
            NpcAssert = _ => { },
            NpcDef = Def,
            SnapshotTick = 60,
            TotalTicks = 120,
        });
    }

    [Fact]
    public void Golden_LockOn_DeathRetargets()
    {
        // Locked on an NPC that starts in the void (PY -25 < KillHeight -20): it dies at
        // the end of tick 0, respawns at the arena spawn (0,0) — still within lock range
        // of the player at (0,5) — and the lock re-targets the respawned enemy. Golden
        // pins: player LockOn true at snap+final, NPC Deaths=1, NPC back at spawn.
        // NPC spawns at Z=-1 (off the heightmap grid): the below-floor force-snap only
        // saves in-bounds spawns, so this one falls freely into the blast zone.
        AssertGoldenScenario(new KitScenario
        {
            Name = "Target Lock Death Re-target",
            Arena = DeathArena(),
            Def = Def,
            Setup = () => TestHelpers.PlayerState(0f, 5f) with { PY = Gpy, FacingYaw = 0f },
            Inputs = new InputSequence().Set(0, new InputState { ToggleLock = true }),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, -1f) with { PY = -25f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = Def,
            SnapshotTick = 60,
            TotalTicks = 120,
        });
    }

    // ────────────────────────── Behavioral ──────────────────────────

    [Fact]
    public void Lock_Idle_KeepsFacing_ToggleOff_FreezesIt()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = MathF.PI };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        // t0: toggle on → facing is NOT steered while idle (lock is passive); it stays PI
        // even though the target sits at +Z (yaw 0).
        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        var locked = sim.GetState(1);
        Assert.True(locked.LockOn);
        TestHelpers.AssertNear(MathF.PI, locked.FacingYaw, 1e-4f);

        // Hold the lock 29 more ticks: facing still untouched
        for (int i = 0; i < 29; i++) sim.Tick(new() { { 1, default } });
        float facingAt30 = sim.GetState(1).FacingYaw;
        TestHelpers.AssertNear(MathF.PI, facingAt30, 1e-4f);

        // t30: toggle off → facing unchanged (no movement input, no steering either way)
        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        Assert.False(sim.GetState(1).LockOn);
        for (int i = 0; i < 70; i++) sim.Tick(new() { { 1, default } });
        TestHelpers.AssertNear(facingAt30, sim.GetState(1).FacingYaw, 1e-4f);
    }

    [Fact]
    public void Lock_Air_KeepsStickyFacing_NotSteeredToTarget()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = 2f, IsGrounded = false, JumpsLeft = 0, FacingYaw = MathF.PI };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        // Lock does NOT override sticky air facing: while airborne (falls in ~4 ticks)
        // facing stays PI — the target sits at +Z (yaw 0) but the lock is passive.
        var airborne = sim.GetState(1);
        Assert.False(airborne.IsGrounded);
        TestHelpers.AssertNear(MathF.PI, airborne.FacingYaw, 1e-4f);

        for (int i = 0; i < 100; i++) sim.Tick(new() { { 1, default } });

        var state = sim.GetState(1);
        Assert.True(state.LockOn, "air lock stays on (target alive, in range)");
        TestHelpers.AssertNear(MathF.PI, state.FacingYaw, 1e-4f);
    }

    [Fact]
    public void Locked_GroundMovement_RefacesToWalk()
    {
        // Walking perpendicular (+X) while locked re-faces the fighter to the walk
        // direction (the lock is passive — it does NOT steer facing while moving).
        // So facing points +X (PI/2), NOT at the NPC.
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, new InputState { MoveX = 1f } } }); // walking +X, perpendicular

        var state = sim.GetState(1);
        Assert.True(state.LockOn, "still in range (~6.7m separation)");
        Assert.True(state.PX > 5f, "player actually walked");
        // Facing follows the walk direction (+X), not the target (yaw toward NPC ~0).
        TestHelpers.AssertNear(MathF.PI / 2f, state.FacingYaw, 1e-3f);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RosterNormals_GroundAndAir_FaceTarget(bool airborne, bool locked)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var roster = BuiltInRosterManifestCodec.Load(Path.Combine(root, "content-cooked/roster/manifest.json"));
        foreach (var entry in roster.Entries)
        {
            var def = TestHelpers.ResolveDef(entry.Selector);
            // Legacy Nilus has LMB/AirLMB normals, not the package 1–4 grid;
            // its Slot1 is the move-specific Void Rift special.
            var slots = entry.Selector == CharacterClass.Nilus
                ? new[] { AbilitySlots.Lmb }
                : new[] { AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4 };
            foreach (byte slot in slots)
                AssertNormalAttackFaces(def, slot, airborne, locked);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LegacyNilusNormals_GroundAndAir_FaceTarget(bool airborne, bool locked)
    {
        AssertNormalAttackFaces(TestHelpers.NilusDef, AbilitySlots.Lmb, airborne, locked);
    }

    [Fact]
    public void FightGuy_NonTargetEnabledSpecials_DoNotAutoFace()
    {
        var def = TestHelpers.FightGuyDef;
        AssertLockedAttackDoesNotFace(def, AbilitySlots.E, 18000);
        AssertLockedAttackDoesNotFace(def, AbilitySlots.R);
        AssertLockedAttackDoesNotFace(def, AbilitySlots.F);
    }

    private static void AssertNormalAttackFaces(CharacterDefinition def, byte activeSlot, bool airborne, bool locked)
    {
        var sim = TestHelpers.MakeSim();
        float gpy = TestHelpers.GroundPY(def);
        var player = TestHelpers.PlayerState() with
        {
            PY = airborne ? 3f : gpy,
            IsGrounded = !airborne,
            JumpsLeft = (byte)(airborne ? 0 : 2),
            FacingYaw = MathF.PI,
        };
        sim.RegisterEntity(1, def, player);
        // Outside normal hit/attack range, behind the initial facing: target rotation
        // must not depend on connecting a hit or an enemy already being in front.
        sim.RegisterEntity(100, def, TestHelpers.NpcState(0f, 5f) with { PY = gpy });
        sim.Tick(new() { { 1, new InputState { ToggleLock = locked } } });
        sim.Tick(new() { { 1, new InputState { ActiveSlot = activeSlot } } });

        var state = sim.GetState(1);
        string context = $"{def.DisplayName} {(airborne ? "air" : "ground")} slot {activeSlot}, locked={locked}";
        Assert.True(state.State == ActionState.Attacking, $"{context}: attack did not start.");
        Assert.Equal(activeSlot, state.AttackSlot);
        Assert.Equal(locked, state.LockOn);
        Assert.Equal(100UL, state.TargetEntityId);
        if (airborne) Assert.False(state.IsGrounded);
        if (locked)
        {
            Assert.True(MathF.Abs(state.FacingYaw) <= 1e-3f, $"{context}: did not snap, yaw={state.FacingYaw}.");
        }
        else
        {
            // Strong tracking corrects most of the angle on the FIRST tick, not
            // after dozens of ticks. Multiplying the fraction by TickDt fails this.
            Assert.True(MathF.Abs(state.FacingYaw) <= MathF.PI * 0.25f,
                $"{context}: tracking too weak, yaw={state.FacingYaw}.");
            float strength = def.GetSlotAbility(activeSlot - 1, airborne)!.Stages[0].TrackingStrength;
            TestHelpers.AssertNear(MathF.PI * (1f - strength), state.FacingYaw, 1e-3f);
        }
    }

    private static void AssertLockedAttackDoesNotFace(CharacterDefinition def, byte activeSlot, short aimYaw = 0)
    {
        var sim = TestHelpers.MakeSim();
        float gpy = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = gpy, FacingYaw = MathF.PI });
        sim.RegisterEntity(100, def, TestHelpers.NpcState(0f, 5f) with { PY = gpy });
        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        sim.Tick(new() { { 1, new InputState { AimYaw = aimYaw } } });
        sim.Tick(new() { { 1, new InputState { ActiveSlot = activeSlot, AimYaw = aimYaw } } });
        sim.Tick(new() { { 1, default } });
        var state = sim.GetState(1);
        Assert.True(state.LockOn);
        Assert.True(MathF.Abs(MathF.PI - state.FacingYaw) <= 1e-3f, $"active slot {activeSlot} changed facing to {state.FacingYaw}.");
    }

    [Fact]
    public void LmbSnap_ExitsLock_AndFacesCamera()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        for (int i = 0; i < 9; i++) sim.Tick(new() { { 1, default } });
        Assert.True(sim.GetState(1).LockOn);

        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } });
        var snapped = sim.GetState(1);
        Assert.False(snapped.LockOn, "accepted LMB snap exits the lock");
        TestHelpers.AssertNear(MathF.PI, snapped.FacingYaw, 1e-4f);

        // Lock stays off afterwards
        for (int i = 0; i < 30; i++) sim.Tick(new() { { 1, default } });
        Assert.False(sim.GetState(1).LockOn);
    }

    [Fact]
    public void LmbSnap_RejectedMidAttack_KeepsLock()
    {
        // The snap is rejected while attack-locked (same gate as the input layer), so
        // the lock survives: "LMB is otherwise unchanged" (ADR-0018 decision 5).
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });          // lock on
        sim.Tick(new() { { 1, new InputState { ActiveSlot = AbilitySlots.Slot1 } } }); // attack (AnimLockTicks > 0)
        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } }); // LMB mid-attack

        var state = sim.GetState(1);
        Assert.True(state.AnimLockTicks > 0, "mid-attack");
        Assert.True(state.LockOn, "rejected snap must not exit the lock");
        Assert.True(MathF.Abs(state.FacingYaw) < 1f, "no snap: facing stayed on the attack target");
    }

    [Fact]
    public void OwnerDeath_ResetsLockOn()
    {
        // LockOn resets on the OWNER's death: the respawned state is fresh (ADR-0018).
        // Spawn the player at Z=-1 — just off the 200x200 heightmap grid (origin 0,0),
        // so the below-floor force-snap doesn't rescue it: it falls freely into the
        // void and dies (blast Deaths=1). In-bounds below-floor spawns snap to the
        // floor instead (Simulation ground collision) and never reach the blast line.
        var sim = TestHelpers.MakeSim(DeathArena());
        var player = TestHelpers.PlayerState(0f, -1f) with { PY = -25f, IsGrounded = false, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } }); // lock on, then dies (void)
        for (int i = 0; i < 5; i++) sim.Tick(new() { { 1, default } });

        var state = sim.GetState(1);
        Assert.Equal((byte)1, state.Deaths);
        Assert.False(state.LockOn, "fresh respawn state has the lock off");
    }

    [Fact]
    public void TargetDeath_LockRetargetsRespawnedEnemy()
    {
        // Behavioral side of the death golden: after the NPC dies and respawns in range,
        // the lock stays on and the resolver re-targets the respawned enemy (facing is
        // not steered while idle — only the target pointer updates).
        // NPC spawns at (0,-1) — off the heightmap grid, so no below-floor force-snap;
        // it falls through the void and dies end of tick 0.
        var sim = TestHelpers.MakeSim(DeathArena());
        var player = TestHelpers.PlayerState(0f, 5f) with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, -1f) with { PY = -25f, IsGrounded = false });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } }); // t0: lock on; NPC dies end of tick
        for (int i = 0; i < 60; i++) sim.Tick(new() { { 1, default } });

        var playerState = sim.GetState(1);
        var npc = sim.GetState(100);
        Assert.Equal((byte)1, npc.Deaths);
        Assert.True(npc.IsGrounded, "NPC respawned at the arena spawn");
        Assert.True(playerState.LockOn, "lock survives target death when the respawn is in range");
        Assert.Equal(100UL, playerState.TargetEntityId);
    }
}
