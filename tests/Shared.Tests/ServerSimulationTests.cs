using Xunit;

namespace SlopArena.Shared.Tests;

public class ServerSimulationTests
{
    private static CharacterDefinition MakeTestDef()
    {
        return new CharacterDefinition
        {
            Class = CharacterClass.Manki,
            Movement = new MovementStats
            {
                RunSpeed = 5f,
                RunAccelerationA = 20f,
                RunAccelerationB = 12f,
                DashSpeed = 15f,
                AirSpeedMax = 5f,
                AirAccelStick = 3f,
                AirAccelBase = 1f,
                JumpForce = 10f,
                ShortHopForce = 6f,
                AirJumpVMultiplier = 0.8f,
                AirJumpHMultiplier = 0.85f,
                Gravity = 20f,
                AirFloatGravity = 6f,
                DashDurationTicks = 15,
                DashCooldownTicks = 30,
                GroundFriction = 0.5f,
                AirFriction = 0.1f,
                MaxFallSpeed = 20f,
                FastFallSpeed = 24f,
                MaxJumps = 2,
                JumpSquatTicks = 3,
            },
            CapsuleRadius = 0.3f,
            CapsuleHeight = 1.5f,
            HurtboxRadius = 0.4f,
            // Full-body capsule so entities appear in the hurtbox list — lets the
            // elimination tests assert untargetability meaningfully.
            HurtboxCapsules = new[] { new HurtboxCapsule(0, -0.65f, 0, 0, 0.65f, 0, 0.3f) },
            HurtboxBoneDefs = null,
            BakedDataPath = "",
        };
    }

    private static ArenaDefinition MakeTestArena()
    {
        return new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            SpawnPoints = new[]
            {
                new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 },
            },
        };
    }

    private static CharacterState MakeIdleState(ulong entityId = 1)
    {
        return new CharacterState
        {
            EntityId = entityId,
            PX = 0, PY = 0, PZ = 0,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = 2,
            AirDodgesLeft = 1,
            FacingYaw = 0,
        };
    }

    // ── Void death ──

    [Fact]
    public void Tick_EntityBelowKillHeight_RespawnsWithDeathCount()
    {
        var arena = TestHelpers.TestArena();
        var sim = new ServerSimulation(arena);
        var state = MakeIdleState(1);
        state.PZ = -1f; // off the 200x200 heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20); only dies because PZ=-1 keeps it off-floor
        sim.RegisterEntity(1, MakeTestDef(), state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(arena.SpawnPoints[0].X, result.PX);
        Assert.Equal(arena.SpawnPoints[0].Y, result.PY);
        Assert.Equal(arena.SpawnPoints[0].Z, result.PZ);
        Assert.Equal(1, result.Deaths);
        Assert.Equal(0u, result.DamagePercent);
    }

    [Fact]
    public void Tick_BelowKillHeight_RespawnsAtAssignedPosition_WithInvincibility()
    {
        // Respawn honors the per-entity respawn position (MatchInstance distributes
        // spawn points) and grants brief invincibility (issue #37).
        var arena = TestHelpers.TestArena();
        var sim = new ServerSimulation(arena);
        var state = MakeIdleState(1);
        state.PZ = -1f; // off the 200x200 heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20); only dies because PZ=-1 keeps it off-floor
        sim.RegisterEntity(1, MakeTestDef(), state);
        sim.SetRespawnPosition(1, 12f, 3f, -7f);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(12f, result.PX);
        Assert.Equal(3f, result.PY);
        Assert.Equal(-7f, result.PZ);
        Assert.Equal(1, result.Deaths);
        Assert.Equal(0u, result.DamagePercent);
        Assert.Equal((ushort)60, result.InvincibilityTicks); // 1s at 60Hz
    }

    [Fact]
    public void Tick_NoRespawnPosition_FallsBackDistributedByEntityIndex()
    {
        // Two spawn points: entity 2 dies → respawns at SpawnPoints[1], not
        // everyone stacking on SpawnPoints[0] (issue #37).
        var arena = TestHelpers.TestArena();
        arena.SpawnPoints = new[]
        {
            new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 },
            new SpawnPoint { X = 10, Y = 0, Z = 10, Yaw = 1.5f },
        };
        var sim = new ServerSimulation(arena);
        var state = MakeIdleState(2);
        state.PZ = -1f; // off the heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20)
        sim.RegisterEntity(2, MakeTestDef(), state);

        sim.Tick(new Dictionary<ulong, InputState> { { 2, default } });

        var result = sim.GetState(2);
        Assert.Equal(10f, result.PX);
        Assert.Equal(10f, result.PZ);
        Assert.Equal(1.5f, result.FacingYaw);
    }

    [Fact]
    public void Tick_DeathAtMaxDeaths_EliminatesAndFreezes()
    {
        // Losing the last stock eliminates the player: no respawn, frozen at the
        // spawn point, excluded from hurtboxes (untargetable) — issue #37.
        var arena = TestHelpers.TestArena();
        var sim = new ServerSimulation(arena, new StockMatchRule(3));
        var state = MakeIdleState(1);
        state.Deaths = 2; // on last stock
        state.PZ = -1f; // off the heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20)
        sim.RegisterEntity(1, MakeTestDef(), state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var afterDeath = sim.GetState(1);
        Assert.Equal(3, afterDeath.Deaths); // eliminated
        Assert.Equal(0u, afterDeath.DamagePercent);
        Assert.Equal(0, afterDeath.InvincibilityTicks); // no grace for spectators

        // Frozen: repeated ticks must not move it or change deaths.
        var frozenPos = (afterDeath.PX, afterDeath.PY, afterDeath.PZ);
        for (int i = 0; i < 30; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
        var later = sim.GetState(1);
        Assert.Equal(frozenPos, (later.PX, later.PY, later.PZ));
        Assert.Equal(3, later.Deaths);

        // Untargetable: not present in the last hurtbox list.
        bool inHurtboxes = false;
        foreach (var e in sim.GetLastEntityData())
            if (e.Id == 1) inHurtboxes = true;
        Assert.False(inHurtboxes);
    }

    [Fact]
    public void Tick_NoWinRule_RespawnsForever_NeverEliminates()
    {
        // Training mode (NoWinMatchRule): deaths keep counting and the entity
        // keeps respawning — no freeze at any stock threshold (issue #37 follow-up).
        var arena = TestHelpers.TestArena();
        var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        var state = MakeIdleState(1);
        state.PZ = -1f; // off the heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20)
        sim.RegisterEntity(1, MakeTestDef(), state);

        // Kill the entity 6 times — past the stock-mode threshold of 3.
        // Each pass re-parks it off-grid (the respawn lands grounded on-stage), so it
        // always falls into the void instead of being force-snapped back to the floor.
        for (int i = 0; i < 6; i++)
        {
            var s = sim.GetState(1);
            s.PX = 0f; s.PZ = -1f; s.PY = -30f;
            sim.SetState(1, s);
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
        }

        var result = sim.GetState(1);
        Assert.Equal(6, result.Deaths); // kept counting, never eliminated
        Assert.Equal(0u, result.DamagePercent);

        // Still a hurtbox target — untargetability only applies to eliminated entities.
        bool inHurtboxes = false;
        foreach (var e in sim.GetLastEntityData())
            if (e.Id == 1) inHurtboxes = true;
        Assert.True(inHurtboxes);
    }

    [Fact]
    public void Tick_InvincibleTarget_TakesNoDamage()
    {
        // Respawn (and dash) invincibility blocks incoming hits entirely (issue #37).
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;

        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = TestHelpers.CombatGroundPY;
        npc.InvincibilityTicks = 60;
        sim.RegisterEntity(100, def, npc);

        // Manki LMB stage 1 hitbox triggers ~tick 12 (see HitstunAnimationTierTests).
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var after = sim.GetState(100);
        Assert.Equal(0u, after.DamagePercent); // hit fully ignored
        Assert.Equal(0, after.HitstunTicks);
        Assert.NotEqual(ActionState.Hitstun, after.State);
    }

    [Fact]
    public void Tick_CooldownOnSlot_DoesNotCrash()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var def = MakeTestDef();
        def.LMB = new AbilitySpec
        {
            Stages = new[] { new AttackStage { DurationTicks = 10 } },
            AnimationNames = new[] { "melee" },
        };
        var state = MakeIdleState(1);
        state.Cooldown0 = 30; // cooldown on slot 1
        sim.RegisterEntity(1, def, state);

        var input = new InputState { ActiveSlot = 1 };
        // Should not throw despite cooldown blocking activation
        sim.Tick(new Dictionary<ulong, InputState> { { 1, input } });

        // Cooldown prevented ServerAbility activation — LMB press is silently dropped
        // (no data-driven fallback for attacks on cooldown)
    }

    [Fact]
    public void Tick_NoInput_StatePreserved()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var initialState = MakeIdleState(1);
        sim.RegisterEntity(1, MakeTestDef(), initialState);

        // 10 ticks with no input
        for (int i = 0; i < 10; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        // State should still be Idle, position unchanged
        Assert.Equal(ActionState.Idle, result.State);
        Assert.Equal(0f, result.PX);
        Assert.Equal(0f, result.PZ);
    }

    // ── Multiple entities ──

    [Fact]
    public void Tick_TwoEntitiesIdle_NeitherChanges()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, MakeTestDef(), MakeIdleState(1));
        sim.RegisterEntity(2, MakeTestDef(), MakeIdleState(2));

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, default },
            { 2, default },
        });

        var s1 = sim.GetState(1);
        var s2 = sim.GetState(2);
        Assert.Equal(ActionState.Idle, s1.State);
        Assert.Equal(ActionState.Idle, s2.State);
        Assert.Equal(0f, s1.PX);
        Assert.Equal(0f, s2.PX);
    }

    // ── GetState/SetState round-trip ──

    [Fact]
    public void SetState_ThenGetState_ReturnsValue()
    {
        var sim = new ServerSimulation(MakeTestArena());
        sim.RegisterEntity(1, MakeTestDef(), MakeIdleState(1));

        var modified = MakeIdleState(1);
        modified.PX = 12.5f;
        modified.DamagePercent = 50;
        sim.SetState(1, modified);

        var result = sim.GetState(1);
        Assert.Equal(12.5f, result.PX);
        Assert.Equal(50u, result.DamagePercent);
    }

    // ── GetLastEntityData after Tick ──

    [Fact]
    public void Tick_EntityRegistered_GetLastEntityDataReturnsList()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, MakeTestDef(), MakeIdleState(1));

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var data = sim.GetLastEntityData();
        Assert.NotNull(data);
        // With no HurtboxCapsules or BakedAnimationData, list may be empty
        // But the assignment should not throw
    }

    // ── Q ability self-hit ──

    [Fact]
    public void Tick_MankiQ_EntityIdSetOnRegister()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var def = CharacterRegistry.Get(CharacterClass.Manki);
        var state = MakeIdleState(1);
        sim.RegisterEntity(1, def, state);

        Assert.Equal((ulong)1, sim.GetState(1).EntityId);
    }

    [Fact]
    public void Tick_MankiQ_DoesNotHitOwner()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var def = CharacterRegistry.Get(CharacterClass.Manki);

        var pState = MakeIdleState(1);
        sim.RegisterEntity(1, def, pState);

        var nState = MakeIdleState(100);
        nState.PX = 3f;
        sim.RegisterEntity(100, def, nState);

        for (int i = 0; i < 20; i++)
        {
            var input = new Dictionary<ulong, InputState>
            {
                { 1, i == 0 ? new InputState { ActiveSlot = 3 } : default },
                { 100, default },
            };
            sim.Tick(input);
        }

        var playerAfter = sim.GetState(1);
        Assert.Equal(0u, playerAfter.DamagePercent);
    }

    [Fact]
    public void Tick_MankiQ_HoldThenThrow_EndsInIdle()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var def = CharacterRegistry.Get(CharacterClass.Manki);
        var state = MakeIdleState(1);
        sim.RegisterEntity(1, def, state);

        // Tick 0: press Q
        sim.Tick(new Dictionary<ulong, InputState>
            { { 1, new InputState { ActiveSlot = 3 } } });
        var t0 = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, t0.State);

        for (int i = 1; i < 75; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
            var s = sim.GetState(1);
            // Q: 8-tick aim hold (Aiming) + 60-tick throw phase (Attacking) = ends at tick 68.
            // Without aim held, the release fires as soon as the 8-tick lock expires (tick 8).
            bool expectedAiming = i < 8;
            bool expectedAttacking = i >= 8 && i < 68;
            Assert.True((s.State == ActionState.Aiming) == expectedAiming,
                $"tick {i}: expected {(expectedAiming ? "Aiming" : "not-Aiming")} but got {s.State}");
            Assert.True((s.State == ActionState.Attacking) == expectedAttacking,
                $"tick {i}: expected {(expectedAttacking ? "Attacking" : "not-Attacking")} but got {s.State}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Soft-lock Targeting ──
    // ═══════════════════════════════════════════════════════════════
    //
    // ProcessTargetLock() reads state.State/AttackSlot and input.TargetEntityId
    // to set state.TargetEntityId each tick. Tests use Manki LMB (stage 1:
    // UseTargetLock=true, WarpRange=6, AttackRange=4, RotateTowardTarget=true).

    [Fact]
    public void TargetEntityId_ZeroWhenNoEnemyInRange()
    {
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        sim.RegisterEntity(1, def, MakeIdleState(1));
        var npc = MakeIdleState(100);
        npc.PZ = 25f; // beyond 20m search range
        sim.RegisterEntity(100, def, npc);

        sim.Tick(new() { { 1, default }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(0ul, state.TargetEntityId);
    }

    [Fact]
    public void TargetEntityId_SetOnLmbAttack_NpcInRange()
    {
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        var npc = MakeIdleState(100);
        npc.PZ = 3f; // within AttackRange=4 → direct attack, no warp
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        // Tick 0: press LMB (slot 1)
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 1 } }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, state.State);
        Assert.Equal(100ul, state.TargetEntityId);
    }

    [Fact]
    public void TargetEntityId_NotSetWhenNpcOutOfRange()
    {
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        var npc = MakeIdleState(100);
        npc.PZ = 25f; // beyond WarpRange=10
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        sim.Tick(new() { { 1, new InputState { ActiveSlot = 1 } }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(0ul, state.TargetEntityId);
    }

    [Fact]
    public void TargetEntityId_UsesClientProvidedTarget()
    {
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        var npc = MakeIdleState(100);
        npc.PZ = 5f;
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        // Client explicitly targets NPC 100 via TargetEntityId in input
        var input = new InputState { ActiveSlot = 1, TargetEntityId = 100 };
        sim.Tick(new() { { 1, input }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(100ul, state.TargetEntityId);
    }

    [Fact]
    public void TargetEntityId_ClientTargetPreferredOverNearerNpc()
    {
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        var npcNear = MakeIdleState(100);
        npcNear.PZ = 3f;
        var npcFar = MakeIdleState(200);
        npcFar.PZ = 8f;
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npcNear);
        sim.RegisterEntity(200, def, npcFar);

        // Client targets the farther NPC, not the nearest
        var input = new InputState { ActiveSlot = 1, TargetEntityId = 200 };
        sim.Tick(new() { { 1, input }, { 100, default }, { 200, default } });

        var state = sim.GetState(1);
        Assert.Equal(200ul, state.TargetEntityId);
    }


    [Fact]
    public void TargetEntityId_SetOnLmbAttack_BeyondEngageRange()
    {
        // Warp is gone (ADR-0015): an enemy beyond the move's engage range still gets
        // soft-locked as TargetEntityId, but nothing warps — the attack is a commitment.
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        var npc = MakeIdleState(100);
        npc.PZ = 5.5f; // beyond engage range (2.0), inside the 20m target-lock search
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        sim.Tick(new() { { 1, new InputState { ActiveSlot = 1 } }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(100ul, state.TargetEntityId);
        Assert.Equal(0f, state.WarpSpeed); // warp never initiates — no auto-approach
    }

    [Fact]
    public void TargetEntityId_RotationTowardNpc()
    {
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        player.FacingYaw = 0f; // facing +Z
        var npc = MakeIdleState(100);
        npc.PX = 1f; // to the right (+X) from player, within engage range (2.0)
        npc.PZ = 0f;
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        // LMB with RotateTowardTarget=true, TrackingStrength=0.9
        // Target is at (+X, 0Z) → expected yaw should rotate toward +X (π/2 rad)
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 1 } }, { 100, default } });
        sim.Tick(new() { { 1, default }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(100ul, state.TargetEntityId);
        // FacingYaw should have rotated toward the NPC (positive yaw = turning right)
        Assert.True(state.FacingYaw > 0.01f,
            $"Expected FacingYaw > 0 (should rotate toward +X), got {state.FacingYaw:F4}");
    }
    // ── Target lock rotation (3-zone) ──

    [Fact]
    public void Tick_TargetLock_FarAway_RotatesNoWarp()
    {
        // Zone 1: dist > WarpRange → NO warp, but facing still rotates toward the target
        // (attack-range no longer gates facing — issue #127).
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        player.FacingYaw = 0f; // facing +Z
        var npc = MakeIdleState(100);
        npc.PX = 5f; // off-axis so rotation would happen if not gated
        npc.PZ = 15f; // distance ≈ 15.8 > WarpRange=10
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        // LMB stage 1: WarpRange=10, AttackRange=2
        var input = TestHelpers.Input(activeSlot: 1);
        sim.Tick(new() { { 1, input }, { 100, default } });

        var state = sim.GetState(1);
        // Unlocked attack: facing lerps toward the target yaw Atan2(5,15) at TrackingStrength
        // 0.9 — one tick of the lerp (no range gate, issue #127).
        float expected = MathF.Atan2(5f, 15f) * 0.9f / 60f;
        TestHelpers.AssertNear(expected, state.FacingYaw, tolerance: 0.0001f);
        Assert.Equal(0f, state.WarpSpeed); // no warp (too far + ServerAbility)
    }

    [Fact]
    public void Tick_TargetLock_InEngageRange_Rotates()
    {
        // Zone 2: dist ≤ AttackRange (the engage/tracking radius, warp gone) →
        // rotates toward target, never warps
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        player.FacingYaw = 0f; // facing +Z
        var npc = MakeIdleState(100);
        npc.PX = 0.75f; // slightly right of center
        npc.PZ = 1.75f; // dist ≈ 1.90 < engage range 2.0
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        // Target is only 23° off axis, so per-tick rotation is small (≈0.006 rad);
        // tick through the attack until it accumulates past the threshold.
        var input = TestHelpers.Input(activeSlot: 1);
        float yaw = 0f;
        for (int i = 0; i < 30 && yaw <= 0.05f; i++)
        {
            sim.Tick(new() { { 1, input }, { 100, default } });
            input = default;
            yaw = sim.GetState(1).FacingYaw;
        }

        // FacingYaw should have rotated toward the NPC (positive yaw = turning right)
        Assert.True(yaw > 0.05f,
            $"Expected FacingYaw > 0.05 (should rotate toward +X), got {yaw:F4}");
        Assert.Equal(0f, sim.GetState(1).WarpSpeed); // warp never initiates

    }
    [Fact]
    public void Tick_TargetLock_InAttackRange_Rotates()
    {
        // Zone 3: dist ≤ AttackRange → rotates toward target, no warp
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        player.FacingYaw = 0f; // facing +Z
        var npc = MakeIdleState(100);
        npc.PX = 1f; // to the right (+X), distance 1
        npc.PZ = 0f; // within AttackRange=2
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        var input = TestHelpers.Input(activeSlot: 1);
        sim.Tick(new() { { 1, input }, { 100, default } });

        var state = sim.GetState(1);
        // FacingYaw should have rotated toward the NPC
        Assert.True(state.FacingYaw > 0.01f,
            $"Expected FacingYaw > 0 (should rotate toward +X), got {state.FacingYaw:F4}");
        // No warp — already within attack range
        Assert.Equal(0f, state.WarpSpeed);
    }
    // ── Whiff commitment (ADR-0015): warp gone, attack at range is a commitment ──

    [Fact]
    public void AttackAtRange_NoWarp_EndsIdleWithLungeDrift()
    {
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.CombatDef;
        var player = MakeIdleState(1);
        var npc = MakeIdleState(100);
        npc.PZ = 5.5f; // beyond the move's engage range — no auto-approach exists
        sim.RegisterEntity(1, def, player);
        sim.RegisterEntity(100, def, npc);

        // 50 ticks: the attack starts anyway (commitment), whiffs, ends naturally
        var state = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 50);

        Assert.Equal(ActionState.Idle, state.State);
        Assert.Equal(0f, state.WarpSpeed);
        // Momentum-preserve (issue #115): EndAbility no longer zeroes velocity — the lunge
        // drift survives into Idle, where friction decays it.
        Assert.True(state.VZ > 0f, $"lunge drift must persist after the move: VZ={state.VZ}");
    }
}
