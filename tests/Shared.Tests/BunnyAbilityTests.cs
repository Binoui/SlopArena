using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

public class BunnyAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.BunnyDef);

    // ── LMB (BunnyLmbCombo) ──

    [Fact]
    public void BunnyLmbCombo_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)1, t0.AttackSlot);
    }

    [Fact]
    public void BunnyLmbCombo_ChainsToStage2()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        for (int i = 0; i < 44; i++) TestHelpers.TickDefault(sim, 1);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        var afterChain = sim.GetState(1);
        Assert.Equal((byte)1, afterChain.ComboStage);
        Assert.Equal(ActionState.Attacking, afterChain.State);
    }

    [Fact]
    public void BunnyLmbCombo_ExpiresAfterFinalStage()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        for (int s = 0; s < 2; s++)
        {
            for (int i = 0; i < 44; i++) TestHelpers.TickDefault(sim, 1);
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        }
        for (int i = 0; i < 65; i++) TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    // ── Q (BunnyWhirlingCarrot) ──

    [Fact]
    public void BunnyWhirlingCarrot_ActivatesAimed()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 500), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
        Assert.True(t0.IsAiming);
    }

    [Fact]
    public void BunnyWhirlingCarrot_ThrowsProjectile()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 500);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim } });
        var rel = new InputState { ActiveSlot = 3, AimDistance = 500 };
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, rel } });
        Assert.Equal((byte)1, sim.GetState(1).ComboStage);
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void BunnyWhirlingCarrot_AppliesMark()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 50);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim }, { 100, default } });
        var rel = new InputState { ActiveSlot = 3, AimDistance = 50 };
        for (int i = 0; i < 90; i++) sim.Tick(new() { { 1, rel }, { 100, default } });
        var npcAfter = sim.GetState(100);
        Assert.True((npcAfter.StatusFlags & (1 << 2)) != 0, "NPC should have Marked status");
        Assert.True(npcAfter.StatusRemainingTicks > 0);
    }

    // ── E (BunnyFlipKick) ──

    [Fact]
    public void BunnyFlipKick_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);
    }

    [Fact]
    public void BunnyFlipKick_AppliesBackwardVelocity()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.FacingYaw = 0f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 2);
        Assert.True(t1.VZ < 0f, $"Expected VZ<0 (backward), got VZ={t1.VZ:F3}");
        Assert.True(t1.VY > 0f, $"Expected VY>0 (upward), got VY={t1.VY:F3}");
    }

    // ── R (BunnyDragonKick) ──

    [Fact]
    public void BunnyDragonKick_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)5, t0.AttackSlot);
    }

    [Fact]
    public void BunnyDragonKick_RecastCancelsEarly()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        for (int i = 0; i < 20; i++) TestHelpers.TickDefault(sim, 1);
        var cancel = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Idle, cancel.State);
    }

    [Fact]
    public void BunnyDragonKick_NormalDamageWithoutMark()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

        // Player lunges forward at 20m/s. After 10 ticks (hitbox spawn): ~3.3m.
        // Hitbox OffZ=2 → center at ~5.3m. Place NPC there.
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0,
            $"NPC should have taken damage, got {sim.GetState(100).DamagePercent}");
    }

    // ── F (BunnyJadeHare) ──

    [Fact]
    public void BunnyJadeHare_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)6, t0.AttackSlot);
    }

    [Fact]
    public void BunnyJadeHare_LocksInPlace()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.VX = 10f; state.VZ = 5f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 2);
        Assert.Equal(0f, t1.VX);
        Assert.Equal(0f, t1.VZ);
    }

    [Fact]
    public void BunnyJadeHare_PullsEnemies()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);
        var npc = TestHelpers.NpcState(2f, 0f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.VX = 0f; npc.VZ = 0f;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);
        var f = TestHelpers.Input(activeSlot: 6);
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, f }, { 100, default } });
        float dist = CombatMath.HorizontalDistance(0, 0, sim.GetState(100).PX, sim.GetState(100).PZ);
        Assert.True(dist < 2f, $"NPC should be pulled closer (<2m), distance={dist:F3}");
    }

    // ── Status ──

    [Fact]
    public void Status_TicksDownAndClears()
    {
        var s = new CharacterState { EntityId = 1, PX = 0, PY = 5f, PZ = 0, IsGrounded = false, State = ActionState.Idle, JumpsLeft = 2, AirDodgesLeft = 1, StatusFlags = (1 << 2), StatusRemainingTicks = 10 };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, TestHelpers.BunnyDef, s);
        for (int i = 0; i < 10; i++) TestHelpers.TickDefault(sim, 1);
        var a = sim.GetState(1);
        Assert.Equal(0u, a.StatusRemainingTicks);
        Assert.Equal((byte)0, a.StatusFlags);
    }

    [Fact]
    public void StatusFlags_DoesNotClearPrematurely()
    {
        var s = new CharacterState { EntityId = 1, PX = 0, PY = 5f, PZ = 0, IsGrounded = false, State = ActionState.Idle, JumpsLeft = 2, AirDodgesLeft = 1, StatusFlags = (1 << 2), StatusRemainingTicks = 10 };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, TestHelpers.BunnyDef, s);
        for (int i = 0; i < 5; i++) TestHelpers.TickDefault(sim, 1);
        var a = sim.GetState(1);
        Assert.Equal(5u, a.StatusRemainingTicks);
        Assert.Equal((byte)(1 << 2), a.StatusFlags);
    }
}
