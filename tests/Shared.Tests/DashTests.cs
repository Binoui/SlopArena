using Xunit;
using System.Collections.Generic;

namespace SlopArena.Shared.Tests;

public class DashTests
{
    [Fact]
    public void Dash_StateTransitionsToIdleAfterDuration()
    {
        var arena = new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
        };
        var sim = new ServerSimulation(arena);
        var def = CharacterRegistry.Get(CharacterClass.Manki);
        var state = new CharacterState
        {
            EntityId = 1,
            PX = 0,
            PY = def.CapsuleHeight * 0.5f,
            PZ = 0,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = 2,
            AirDodgesLeft = 1,
        };
        sim.RegisterEntity(1, def, state);

        // Tick 0: press dash forward (MoveY=1 = forward in -Z)
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { Dash = true, MoveY = 1f } }
        });
        var t0 = sim.GetState(1);
        Assert.Equal(ActionState.Dashing, t0.State);
        Assert.True(t0.DashDurationTicks > 0);

        // Tick through dash duration (15 ticks total)
        for (int i = 1; i <= 15; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
            var s = sim.GetState(1);

            if (i < 15)
            {
                Assert.True(s.State == ActionState.Dashing,
                    $"tick {i}: expected Dashing but got {s.State}, DashDurationTicks={s.DashDurationTicks}");
            }
            else
            {
                // Tick 15: dash should have just ended
                Assert.Equal(ActionState.Idle, s.State);
                // Check residual velocity
                float residualSpeed = System.MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
                Assert.True(residualSpeed < 1f,
                    $"Expected VZ near 0 but got {s.VZ:F3} m/s (residual speed={residualSpeed:F3})");
            }
        }
    }

    [Fact]
    public void Dash_ResidualVelocityAfterDuration()
    {
        var arena = new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
        };
        var sim = new ServerSimulation(arena);
        var def = CharacterRegistry.Get(CharacterClass.Manki);
        var state = new CharacterState
        {
            EntityId = 1,
            PX = 0,
            PY = def.CapsuleHeight * 0.5f,
            PZ = 0,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = 2,
            AirDodgesLeft = 1,
        };
        sim.RegisterEntity(1, def, state);

        // press dash
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { Dash = true, MoveY = 1f } }
        });

        // Let dash complete, then measure residual velocity
        for (int i = 0; i < 20; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
        }

        var after = sim.GetState(1);
        float hSpeed = System.MathF.Sqrt(after.VX * after.VX + after.VZ * after.VZ);

        // After 5 post-dash ticks of ground friction, velocity should be nearly 0
        Assert.True(hSpeed < 5f,
            $"VZ={after.VZ:F3} — residual velocity too high after dash completes. " +
            $"State={after.State}, DashDurationTicks={after.DashDurationTicks}");
    }
}
