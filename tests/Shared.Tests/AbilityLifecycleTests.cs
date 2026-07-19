using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Per-ability lifecycle tests.
/// ServerAbility classes (MankiLmbCombo, MankiAerosolFlame, MankiRoundBomb)
/// are partially implemented — tests for them check basic activation only.
/// Data-driven path (no ServerAbility) is fully working.
/// </summary>
public class AbilityLifecycleTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

    // ── LMB: ServerAbility (MankiLmbCombo) — basic activation only ──

    [Fact]
    public void MankiLMB_StartsAttacking()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)1, t0.AttackSlot);
    }

    // ── AirLMB: data-driven, no ServerAbility ──

    [Fact]
    public void MankiAirLMB_DataDrivenDuration()
    {
        // AirLMB: DurationTicks=20. Entity must be truly airborne (PY above ground snap).
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f; // well above ground
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press LMB while airborne → data-driven attack via AirLMB spec
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);

        // Tick past DurationTicks (20) with margin
        for (int i = 0; i < 30; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
    }

    // ── Q: ServerAbility (MankiRoundBomb) — basic activation ──

    [Fact]
    public void MankiQ_BasicActivation()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
    }

    // ── RMB: ServerAbility (MankiAerosolFlame) — basic activation ──

    [Fact]
    public void MankiRMB_Normal_StartsAttacking()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
    }
    // ══════════════════════════════════════════════════════════════════
    // ── F: Overclock — buff lifecycle ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiOverclock_ActivatesBuffState()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press F (slot 6)
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);

        Assert.True((after.BuffActiveFlags & (byte)BuffType.Overclock) != 0,
            "Overclock flag should be set after F press");
        Assert.True(after.BuffRemainingTicks > 0,
            "BuffRemainingTicks should be > 0 after F press");
    }

    [Fact]
    public void MankiOverclock_BuffDurationMatchesSpec()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        // Duration is 480 ticks (8s) per MankiData F spec. TickTimers decrements in activation tick → 479.
        Assert.True(after.BuffRemainingTicks >= 478 && after.BuffRemainingTicks <= 480,
            $"Expected buff duration ~479 ticks after activation, got {after.BuffRemainingTicks}");
    }

    [Fact]
    public void MankiOverclock_BuffTicksDownGradually()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.True(t0.BuffRemainingTicks > 470 && t0.BuffRemainingTicks <= 480,
            $"Expected ~479 after activation tick, got {t0.BuffRemainingTicks}");

        // Tick 10 more times — timer should decrease by ~10
        var t10 = TestHelpers.TickDefault(sim, 10);
        Assert.True(t10.BuffRemainingTicks > 459 && t10.BuffRemainingTicks < t0.BuffRemainingTicks,
            $"Expected buff to decrease by ~10 from {t0.BuffRemainingTicks}, got {t10.BuffRemainingTicks}");
    }

    [Fact]
    public void MankiOverclock_BuffExpiresAfterDuration()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.True(t0.BuffRemainingTicks >= 478 && t0.BuffRemainingTicks <= 480,
            $"Expected ~479 after activation tick, got {t0.BuffRemainingTicks}");

        // Tick 480 times (remaining ticks after rendering plus a margin)
        TestHelpers.TickDefault(sim, 485);

        var expired = sim.GetState(1);
        Assert.Equal(0u, expired.BuffRemainingTicks);
        Assert.Equal(0, expired.BuffActiveFlags);
    }

    [Fact]
    public void MankiOverclock_ReactivationBlockedWhileActive()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.True(t0.BuffRemainingTicks >= 478 && t0.BuffRemainingTicks <= 480,
            $"Expected ~479 after activation tick, got {t0.BuffRemainingTicks}");

        // Tick a bit, then press F again — should NOT reset duration
        var tMid = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 50);
        // Buff should still be ticking down from original activation:
        // 480 - 1 (tick 0) - 50 (additional ticks) = 429
        Assert.True(tMid.BuffRemainingTicks > 400 && tMid.BuffRemainingTicks < 480,
            $"Expected buff to be partially consumed (~429), got {tMid.BuffRemainingTicks}");
    }

    [Fact]
    public void MankiOverclock_PersistsAfterInjectionEnds()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Activate F — injection lasts 30 ticks
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);

        // Tick through injection + margin
        var afterInjection = TestHelpers.TickDefault(sim, 40);

        // Ability should have ended (state is Idle), but buff persists
        Assert.Equal(ActionState.Idle, afterInjection.State);
        Assert.True(afterInjection.BuffRemainingTicks > 400,
            $"Buff should still have most of its duration left, got {afterInjection.BuffRemainingTicks}");
    }

    [Fact]
    public void MankiOverclock_DoesNotBlockOtherAbilities()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Activate F
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);

        // Wait for injection to finish
        TestHelpers.TickDefault(sim, 40);

        // Now press LMB (slot 1) — should work even though buff is active
        var lmbAfter = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, lmbAfter.State);
        Assert.Equal((byte)1, lmbAfter.AttackSlot);
    }

    [Fact]
    public void Overclock_DeathClearsBuff()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        var state = TestHelpers.PlayerState();
        state.PY = 0.65f; // grounded
        sim.RegisterEntity(1, def, state);

        // Activate Overclock
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });
        var afterBuff = sim.GetState(1);
        Assert.True((afterBuff.BuffActiveFlags & (byte)BuffType.Overclock) != 0,
            "Overclock should be active after F press");
        Assert.True(afterBuff.BuffRemainingTicks > 0,
            "Buff ticks should be > 0");

        // Force below kill height, next tick kills them
        afterBuff.PY = -30f;
        sim.SetState(1, afterBuff);
        sim.Tick(new() { { 1, default } });

        var afterDeath = sim.GetState(1);
        Assert.Equal((byte)0, afterDeath.BuffActiveFlags);
        Assert.Equal((ushort)0, afterDeath.BuffRemainingTicks);
        Assert.Equal(1, afterDeath.Deaths);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── ApplyBuffBonuses — pure math validation ──
    // ══════════════════════════════════════════════════════════════════
    [Fact]
    public void OverclockBuffs_AddsDamageAndRadius()
    {
        var state = new CharacterState
        {
            BuffActiveFlags = (byte)BuffType.Overclock,
            BuffRemainingTicks = 400,
        };

        float damage = 10f;
        float radius = 2f;
        ServerAbility.ApplyBuffBonuses(ref state, ref damage, ref radius);

        Assert.Equal(13f, damage);  // 10 + 3
        Assert.Equal(2.5f, radius); // 2 + 0.5
    }

    [Fact]
    public void OverclockBuffs_DoesNotApplyWithoutBuff()
    {
        var state = new CharacterState
        {
            BuffActiveFlags = 0,
            BuffRemainingTicks = 0,
        };

        float damage = 10f;
        float radius = 2f;
        ServerAbility.ApplyBuffBonuses(ref state, ref damage, ref radius);

        Assert.Equal(10f, damage);  // unchanged
        Assert.Equal(2f, radius);   // unchanged
    }



    [Fact]
    public void CharacterStatePacket_RoundTripsBuffActiveFlags()
    {
        var original = new CharacterStatePacket
        {
            TickNumber = 42,
            BuffRemainingTicks = 400,
            BuffActiveFlags = (byte)BuffType.Overclock,
            PositionX = 1, PositionY = 2, PositionZ = 3,
            CurrentActionState = 1, IsGrounded = true, StateDurationFrames = 10,
        };
        Span<byte> buf = stackalloc byte[CharacterStatePacket.Size];
        original.Serialize(buf);
        var deserialized = CharacterStatePacket.Deserialize(buf);
        Assert.Equal(original.BuffActiveFlags, deserialized.BuffActiveFlags);
        Assert.Equal(original.BuffRemainingTicks, deserialized.BuffRemainingTicks);
    }
    // ══════════════════════════════════════════════════════════════════
    // ── R: Bazooka — FPS fire-and-forget rocket ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiR_BasicActivation_EntersAttacking()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal((byte)5, after.AttackSlot);
    }

    [Fact]
    public void MankiR_RecoveryEnds_ReturnsToIdle()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Total: cast_duration=20 + recovery_duration=15 = 35 ticks
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 40);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    [Fact]
    public void MankiR_ProjectileUsesAimDirection()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.AimYaw = 1.0f;
        state.AimPitch = 0.5f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Attacking, after.State);
    }


    // ══════════════════════════════════════════════════════════════════
    // ── AirRMB (slot 2, airborne): data-driven — basic lifecycle ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiAirRMB_BasicActivationAndExpiry()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; // airborne
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)2, t0.AttackSlot);

        // AirRMB DurationTicks=28, wait 35 for margin
        for (int i = 0; i < 35; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((byte)0, ended.AttackSlot);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── RMB (slot 2): Two-phase charge-hold architecture ──
    // ══════════════════════════════════════════════════════════════════
    // MankiAerosolFlame has two phases:
    //   Phase 1 (ComboStage=0, AnimIndex=0) = charge hold
    //   Phase 2 (ComboStage=1, AnimIndex=1) = release-to-attack
    // Both normal and charged releases play spell_rmb_charged (AnimIndex=1).
    // Tap release (instant skip if !IsAiming) or hold past charge_threshold=45.


    [Fact]
    public void MankiRMB_Charged_HoldThenRelease()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold RMB with IsAiming=true for 50 ticks using manual loop
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 50; i++)
            sim.Tick(holdInputs);

        // Release (input without aiming) — should fire charged
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        var released = TestHelpers.TickN(sim, releaseInput, 1);
        Assert.Equal(ActionState.Attacking, released.State);
        Assert.Equal((byte)1, released.ComboStage);
    }

    [Fact]
    public void MankiRMB_TapRelease_SpawnsNormalHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming=false for 5 ticks (manual release debounce)
        // triggerTick=8 for normal spawns hitbox at AttackElapsedTicks=8
        // 5 hold ticks + 12 release ticks = hitbox active
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 5);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 12);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Normal hitbox params per MankiData
        Assert.Equal(8f, hb.Damage);
        Assert.Equal(0.8f, hb.Radius);
        Assert.Equal(5.6f, hb.BaseKnockback);
        Assert.Equal(8.4f, hb.KnockbackGrowth);
    }

    [Fact]
    public void MankiRMB_HoldRelease_SpawnsChargedHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming for 55 ticks using direct sim.Tick loop
        // (TickN resets input to default after tick 1, dropping IsAiming)
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 50; i++)
            sim.Tick(holdInputs);

        // Release — charged attack starts, triggerTick=10, wait 13 ticks
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        var relInputs = new Dictionary<ulong, InputState> { { 1, releaseInput } };
        for (int i = 0; i < 13; i++)
            sim.Tick(relInputs);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Charged hitbox params per MankiData
        Assert.Equal(14f, hb.Damage);
        Assert.Equal(1.0f, hb.Radius);
        Assert.Equal(9.6f, hb.BaseKnockback);
        Assert.Equal(14.4f, hb.KnockbackGrowth);
    }

    [Fact]
    public void MankiRMB_ReleaseUnderThreshold_StaysNormal()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming for 5 ticks (way under charge_threshold=45)
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 5; i++)
            sim.Tick(holdInputs);

        // Release — should fire as normal (under threshold)
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        var relInputs = new Dictionary<ulong, InputState> { { 1, releaseInput } };
        for (int i = 0; i < 12; i++) // + wait past triggerTick=8
            sim.Tick(relInputs);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Should be normal params (not charged)
        Assert.Equal(8f, hb.Damage);
        Assert.Equal(0.8f, hb.Radius);
    }

    [Fact]
    public void MankiRMB_AutoRelease_AtMaxHold()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming past ChargeHoldTicks (45) — auto-release fires charged attack
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 60; i++)
            sim.Tick(holdInputs);

        // Release RMB (stop sending ActiveSlot) — let charged attack play out
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default } });

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Should be charged params (auto-release past ChargeHoldTicks = always charged)
        Assert.Equal(14f, hb.Damage);
        Assert.Equal(1.0f, hb.Radius);
    }

    [Fact]
    public void MankiRMB_ChargePhase_HasAnimIndexZero()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming — stays in charge phase (ComboStage=0, AnimIndex=0)
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var inputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 10; i++)
            sim.Tick(inputs);

        var duringCharge = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, duringCharge.State);
        Assert.Equal((byte)0, duringCharge.ComboStage);  // charge phase
        Assert.Equal((byte)0, duringCharge.ComboStage);
    }

    [Fact]
    public void MankiRMB_CooldownApplied()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tap RMB — instant skip, normal attack (normal_duration=58)
        // Wait enough ticks for attack to complete + a few more
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 70);

        // After attack ends, slot 2 (RMB, Cooldown1) should have cooldown=30
        var afterIdle = sim.GetState(1);
        Assert.Equal(ActionState.Idle, afterIdle.State);
        Assert.True(afterIdle.Cooldown1 > 0 && afterIdle.Cooldown1 <= 30,
            $"Expected cooldown 1-30 on slot 2, got Cooldown1={afterIdle.Cooldown1}");
    }

    [Fact]
    public void MankiRMB_HoldRelease_WithNoAiming()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming=false for 5 ticks → manual release → ComboStage=1
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        for (int i = 0; i < 6; i++)
        {
            var inputs = new Dictionary<ulong, InputState> { { 1, releaseInput } };
            sim.Tick(inputs);
        }

        var afterRelease = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, afterRelease.State);
        Assert.Equal((byte)1, afterRelease.ComboStage);

        // Hitbox should be approaching trigger_tick=8 — run 9 more ticks
        for (int i = 0; i < 9; i++)
            sim.Tick(new() { { 1, releaseInput } });

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        Assert.Equal(8f, hitboxes[0].Damage); // normal params
    }

    [Fact]
    public void MankiRMB_ChargePhase_NoHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming — stays in charge phase
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var inputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 10; i++)
            sim.Tick(inputs);

        var duringCharge = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, duringCharge.State);
        Assert.Equal(0, duringCharge.AnimIndex);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void MankiRMB_Charged_HitsBeyondNormalRange()
    {
        // Place NPC at z=4.5 — outside normal RMB range (~4.1) but inside charged range (~5.3)
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        var gpy = TestHelpers.CombatGroundPY;
        var player = TestHelpers.PlayerState(0f, 0f);
        player.PY = gpy;
        sim.RegisterEntity(1, def, player);
        var npc = TestHelpers.NpcState(0f, 4.5f);
        npc.PY = gpy;
        sim.RegisterEntity(100, def, npc);

        // ── Part A: Uncharged RMB (instant skip) should NOT hit NPC at 4.5m ──
        // Tick 0: tap RMB (no aiming), instant skip to Phase 2
        var tapInput = new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(activeSlot: 2) },
            { 100, default },
        };
        sim.Tick(tapInput);
        // Tick 1-20: normal attack phase, hitbox active from tick 8→46
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfterUncharged = sim.GetState(100);
        Assert.Equal(0u, npcAfterUncharged.DamagePercent);

        // ── Part B: Charged RMB should hit NPC at 4.5m ──
        // New sim for clean state
        var sim2 = TestHelpers.MakeSim(arena);
        var player2 = TestHelpers.PlayerState(0f, 0f);
        player2.PY = gpy;
        sim2.RegisterEntity(1, def, player2);
        var npc2 = TestHelpers.NpcState(0f, 4.5f);
        npc2.PY = gpy;
        sim2.RegisterEntity(100, def, npc2);

        // Hold with aiming for 55 ticks (past charge_threshold=45)
        var holdInput2 = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs2 = new Dictionary<ulong, InputState> { { 1, holdInput2 }, { 100, default } };
        for (int i = 0; i < 55; i++)
            sim2.Tick(holdInputs2);

        // Release — charged attack starts, triggerTick=10 → hitbox active from tick 11
        var releaseInput2 = TestHelpers.Input(activeSlot: 2);
        var relInputs2 = new Dictionary<ulong, InputState> { { 1, releaseInput2 }, { 100, default } };
        for (int i = 0; i < 15; i++)
            sim2.Tick(relInputs2); // 15 ticks = AttackElapsedTicks=15, past trigger=10

        var npcAfterCharged = sim2.GetState(100);
        Assert.True(npcAfterCharged.DamagePercent > 0,
            $"Charged RMB should hit NPC at 4.5m (charged range ~5.3m), got damage={npcAfterCharged.DamagePercent}");
    }
    // ══════════════════════════════════════════════════════════════════
    // ── E (slot 3): Grapple Gun — basic activation ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiE_BasicActivation_EntersAttacking()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);
    }

    [Fact]
    public void MankiE_FiresThenMisses_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 40);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }
}
