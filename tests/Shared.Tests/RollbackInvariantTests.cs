using System;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Property-based fuzz of the netplay/rollback client path: random inputs + random
/// packet loss/delay through NetplayHarness. Asserts the crash class (KeyNotFound on
/// defs/states, poisoned dictionaries, NaN drift) never fires, plus exact self-state
/// re-convergence after an idle settle tail (drops disabled).
///
/// Entity 1's ActiveSlot is forced to 0: the self entity's own attacks can diverge
/// between the local sim (mirror opponent) and the server (real opponent) on combo
/// chain — ReconcileWithServer refuses to patch a history suffix containing a Complex
/// tick (LocalTrack trusts the live sim through attacks), so an interrupted self-attack
/// would leave the live combo diverging forever. Entity 2's inputs are fully random —
/// they exercise the opponent prediction path (Complex routing, RawTrack,
/// re-registration, lunge, hits on entity 1 server-side).
///
/// On failure FsCheck prints a seed — pass it to the PositiveInt parameter to replay:
///   "Falsifiable, with seed: 12345"
/// </summary>
public class RollbackInvariantTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

    [Property(MaxTest = 1, EndSize = 300)]
    public void Rollback_DeepFuzz_NoCrash_Converges(PositiveInt seed)
    {
        var rng = new Random(seed.Item);
        var arena = TestHelpers.TestArena();

        int delayTicks = rng.Next(0, 4);
        int dropEvery = rng.Next(3) == 0 ? 0 : rng.Next(4, 9); // ~1/3 of runs lossless
        int traceTicks = 300;

        var h = new NetplayHarness(arena, Def, delayTicks, dropEvery);

        for (int tick = 0; tick < traceTicks; tick++)
        {
            var in1 = RandomInput(rng);
            in1.ActiveSlot = 0; // self never attacks (see class doc)
            var in2 = RandomInput(rng);
            h.Step(in1, in2);

            AssertFinite(h, tick);
        }

        // Idle settle tail with drops DISABLED: no new attacks can occur (idle inputs)
        // and hitstun is finite (≤ 60, asserted above), so a fresh packet with an
        // all-Predictable history suffix must eventually arrive and the reconcile +
        // replay re-converge the self state exactly. The 300-tick bound is far above
        // the worst case (hitstun 60 + delay ≤ 3 + delivery). Convergence is asserted
        // to happen, not timed — no fixed-tick assumption.
        h.SetDropsEnabled(false);
        bool converged = false;
        for (int i = 0; i < 300 && !converged; i++)
        {
            h.Step(default, default);
            AssertFinite(h, traceTicks + i);
            converged = NetplayHarness.IsSelfConverged(h);
        }

        Assert.True(converged, "self state failed to re-converge within 300 idle ticks");
    }

    private static void AssertFinite(NetplayHarness h, int tick)
    {
        foreach (var id in new[] { NetplayHarness.SelfId, NetplayHarness.OpponentId })
        {
            var s = h.ClientState(id);
            Assert.True(Enum.IsDefined(typeof(ActionState), s.State),
                $"Tick {tick}, entity {id}: invalid ActionState {s.State}");
            Assert.InRange(s.DamagePercent, 0, 999);
            Assert.True(
                float.IsFinite(s.PX) && float.IsFinite(s.PY) && float.IsFinite(s.PZ) &&
                float.IsFinite(s.VX) && float.IsFinite(s.VY) && float.IsFinite(s.VZ),
                $"Tick {tick}, entity {id}: non-finite position/velocity ({s.PX}, {s.PY}, {s.PZ}) / ({s.VX}, {s.VY}, {s.VZ})");
            Assert.True(s.HitstunTicks <= 60,
                $"Tick {tick}, entity {id}: HitstunTicks={s.HitstunTicks} > 60 (stuck)");
        }
    }

    /// <summary>Random valid InputState (mirrors SimulationInvariantTests.RandomInput).</summary>
    private static InputState RandomInput(Random rng)
    {
        var input = new InputState
        {
            MoveX = (float)(rng.NextDouble() * 2.0 - 1.0),
            MoveY = (float)(rng.NextDouble() * 2.0 - 1.0),
            Up = rng.Next(4) == 0,
            Down = rng.Next(4) == 0,
            Left = rng.Next(4) == 0,
            Right = rng.Next(4) == 0,
            Jump = rng.Next(8) == 0,
            Dash = rng.Next(8) == 0,
            Crouch = rng.Next(10) == 0,
            ActiveSlot = rng.Next(7) == 0 ? (byte)rng.Next(1, 7) : (byte)0,
            IsAiming = rng.Next(10) == 0,
        };
        input.FacingYaw = (short)rng.Next(-18000, 18001);
        input.AimYaw = (short)rng.Next(-18000, 18001);
        input.AimDistance = (ushort)rng.Next(0, 6501);
        input.AimPitch = (short)rng.Next(-9000, 9001);
        // Target entity 1, 2, or none — exercises mirror target-lock (guarded lookup).
        input.TargetEntityId = (byte)rng.Next(0, 3);
        return input;
    }
}
