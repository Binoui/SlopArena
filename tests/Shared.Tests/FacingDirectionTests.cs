using Xunit;

namespace SlopArena.Shared.Tests;

public class FacingDirectionTests
{
    [Fact]
    public void FacingYaw_Stable_AfterHit()
    {
        // Arrange: NPC facing +X (FacingYaw = PI/2), player at +Z in front
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        var py = TestHelpers.CombatGroundPY;

        var player = TestHelpers.PlayerState();
        player.PY = py;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = py;
        npc.FacingYaw = MathF.PI / 2f; // facing +X (perpendicular to knockback direction)
        sim.RegisterEntity(100, def, npc);

        // Act: Manki LMB stage 1 — hitbox triggers at tick ~12
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 14; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // Assert: hit connected, facing unchanged (victim frozen — hitstop, ADR-0012)
        var afterHit = sim.GetState(100);
        Assert.True(afterHit.HitstopTicks > 0, "victim should be frozen at connect");
        Assert.Equal(MathF.PI / 2f, afterHit.FacingYaw, 5); // unchanged

        // Freeze expiry is the relevant boundary; hitstun duration is derived from launch.
        while (sim.GetState(100).HitstopTicks > 0)
            sim.Tick(new() { { 1, default }, { 100, default } });
        var afterLaunch = sim.GetState(100);
        Assert.Equal(MathF.PI / 2f, afterLaunch.FacingYaw, 5); // unchanged
    }

    [Fact]
    public void FacingYaw_Stable_ThroughFullHitstun()
    {
        // Same setup, but tick through the entire hitstun duration + recovery
        // to verify facing doesn't snap when hitstun expires.
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
        // Tick through hitstun expiry and post-hitstun recovery (no input for NPC)
        for (int i = 0; i < 80; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // After hitstun expires and knockback decays, facing should still be original
        var state = sim.GetState(100);
        Assert.Equal(ActionState.Idle, state.State);
        Assert.Equal(MathF.PI / 2f, state.FacingYaw, 5);
    }
}
