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
                WalkSpeed = 5f,
                SprintSpeed = 8f,
                DashSpeed = 15f,
                AirAcceleration = 3f,
                JumpForce = 10f,
                Gravity = 20f,
                AirFloatGravity = 6f,
                DashDurationTicks = 15,
                DashCooldownTicks = 30,
                GroundFriction = 0.5f,
                AirFriction = 0.1f,
                MaxFallSpeed = 20f,
                MaxJumps = 2,
                JumpSquatTicks = 3,
            },
            CapsuleRadius = 0.3f,
            CapsuleHeight = 1.5f,
            HurtboxRadius = 0.4f,
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
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var state = MakeIdleState(1);
        state.PY = -30f; // below KillHeight (-20)
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

        var result = sim.GetState(1);
        // Cooldown prevented server ability creation, but data-driven attack path
        // still runs in SimulateTick — this is current expected behavior
        Assert.Equal(29, result.Cooldown0); // ticked down
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
    public void Tick_MankiQ_StaysAttackingForDuration()
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
        Assert.Equal(ActionState.Attacking, t0.State);

        for (int i = 1; i < 60; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
            var s = sim.GetState(1);
            bool shouldBeAttacking = i < 59;
            Assert.True((s.State == ActionState.Attacking) == shouldBeAttacking,
                $"tick {i}: expected {(shouldBeAttacking ? "Attacking" : "Idle")} but got {s.State}");
        }
    }
}
