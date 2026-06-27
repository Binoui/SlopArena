using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public static class TestHelpers
{
    public static CharacterDefinition MankiDef => CharacterRegistry.Get(CharacterClass.Manki);

    /// <summary>
    /// Create a player state at (x, z). PY defaults to 0 — physics tests
    /// that need grounded must set PY = floorY + def.CapsuleHeight * 0.5f.
    /// </summary>
    public static CharacterState PlayerState(float x = 0f, float z = 0f)
    {
        return new CharacterState
        {
            EntityId = 1,
            PX = x, PY = 0, PZ = z,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = 2,
            AirDodgesLeft = 1,
            FacingYaw = 0,
        };
    }

    public static CharacterState NpcState(float x = 0f, float z = 0f)
    {
        var s = PlayerState(x, z);
        s.EntityId = 100;
        return s;
    }

    /// <summary>
    /// Arena with a 1x1 heightmap at floorY. Callers must adjust
    /// entity PY to (floorY + def.CapsuleHeight * 0.5f) for groundedness.
    /// </summary>
    public static ArenaDefinition TestArena(float floorY = 0f)
    {
        int w = 200, h = 200;
        var data = new float[w * h];
        Array.Fill(data, floorY);
        return new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            SpawnPoints = new[]
            {
                new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 },
            },
            Heightmap = new ArenaHeightmap
            {
                Data = data,
                Width = w,
                Height = h,
                CellSize = 1f,
                OriginX = 0f,
                OriginZ = 0f,
            },
        };
    }

    /// <summary>
    /// Create a minimal input state, defaulting all fields to 0/false.
    /// </summary>
    public static InputState Input(byte activeSlot = 0, bool jump = false, bool dash = false,
        float moveX = 0f, float moveY = 0f, bool aiming = false)
    {
        return new InputState
        {
            ActiveSlot = activeSlot,
            Jump = jump,
            Dash = dash,
            MoveX = moveX,
            MoveY = moveY,
            IsAiming = aiming,
        };
    }

    /// <summary>
    /// Create a fresh simulation for the given arena.
    public static ServerSimulation MakeSim(ArenaDefinition? arena = null)
    {
        return new ServerSimulation(arena ?? TestArena());
    }

    /// <summary>
    /// Register entity 1 with the given definition and state.
    /// </summary>
    public static void RegisterPlayer(ServerSimulation sim, CharacterDefinition def, CharacterState state)
    {
        sim.RegisterEntity(1, def, state);
    }

    /// <summary>
    /// Register entity 100 (NPC) with the given definition and state.
    /// </summary>
    public static void RegisterNpc(ServerSimulation sim, CharacterDefinition def, CharacterState state)
    {
        sim.RegisterEntity(100, def, state);
    }

    /// <summary>
    /// Run N ticks, feeding firstInput on tick 0 and default input on ticks 1..N-1.
    /// Returns entity 1's state after tick N.
    /// </summary>
    public static CharacterState TickN(ServerSimulation sim, InputState firstInput, int totalTicks)
    {
        var inputs = new Dictionary<ulong, InputState> { { 1, firstInput } };
        for (int i = 0; i < totalTicks; i++)
        {
            if (i > 0) inputs[1] = default;
            sim.Tick(inputs);
        }
        return sim.GetState(1);
    }

    /// <summary>
    /// Run N ticks with all-default input. Returns entity 1's state.
    /// </summary>
    public static CharacterState TickDefault(ServerSimulation sim, int totalTicks)
    {
        return TickN(sim, default, totalTicks);
    }

    /// <summary>
    /// Compute the ground-level PY for a given def with the floor at floorY.
    /// </summary>
    public static float GroundPY(CharacterDefinition def, float floorY = 0f)
    {
        return floorY + def.CapsuleHeight * 0.5f;
    }

    /// <summary>
    /// Return the ground-level PY for Manki with floor at 0.
    /// </summary>
    public static float MankiGroundPY => 0f + MankiDef.CapsuleHeight * 0.5f; // 0.65

    /// <summary>
    /// Approximate float equality within tolerance.
    /// Use this instead of Assert.Equal(float, float, int) which checks decimal precision.
    /// </summary>
    public static void AssertNear(float expected, float actual, float tolerance = 0.001f)
    {
        float diff = Math.Abs(expected - actual);
        Assert.True(diff <= tolerance,
            $"Expected {expected:F6} ± {tolerance:F6} but got {actual:F6} (diff={diff:F6})");
    }
}
