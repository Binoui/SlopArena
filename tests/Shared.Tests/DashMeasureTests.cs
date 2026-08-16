using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public class DashMeasureTests
{
    private readonly ITestOutputHelper _o;
    public DashMeasureTests(ITestOutputHelper o) => _o = o;

    [Fact]
    public void MeasureGroundedAndAerialDashLength()
    {
        var chars = new (string name, CharacterDefinition def)[]
        {
            ("Manki", TestHelpers.MankiDef),
            ("FightGuy", TestHelpers.FightGuyDef),
            ("Kistu", TestHelpers.KistuDef),
            ("Nilus", TestHelpers.NilusDef),
        };

        foreach (var (name, def) in chars)
        {
            var m = def.Movement;
            _o.WriteLine($"\n=== {name} === run={m.RunSpeed} dash={m.DashSpeed} dashDur={m.DashDurationTicks} ({m.DashDurationTicks/60f:F2}s) cooldown={m.DashCooldownTicks}");

            // Grounded dash
            float gDist = MeasureDash(def, airborne: false);
            // Aerial dash
            float aDist = MeasureDash(def, airborne: true);

            _o.WriteLine($"  grounded dash length: {gDist:F2} m");
            _o.WriteLine($"  aerial   dash length: {aDist:F2} m");
        }
    }

    private static float MeasureDash(CharacterDefinition def, bool airborne)
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        if (airborne)
        {
            state.PY = TestHelpers.GroundPY(def) + 5f;
            state.IsGrounded = false;
        }
        else
        {
            state.PY = TestHelpers.GroundPY(def);
        }
        state.VX = 0f; state.VZ = 0f;
        TestHelpers.RegisterPlayer(sim, def, state);

        float startZ = sim.GetState(1).PZ;
        sim.Tick(new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(dash: true, moveY: 1f) } });
        // Tick well past dash duration + expiry hard-stop
        int total = def.Movement.DashDurationTicks + 30;
        for (int i = 1; i < total; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default(InputState) } });
        var s = sim.GetState(1);
        return s.PZ - startZ;
    }
}
