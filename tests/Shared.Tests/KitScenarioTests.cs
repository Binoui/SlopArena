using System.IO;
using Xunit;
using Xunit.Sdk;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Base class for kit regression tests.
/// Provides AssertScenario and AssertGoldenScenario helpers.
/// </summary>
public abstract class KitScenarioTests
{
    /// <summary>
    /// Run a scenario and call its Assert on the final state.
    /// </summary>
    protected static void AssertScenario(KitScenario scenario)
    {
        var (state, _, _, _) = ScenarioRunner.Run(scenario);
        scenario.Assert(state);
    }

    /// <summary>
    /// Run a scenario and compare results against a golden file.
    /// Captures both mid-ability state (PlayerSnap) and final state (PlayerFinal).
    /// When REGENERATE_GOLDENS=1, writes the golden file instead of comparing.
    /// </summary>
    protected static void AssertGoldenScenario(KitScenario scenario)
    {
        var (playerFinal, npcFinal, playerSnap, npcSnap) = ScenarioRunner.Run(scenario);

        var snapshot = new StateSnapshot
        {
            Scenario = scenario.Name,
            TickCount = scenario.TotalTicks > 0 ? scenario.TotalTicks : 200,
            PlayerSnap = EntitySnapshot.FromState(playerSnap),
            NpcSnap = npcSnap.HasValue ? EntitySnapshot.FromState(npcSnap.Value) : null,
            PlayerFinal = EntitySnapshot.FromState(playerFinal),
            NpcFinal = npcFinal.HasValue ? EntitySnapshot.FromState(npcFinal.Value) : null,
        };

        string goldenPath = GoldenSnapshot.GoldenPath(scenario);

        if (ShouldRegenerateGoldens())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            string json = GoldenSnapshot.Serialize(snapshot);
            File.WriteAllText(goldenPath, json);
            return;
        }

        if (!File.Exists(goldenPath))
            throw new XunitException(
                $"Golden file not found: {goldenPath}\n" +
                $"Run with REGENERATE_GOLDENS=1 to create it.");

        string expectedJson = File.ReadAllText(goldenPath);
        var expected = GoldenSnapshot.Deserialize(expectedJson);

        AssertSnapshotEqual(expected, snapshot, goldenPath);
    }

    /// <summary>
    /// Compare two state snapshots field by field.
    /// </summary>
    private static void AssertSnapshotEqual(StateSnapshot expected, StateSnapshot actual, string path)
    {
        Assert.Equal(expected.Scenario, actual.Scenario);
        Assert.Equal(expected.TickCount, actual.TickCount);
        AssertEntityEqual(expected.PlayerSnap, actual.PlayerSnap, "player_snap");
        AssertEntityEqual(expected.PlayerFinal, actual.PlayerFinal, "player_final");
        AssertNullableEqual(expected.NpcSnap, actual.NpcSnap, "npc_snap");
        AssertNullableEqual(expected.NpcFinal, actual.NpcFinal, "npc_final");
    }

    private static void AssertNullableEqual(EntitySnapshot? expected, EntitySnapshot? actual, string label)
    {
        if (expected == null && actual == null) return;
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        AssertEntityEqual(expected!, actual!, label);
    }


    private static void AssertEntityEqual(EntitySnapshot expected, EntitySnapshot actual, string label)
    {
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.PX, actual.PX, 3);
        Assert.Equal(expected.PY, actual.PY, 3);
        Assert.Equal(expected.PZ, actual.PZ, 3);
        Assert.Equal(expected.VX, actual.VX, 3);
        Assert.Equal(expected.VY, actual.VY, 3);
        Assert.Equal(expected.VZ, actual.VZ, 3);
        Assert.Equal(expected.DamagePercent, actual.DamagePercent);
        Assert.Equal(expected.Deaths, actual.Deaths);
        Assert.Equal(expected.ComboStage, actual.ComboStage);
        Assert.Equal(expected.AttackSlot, actual.AttackSlot);
        Assert.Equal(expected.HitstunTicks, actual.HitstunTicks);
        Assert.Equal(expected.AirTimeTicks, actual.AirTimeTicks);
        Assert.Equal(expected.ChargeTicks, actual.ChargeTicks);
        Assert.Equal(expected.Cooldown0, actual.Cooldown0);
        Assert.Equal(expected.Cooldown1, actual.Cooldown1);
        Assert.Equal(expected.Cooldown2, actual.Cooldown2);
        Assert.Equal(expected.Cooldown3, actual.Cooldown3);
        Assert.Equal(expected.Cooldown4, actual.Cooldown4);
        Assert.Equal(expected.Cooldown5, actual.Cooldown5);
        Assert.Equal(expected.BuffRemainingTicks, actual.BuffRemainingTicks);
        Assert.Equal(expected.JumpsLeft, actual.JumpsLeft);
        Assert.Equal(expected.IsGrounded, actual.IsGrounded);
        Assert.Equal(expected.InvincibilityTicks, actual.InvincibilityTicks);
        Assert.Equal(expected.DashDurationTicks, actual.DashDurationTicks);
    }

    private static bool ShouldRegenerateGoldens()
    {
        string? val = System.Environment.GetEnvironmentVariable("REGENERATE_GOLDENS");
        return val == "1" || string.Equals(val, "true", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ground-level PY for Manki with floor at 0.
    /// </summary>
    protected static float MankiGpy => TestHelpers.MankiGroundPY;

    /// <summary>
    /// Ground-level PY for FightGuy with floor at 0.
    /// </summary>
    protected static float FightGuyGpy => TestHelpers.GroundPY(TestHelpers.FightGuyDef);

    /// <summary>
    /// Ground-level PY for Nilus with floor at 0.
    /// </summary>
    protected static float NilusGpy => TestHelpers.GroundPY(TestHelpers.NilusDef);
}
