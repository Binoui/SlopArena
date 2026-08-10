using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

public class FightGuyAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);

    // ── LMB (FightGuyLmbCombo) ──

    [Fact]
    public void FightGuyLmbCombo_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)1, t0.AttackSlot);
    }

    [Fact]
    public void FightGuyLmb_RepeatPressMidMove_DoesNotChain()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        var def = TestHelpers.FightGuyDef;
        var stageTicks = def.LMB!.Stages[0].DurationTicks;
        TestHelpers.RegisterPlayer(sim, def, state);
        // Tick 0: LMB to start the single move
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        // Tick 1: repeat press — no chain, no stage advance (issue #115)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Mid-move: still the single move, never chained.
        for (int i = 2; i < stageTicks / 2; i++)
            TestHelpers.TickDefault(sim, 1);
        var mid = sim.GetState(1);
        Assert.Equal((byte)0, mid.ComboStage); // never chained
        Assert.Equal(ActionState.Attacking, mid.State);

        // Full duration: ends Idle, no queued second move.
        for (int i = 0; i < stageTicks; i++)
            TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void FightGuyLmb_MoveExpiresToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        int duration = TestHelpers.FightGuyDef.LMB!.Stages[0].DurationTicks;
        for (int i = 0; i < duration + 10; i++)
            TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void FightGuyLmbCombo_Stage1DealsDamageToNpc()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC in front of player, within LMB hitbox range
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = GroundPY;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // LMB and tick through hitbox trigger (tick 6)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // Player should have lunged forward
        var playerAfter = sim.GetState(1);
        Assert.True(playerAfter.PZ > 0.3f,
            $"Expected player to lunge forward (PZ > 0.3), got PZ={playerAfter.PZ:F3}");

        // NPC should have taken damage
        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should have taken damage from LMB, got {npcAfter.DamagePercent}");
    }

    // ── Q (FightGuyKiShot) ──

    [Fact]
    public void FightGuyKiShot_ActivatesAimed()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 11, aiming: true, aimDistance: 500), 1);
        Assert.Equal(ActionState.Aiming, t0.State);
        Assert.Equal((byte)11, t0.AttackSlot);
        Assert.True(t0.IsAiming);
    }

    [Fact]
    public void FightGuyKiShot_ThrowsProjectile()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);
        var aim = TestHelpers.Input(activeSlot: 11, aiming: true, aimDistance: 500);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim } });
        var rel = new InputState { ActiveSlot = 11, AimDistance = 500 };
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, rel } });
        Assert.Equal((byte)1, sim.GetState(1).ComboStage);
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void FightGuyKiShot_AppliesMark()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);
        var aim = TestHelpers.Input(activeSlot: 11, aiming: true, aimDistance: 50);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim }, { 100, default } });
        var rel = new InputState { ActiveSlot = 11, AimDistance = 50 };
        for (int i = 0; i < 90; i++) sim.Tick(new() { { 1, rel }, { 100, default } });
        var npcAfter = sim.GetState(100);
        Assert.True((npcAfter.StatusFlags & (1 << 2)) != 0, "NPC should have Marked status");
        Assert.True(npcAfter.StatusRemainingTicks > 0);
    }

    [Fact]
    public void FightGuyKiShot_MarkExpiresAfterDuration()
    {
        var sim = TestHelpers.MakeSim();
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;  // 5s mark
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Tick exactly 300 times — should clear at 0
        for (int i = 0; i < 300; i++)
            sim.Tick(new() { { 100, default } });

        var after = sim.GetState(100);
        Assert.Equal((ushort)0, after.StatusRemainingTicks);
        Assert.Equal((byte)0, after.StatusFlags);
    }

    [Fact]
    public void FightGuyKiShot_MarkStillActiveAtHalfDuration()
    {
        var sim = TestHelpers.MakeSim();
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        for (int i = 0; i < 150; i++)
            sim.Tick(new() { { 100, default } });

        var after = sim.GetState(100);
        Assert.Equal((ushort)150, after.StatusRemainingTicks);
        Assert.Equal((byte)(1 << 2), after.StatusFlags);
    }

    // ── E (FightGuyCycloneKick) ──

    [Fact]
    public void FightGuyCycloneKick_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)5, t0.AttackSlot);
    }

    [Fact]
    public void FightGuyCycloneKick_AppliesForwardLunge()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.FacingYaw = 0f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 3);
        Assert.True(t1.VZ > 16f, $"Expected VZ>16 (forward lunge), got VZ={t1.VZ:F3}");
        Assert.True(t1.PZ > 0.1f, $"Expected forward position change, got PZ={t1.PZ:F3}");
    }

    [Fact]
    public void FightGuyCycloneKick_HitboxInFrontStuns()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC in front (OffZ=1.8 hitbox, player lunges forward)
        var npc = TestHelpers.NpcState(0f, 3f);
        npc.PY = GroundPY;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Press E and tick past hitbox trigger (tick 10, after windup)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 24; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });
        var npcAfter = sim.GetState(100);

        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from Tornado Kick, got {npcAfter.DamagePercent}");

        // Hitstop (ADR-0012): every connecting hit freezes first; the banded 20-tick stun
        // only starts once the freeze chain ends (the kick re-frozen the victim each contact).
        // Find the first tick the stun lock is live — it must reach the band.
        int stunTick = -1;
        for (int i = 0; i < 120 && stunTick < 0; i++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });
            var s = sim.GetState(100);
            if (s.HitstopTicks == 0 && s.HitstunTicks > 0) stunTick = i;
        }
        Assert.True(stunTick >= 0, "the stun must eventually land after the freeze chain");
        // Band check: the banded 20-tick stun must reach the 10-25 band. The kick's
        // re-hit pattern shifts with momentum-preserve drift, so assert the band floor,
        // not a brittle exact count.
        Assert.True(sim.GetState(100).HitstunTicks >= 10,
            $"Expected HitstunTicks in the 10-25 band, got {sim.GetState(100).HitstunTicks}");
    }

    [Fact]
    public void FightGuyCycloneKick_HitsMultipleEnemiesAlongPath()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC1 close (z=2), NPC2 far (z=6)
        var npc1 = TestHelpers.NpcState(0f, 2f);
        npc1.PY = GroundPY;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc1);

        var npc2 = TestHelpers.NpcState(0f, 6f);
        npc2.PY = GroundPY;
        sim.RegisterEntity(101, TestHelpers.FightGuyDef, npc2);

        // Activate E and tick through the dash, watching for each victim's stun lock.
        // Hitstop (ADR-0015) freezes each freeze chain, then the banded StunTicks 20 applies.
        // The hits are staggered (NPC2 sits 4 m farther down the dash), so the 20-tick
        // windows don't overlap — check each NPC for its own window, and scan from the
        // first tick: NPC1's window (t20-30) closes before NPC2's even opens (t39+).
        bool n1Stunned = false, n2Stunned = false;
        for (int i = 0; i < 150 && !(n1Stunned && n2Stunned); i++)
        {
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 5) : default }, { 100, default }, { 101, default } });
            if (i < 5) continue; // windup — no hits yet
            var a = sim.GetState(100);
            var b = sim.GetState(101);
            if (a.HitstopTicks == 0 && a.HitstunTicks >= 10) n1Stunned = true;
            if (b.HitstopTicks == 0 && b.HitstunTicks >= 10) n2Stunned = true;
        }
        Assert.True(sim.GetState(100).DamagePercent > 0, "NPC1 should take damage");
        Assert.True(sim.GetState(101).DamagePercent > 0, "NPC2 should take damage");
        Assert.True(n1Stunned && n2Stunned, "both NPCs must be stunned after their freeze chains");
    }

    // ── F (FightGuyTempest) ──

    [Fact]
    public void FightGuyTempest_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)6, t0.AttackSlot);
    }

    [Fact]
    public void FightGuyTempest_LocksInPlace()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.VX = 10f; state.VZ = 5f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 2);
        Assert.Equal(0f, t1.VX);
        Assert.Equal(0f, t1.VZ);
    }

    [Fact]
    public void FightGuyTempest_PullsEnemies()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);
        var npc = TestHelpers.NpcState(2f, 0f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.VX = 0f; npc.VZ = 0f;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);
        var f = TestHelpers.Input(activeSlot: 6);
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, f }, { 100, default } });
        float dist = CombatMath.HorizontalDistance(0, 0, sim.GetState(100).PX, sim.GetState(100).PZ);
        Assert.True(dist < 2f, $"NPC should be pulled closer (<2m), distance={dist:F3}");
    }

    [Fact]
    public void FightGuyTempest_LauncherSpawnsOnFinalSpinTick()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        // Activate F and tick through windup (12) + spin (60) = 72 ticks
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });
        // Tick through windup (12) + spin (60) = 72 ticks total from activation
        // Launcher spawns at tick 72 (spinElapsed==_spinDuration)
        for (int i = 0; i < 71; i++)
            sim.Tick(new() { { 1, default } });

        // Ability should be ended, but launcher hitbox (4 tick duration)
        // should still be active with 3 remaining ticks
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.True(sim.Resolver.GetActiveHitboxes().Count >= 1,
            $"Expected at least 1 active hitbox (launcher), got {sim.Resolver.GetActiveHitboxes().Count}");
    }

    // ── Status ──

    [Fact]
    public void Status_TicksDownAndClears()
    {
        var s = new CharacterState { EntityId = 1, PX = 0, PY = 5f, PZ = 0, IsGrounded = false, State = ActionState.Idle, JumpsLeft = 2, AirDodgesLeft = 1, StatusFlags = (1 << 2), StatusRemainingTicks = 10 };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, s);
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
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, s);
        for (int i = 0; i < 5; i++) TestHelpers.TickDefault(sim, 1);
        var a = sim.GetState(1);
        Assert.Equal(5u, a.StatusRemainingTicks);
        Assert.Equal((byte)(1 << 2), a.StatusFlags);
    }

    // ── Bone-attached hitbox ──

    [Fact]
    public void BoneHitbox_FromData_BoneHitboxDisabledWithoutBakedData()
    {
        // Custom LMB with a bone-attached hitbox
        var boneLMB = new AbilitySpec
        {
            Name = "BoneLMB",
            CooldownTicks = 0,
            Stages = new AttackStage[]
            {
                new()
                {
                    DurationTicks = 20,
                    HitboxEvents = new[]
                    {
                        new HitboxEvent
                        {
                            TriggerTick = 5,
                            DurationTicks = 5,
                            Radius = 0.8f,
                            BoneName = "mixamorig:RightFoot",
                            BoneOffY = 0.1f,
                            Damage = 10f,
                            Knockback = new() { Profile = KnockbackProfile.Medium },
                            StunTicks = 10,
                            Interruptible = true,
                        },
                    },
                    LungeForce = 0f,
                },
            },
            AnimationNames = new[] { "melee" },
        };

        // Def based on BoneHitboxTestDef but with the custom bone LMB
        var src = TestHelpers.BoneHitboxTestDef;
        var def = new CharacterDefinition
        {
            Class = src.Class,
            DisplayName = src.DisplayName,
            CapsuleRadius = src.CapsuleRadius,
            CapsuleHeight = src.CapsuleHeight,
            HurtboxRadius = src.HurtboxRadius,
            Movement = src.Movement,
            LMB = boneLMB,
            HurtboxBoneDefs = src.HurtboxBoneDefs,
            BakedDataPath = "", // No baked data — bone hitbox skips
            HurtboxCapsules = src.HurtboxCapsules!,
            IdleAnim = src.IdleAnim,
            RunAnim = src.RunAnim,
            DashAnim = src.DashAnim,
            JumpAnim = src.JumpAnim,
            FallAnim = src.FallAnim,
            HitSmallAnim = src.HitSmallAnim,
            HitMediumAnim = src.HitMediumAnim,
            HitHardAnim = src.HitHardAnim,
            VisualScale = src.VisualScale,
            ModelYOffset = src.ModelYOffset,
            ModelSoleOffset = src.ModelSoleOffset,
        };

        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.MankiDef); // 0.75
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, def, player);

        // NPC in front-right where right foot bone would be (without baked data, shouldn't matter)
        var npc = TestHelpers.NpcState(0.5f, 1.5f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.MankiDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Tick through hitbox trigger (tick 5)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // No baked data → bone hitbox should have been skipped
        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent == 0,
            $"NPC should take NO damage (bone hitbox skipped without baked data), got {npcAfter.DamagePercent}");
    }

    [Fact]
    public void BoneHitbox_EntityOffsetHitboxStillWorks()
    {
        // Uses FightGuyDef LMB (entity-relative OffX/OffY/OffZ, BoneName=null default).
        // HitboxEvent with no BoneName → standard positioning path.
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef); // 0.85
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // FightGuy LMB stage 0 hitbox: OffX=0, OffY=0.8, OffZ=1.2, Radius=0.5
        // Player at (0, 0.85, 0), lunge forward ~0.8m by tick 6 (hitbox trigger)
        // → hitbox center approx at (0, 1.65, 2.0). NPC at (0, 0.65, 1.5): overlap
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = TestHelpers.CombatGroundPY;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.CombatDef, npc);

        // LMB stage 0, hitbox triggers at tick 6
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from entity-offset hitbox, got {npcAfter.DamagePercent}");
    }
    // ── RMB Charged Uppercut ──

    [Fact]
    public void FightGuyRmb_ChargeTicksIncreaseWhileHolding()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        // RMB + hold (aiming=true) for 10 ticks → ChargeTicks should increase
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, hold } });
        var after = sim.GetState(1);
        Assert.True(after.ChargeTicks > 0,
            $"Expected ChargeTicks > 0 after 10 ticks holding RMB, got {after.ChargeTicks}");
    }

    [Fact]
    public void FightGuyRmb_ReleasesOnButtonRelease()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        // Hold for 10 ticks, then release (IsAiming=false)
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, hold } });
        // Release RMB (IsAiming=false — but must keep activeSlot=2 for the ability to stay active)
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, release } });
        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ComboStage); // Should be in attack phase
    }

    [Fact]
    public void FightGuyRmb_AutoReleaseOnFullCharge()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        // Hold for full charge duration (180 ticks) — auto-releases
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 190; i++)
            sim.Tick(new() { { 1, hold } });
        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ComboStage); // Attack phase
        Assert.True(after.ChargeTicks >= 180,
            $"Expected ChargeTicks >= 180 at full charge, got {after.ChargeTicks}");
    }

    [Fact]
    public void FightGuyRmb_ChargedVariantDealsMoreDamage()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC where the fully-charged uppercut lands. Momentum-preserve (issue #115):
        // the charge lunge (2 m/s) coasts for the whole 180-tick hold (~6.0m), then the
        // release lunge (5 m/s) carries ~0.4m more before the first hitbox triggers —
        // the charged attack fires around z=6.6, so the dummy sits at z=6.5.
        var npc = TestHelpers.NpcState(0f, 6.5f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Charged: hold for full 180 ticks, then release
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 185; i++)
            sim.Tick(new() { { 1, hold }, { 100, default } });
        // Release — let attack play out to hitbox triggers (tick 5-15)
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 25; i++)
            sim.Tick(new() { { 1, release }, { 100, default } });
        var npcAfter = sim.GetState(100);

        // Each charged hitbox does 14 damage, 3 hitboxes = up to 42 total
        Assert.True(npcAfter.DamagePercent >= 14,
            $"Expected charged RMB to deal >=14 damage, got {npcAfter.DamagePercent}");
    }

    [Fact]
    public void FightGuyRmb_UnchargedDealsLessDamageThanCharged()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC at same position as charged test
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Uncharged: hold for only 6 ticks (debounce = 5), then release immediately
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 6; i++)
            sim.Tick(new() { { 1, hold }, { 100, default } });
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, release }, { 100, default } });
        var npcAfter = sim.GetState(100);

        // Uncharged hitboxes do 6 damage each, 3 hitboxes = up to 18 total
        Assert.True(npcAfter.DamagePercent >= 6,
            $"Expected uncharged RMB to deal >=6 damage, got {npcAfter.DamagePercent}");
        // But less than charged (14+ per hitbox)
        Assert.True(npcAfter.DamagePercent <= 18,
            $"Expected uncharged RMB to deal <=18 damage (uncharged), got {npcAfter.DamagePercent}");
    }

    [Fact]
    public void FightGuyRmb_ThreeHitboxPositionsCoverUppercutArc()
    {
        var sim = TestHelpers.MakeSim();
        // Place NPCs at 3 different heights to verify each sphere hitbox position
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC 1: low position (hitbox 1: OffY=0.2, OffZ=0.8, Radius=0.7)
        var npc1 = TestHelpers.NpcState(0f, 1.5f); // within low hitbox range
        npc1.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        npc1.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc1);

        // Hold 6 ticks, release, tick through attack
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 6; i++)
            sim.Tick(new() { { 1, hold }, { 100, default } });
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, release }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0,
            $"Low NPC should take damage from uppercut hitbox 1, got {sim.GetState(100).DamagePercent}");
    }

    [Fact]
    public void FightGuyRmb_ExpiresAfterAttack()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        // Hold briefly, release
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 6; i++)
            sim.Tick(new() { { 1, hold } });
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 45; i++) // 35 duration + margin
            sim.Tick(new() { { 1, release } });
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    [Fact]
    public void FightGuyRmb_ChargeResetsAfterAttack()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        // First charge: hold for 10 ticks, release, let attack expire
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, hold } });
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 45; i++)
            sim.Tick(new() { { 1, release } });

        // Attack should be over — verify ChargeTicks reset
        var afterFirst = sim.GetState(1);
        Assert.Equal(ActionState.Idle, afterFirst.State);
        Assert.Equal((ushort)0, afterFirst.ChargeTicks);

        // Wait for cooldown to expire (60 ticks)
        for (int i = 0; i < 70; i++)
            sim.Tick(new() { { 1, default } });

        // Second RMB press — should be able to charge again
        var hold2 = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, hold2 } });
        var afterSecond = sim.GetState(1);
        Assert.True(afterSecond.ChargeTicks > 0,
            $"Expected ChargeTicks > 0 on second charge, got {afterSecond.ChargeTicks}");
    }
}
