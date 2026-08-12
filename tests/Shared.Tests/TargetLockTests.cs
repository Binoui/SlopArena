using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Persistent target lock (ADR-0018 / issue #127): RMB edge toggles sim-authoritative
/// LockOn; while locked, facing lerps toward the resolved soft-lock target every tick
/// (ground + air, outside attacks); the lock disengages on toggle-off, target beyond
/// lock range (10m), or an accepted LMB facing snap, and the owner's LockOn resets on
/// death (fresh respawn state). Target death re-targets through the resolver.
///
/// The four golden scenarios pin the lock lifecycle (LockOn, positions, deaths).
/// Facing angles are deliberately excluded from the golden schema, so the turn is
/// asserted behaviorally in companion tests.
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
    public void Golden_LockOn_TracksFacing()
    {
        // Player faces away from the NPC (PI vs target yaw 0); lock on at t0, nothing
        // else. LockOn stays true through snap + final; positions untouched (movement
        // stays camera-relative — only facing moves).
        AssertGoldenScenario(new KitScenario
        {
            Name = "Target Lock On Facing Tracking",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = MathF.PI },
            Inputs = new InputSequence().Set(0, new InputState { ToggleLock = true }),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 3f) with { PY = Gpy },
            NpcAssert = _ => { },
            NpcDef = Def,
            SnapshotTick = 120,
            TotalTicks = 200,
        });
    }

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
        AssertGoldenScenario(new KitScenario
        {
            Name = "Target Lock Death Re-target",
            Arena = DeathArena(),
            Def = Def,
            Setup = () => TestHelpers.PlayerState(0f, 5f) with { PY = Gpy, FacingYaw = 0f },
            Inputs = new InputSequence().Set(0, new InputState { ToggleLock = true }),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 0f) with { PY = -25f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = Def,
            SnapshotTick = 60,
            TotalTicks = 120,
        });
    }

    // ────────────────────────── Behavioral ──────────────────────────

    [Fact]
    public void ToggleOn_LerpsFacingTowardTarget_ToggleOff_FreezesIt()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = MathF.PI };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        // t0: toggle on → facing starts rotating away from PI toward 0 (target yaw)
        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        var locked = sim.GetState(1);
        Assert.True(locked.LockOn);
        Assert.True(locked.FacingYaw < MathF.PI, $"facing should have turned toward target, was {locked.FacingYaw}");

        // Hold the lock 29 more ticks: facing keeps rotating toward 0
        float facingAt30;
        for (int i = 0; i < 29; i++) sim.Tick(new() { { 1, default } });
        facingAt30 = sim.GetState(1).FacingYaw;
        Assert.True(facingAt30 < locked.FacingYaw, "facing should keep rotating while locked");

        // t30: toggle off → facing freezes (no lock lerp, no movement input)
        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        Assert.False(sim.GetState(1).LockOn);
        for (int i = 0; i < 70; i++) sim.Tick(new() { { 1, default } });
        TestHelpers.AssertNear(facingAt30, sim.GetState(1).FacingYaw, 1e-4f);
    }

    [Fact]
    public void Lock_TracksTarget_InAir_OverridingStickyFacing()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = 2f, IsGrounded = false, JumpsLeft = 0, FacingYaw = MathF.PI };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        // Assert while still airborne (falls to ground in ~4 ticks): the lock lerp must
        // run against sticky air facing — facing turns from PI toward 0 in the air.
        var airborne = sim.GetState(1);
        Assert.False(airborne.IsGrounded);
        Assert.True(airborne.FacingYaw < MathF.PI - 1e-3f,
            $"air facing should turn from PI toward target, was {airborne.FacingYaw}");

        for (int i = 0; i < 100; i++) sim.Tick(new() { { 1, default } });

        var state = sim.GetState(1);
        Assert.True(state.LockOn, "air lock stays on (target alive, in range)");
        Assert.True(state.FacingYaw < MathF.PI - 0.5f,
            $"facing kept turning toward target after landing, was {state.FacingYaw}");
    }

    [Fact]
    public void Locked_GroundMovement_DoesNotReface_WhileLocked()
    {
        // Walking perpendicular (+X) while locked must NOT re-face the fighter to the
        // walk direction (ADR-0018: movement stays camera-relative, only facing is
        // locked to the target). Facing keeps pointing at the NPC (~0), not +X (PI/2).
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
        Assert.True(MathF.Abs(state.FacingYaw) < 1f,
            $"locked ground facing must stay on the target, not follow the walk, was {state.FacingYaw}");
        Assert.True(state.PX > 5f, "player actually walked");
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
        var sim = TestHelpers.MakeSim(DeathArena());
        var player = TestHelpers.PlayerState() with { PY = -25f, IsGrounded = false, FacingYaw = 0f };
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
        // the lock stays on and facing keeps rotating toward the (respawned) target.
        var sim = TestHelpers.MakeSim(DeathArena());
        var player = TestHelpers.PlayerState(0f, 5f) with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 0f) with { PY = -25f, IsGrounded = false });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } }); // t0: lock on; NPC dies end of tick
        for (int i = 0; i < 60; i++) sim.Tick(new() { { 1, default } });

        var playerState = sim.GetState(1);
        var npc = sim.GetState(100);
        Assert.Equal((byte)1, npc.Deaths);
        Assert.True(npc.IsGrounded, "NPC respawned at the arena spawn");
        Assert.True(playerState.LockOn, "lock survives target death when the respawn is in range");
        Assert.True(playerState.FacingYaw > 0.5f,
            $"facing keeps turning toward the respawned target (PI side), was {playerState.FacingYaw}");
        Assert.Equal(100UL, playerState.TargetEntityId);
    }
}
