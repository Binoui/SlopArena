using Xunit;

namespace SlopArena.Shared.Tests;

public class FacingDirectionTests
{
    [Fact]
    public void FacingYaw_AfterHit_FacesHitDirection()
    {
        // Arrange: NPC at +Z from the player; player faces +Z and hits it with Manki LMB.
        // The victim must TURN to face the hit direction (the attacker→target launch
        // direction) at connect — ADR-0019 hit-reaction facing.
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

        // Assert: hit connected, victim frozen, and now faces the hit direction
        // (attacker→target), NOT its prior facing.
        var afterHit = sim.GetState(100);
        var attacker = sim.GetState(1);
        Assert.True(afterHit.HitstopTicks > 0, "victim should be frozen at connect");
        float expected = MathF.Atan2(afterHit.PX - attacker.PX, afterHit.PZ - attacker.PZ);
        Assert.NotEqual(MathF.PI / 2f, afterHit.FacingYaw, 5); // facing changed
        Assert.Equal(expected, afterHit.FacingYaw, 3);

        // Freeze expiry is the relevant boundary; facing must persist into the launch.
        while (sim.GetState(100).HitstopTicks > 0)
            sim.Tick(new() { { 1, default }, { 100, default } });
        var afterLaunch = sim.GetState(100);
        Assert.Equal(expected, afterLaunch.FacingYaw, 3); // unchanged through launch
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
        float expected = MathF.Atan2(afterHit.PX - sim.GetState(1).PX, afterHit.PZ - sim.GetState(1).PZ);

        // Tick through hitstun expiry and post-hitstun recovery (no input for NPC)
        for (int i = 0; i < 80; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // After hitstun expires and knockback decays, facing stays at the hit direction.
        var state = sim.GetState(100);
        Assert.Equal(ActionState.Idle, state.State);
        Assert.Equal(expected, state.FacingYaw, 3);
    }
}
