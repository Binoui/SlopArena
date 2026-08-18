using Xunit;

namespace SlopArena.Shared.Tests;

public class FacingDirectionTests
{
    [Fact]
    public void FacingYaw_AfterHit_FacesAttacker()
    {
        // Arrange: NPC at +Z from the player; player faces +Z and hits it with Manki LMB.
        // The victim must TURN to face the attacker (the direction the hit came from,
        // opposite the launch) at connect — hit-reaction facing.
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        var py = TestHelpers.CombatGroundPY;

        var player = TestHelpers.PlayerState();
        player.PY = py;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = py;
        npc.FacingYaw = MathF.PI / 2f; // was facing +X — must be overridden by the hit
        sim.RegisterEntity(100, def, npc);

        // Act: Manki LMB stage 1 — hitbox triggers at tick ~12
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 14; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // Assert: hit connected, victim frozen, and now faces the attacker
        // (victim→attacker direction), NOT its prior facing.
        var afterHit = sim.GetState(100);
        var attacker = sim.GetState(1);
        Assert.True(afterHit.HitstopTicks > 0, "victim should be frozen at connect");
        float expected = MathF.Atan2(attacker.PX - afterHit.PX, attacker.PZ - afterHit.PZ);
        Assert.NotEqual(MathF.PI / 2f, afterHit.FacingYaw, 5); // facing changed
        AssertNearAngle(expected, afterHit.FacingYaw);

        // Freeze expiry is the relevant boundary; facing must persist into the launch.
        while (sim.GetState(100).HitstopTicks > 0)
            sim.Tick(new() { { 1, default }, { 100, default } });
        var afterLaunch = sim.GetState(100);
        AssertNearAngle(expected, afterLaunch.FacingYaw); // unchanged through launch
    }

    /// <summary>Facing yaws are mod 2π (π ≡ −π); compare wrapped angle deltas, not raw values.</summary>
    private static void AssertNearAngle(float a, float b)
    {
        float diff = MathF.Abs(a - b);
        while (diff > MathF.PI) diff -= 2f * MathF.PI;
        Assert.True(diff < 0.003f, $"facing {b:F3} should match expected {a:F3} (diff {diff:F3})");
    }

    [Fact]
    public void FacingYaw_Stable_ThroughFullHitstun()
    {
        // Same setup — verify the hit-facing persists through the entire hitstun
        // duration + recovery (doesn't snap away when hitstun expires).
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        var py = TestHelpers.CombatGroundPY;

        var player = TestHelpers.PlayerState();
        player.PY = py;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = py;
        npc.FacingYaw = MathF.PI / 2f;
        sim.RegisterEntity(100, def, npc);

        // Hit
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        var afterHit = sim.GetState(100);
        float expected = MathF.Atan2(sim.GetState(1).PX - afterHit.PX, sim.GetState(1).PZ - afterHit.PZ);

        // Tick through hitstun expiry and post-hitstun recovery (no input for NPC)
        for (int i = 0; i < 80; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // After hitstun expires and knockback decays, facing stays at the hit direction
        // (toward the attacker).
        var state = sim.GetState(100);
        Assert.Equal(ActionState.Idle, state.State);
        AssertNearAngle(expected, state.FacingYaw);
    }
}
