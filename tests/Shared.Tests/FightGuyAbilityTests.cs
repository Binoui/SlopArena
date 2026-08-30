using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

public class FightGuyAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);

    // ── A (FightGuyKiShot) ──

    [Fact]
    public void FightGuyKiShot_Press_EntersAimHold()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        var t0 = TestHelpers.TickN(sim, new InputState
        {
            ActiveSlot = 11,
            AimYaw = 9000,
            IsAiming = true,
        }, 1);

        // Hold-to-aim: the press opens the aim stance; the projectile only fires
        // after release (see FightGuyKiShot_FiresOneMovingProjectileAfterRelease).
        Assert.Equal(ActionState.Aiming, t0.State);
        Assert.Equal((byte)11, t0.AttackSlot);
        Assert.True(t0.IsAiming);
    }

    [Fact]
    public void FightGuyKiShot_FiresOneMovingProjectileAfterRelease()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        // Press, aim at 90°, hold, then release — the projectile must NOT spawn
        // while held and must use the aim captured at release.
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 11, AimYaw = 9000, IsAiming = true } } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, new InputState { AimYaw = 9000, IsAiming = true } } });
        Assert.Empty(sim.Resolver.GetActiveHitboxes());

        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default } });

        var projectile = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(HitboxShape.Sphere, projectile.Shape);
        // Fired at the 90° release aim, level pitch: 25 speed sideways. VY may
        // carry a tick or two of the projectile's own gravity — the contract is
        // the direction, not the exact post-spawn velocity.
        Assert.InRange(projectile.VX, 24.99f, 25.01f);
        Assert.InRange(projectile.VY, -0.1f, 0.1f);
        Assert.InRange(MathF.Abs(projectile.VZ), 0f, 0.02f);
    }

    [Fact]
    public void FightGuyKiShot_HitDoesNotMarkTarget()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        var npc = TestHelpers.NpcState(0f, 2f);
        npc.PY = GroundPY + 1.2f;
        npc.IsGrounded = false;
        npc.AirTimeTicks = 100;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        sim.Tick(new() { { 1, new InputState { ActiveSlot = 11 } }, { 100, default } });
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var target = sim.GetState(100);
        Assert.Equal((ushort)6, target.DamagePercent);
        Assert.Equal((byte)0, target.StatusFlags);
        Assert.Equal((ushort)0, target.StatusRemainingTicks);
    }

    // ── R (FightGuyCycloneKick) ──

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
    public void FightGuyCycloneKick_HitsEachTargetOnceWithModerateKnockback()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        var npc = TestHelpers.NpcState(0f, 3f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        float maxHorizontalVelocity = 0f;
        ushort maxStun = 0;
        for (int i = 0; i < 80; i++)
        {
            sim.Tick(new()
            {
                { 1, i == 0 ? TestHelpers.Input(activeSlot: 5) : default },
                { 100, default },
            });
            var target = sim.GetState(100);
            maxHorizontalVelocity = MathF.Max(maxHorizontalVelocity,
                MathF.Sqrt(target.VX * target.VX + target.VZ * target.VZ));
            maxStun = Math.Max(maxStun, target.HitstunTicks);
        }

        Assert.Equal((ushort)7, sim.GetState(100).DamagePercent);
        Assert.True(maxHorizontalVelocity > 0f, "Cyclone must apply nonzero knockback");
        Assert.True(maxStun > 0 && maxStun <= 6, $"expected short stun, got {maxStun}");
    }

    [Fact]
    public void FightGuyCycloneKick_HitsMultipleEnemiesAlongPath()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        var npc1 = TestHelpers.NpcState(0f, 2f);
        npc1.PY = GroundPY;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc1);

        var npc2 = TestHelpers.NpcState(0f, 6f);
        npc2.PY = GroundPY;
        sim.RegisterEntity(101, TestHelpers.FightGuyDef, npc2);

        for (int i = 0; i < 150; i++)
        {
            sim.Tick(new()
            {
                { 1, i == 0 ? TestHelpers.Input(activeSlot: 5) : default },
                { 100, default },
                { 101, default },
            });
        }

        Assert.Equal((ushort)7, sim.GetState(100).DamagePercent);
        Assert.Equal((ushort)7, sim.GetState(101).DamagePercent);
    }

    // ── F (FightGuyDragonBeam) ──

    [Fact]
    public void FightGuyDragonBeam_ActivatesAndLocksInPlace()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.VX = 10f;
        state.VZ = 5f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)6, t0.AttackSlot);
        Assert.Equal(0f, t0.VX);
        Assert.Equal(0f, t0.VZ);
    }

    [Fact]
    public void FightGuyDragonBeam_NoHitboxBeforeFireThenSpawnsCapsule()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        for (int i = 0; i < 23; i++)
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 6) : default } });
        Assert.Empty(sim.Resolver.GetActiveHitboxes());

        sim.Tick(new() { { 1, default } });
        var beam = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(HitboxShape.Capsule, beam.Shape);
        Assert.InRange(beam.EndZ - beam.Z, 17.99f, 18.01f);
        Assert.Equal(0f, beam.VX);
        Assert.Equal(0f, beam.VY);
        Assert.Equal(0f, beam.VZ);
    }

    [Fact]
    public void FightGuyDragonBeam_HitsOnceWithoutPull()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        var npc = TestHelpers.NpcState(0f, 4f);
        npc.PY = GroundPY + 2.5f;
        npc.IsGrounded = false;
        npc.AirTimeTicks = 100;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        for (int i = 0; i < 23; i++)
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 6) : default }, { 100, default } });
        float beforeFireZ = sim.GetState(100).PZ;

        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var target = sim.GetState(100);
        Assert.Equal((ushort)14, target.DamagePercent);
        Assert.True(target.PZ > beforeFireZ, "Dragon Beam must launch away, not pull toward the caster");
    }

    [Fact]
    public void FightGuyDragonBeam_ActivatesOnGroundAndAir()
    {
        var groundSim = TestHelpers.MakeSim();
        var ground = TestHelpers.PlayerState();
        ground.PY = GroundPY;
        TestHelpers.RegisterPlayer(groundSim, TestHelpers.FightGuyDef, ground);
        var groundAfter = TestHelpers.TickN(groundSim, TestHelpers.Input(activeSlot: 6), 28);
        Assert.Equal(ActionState.Idle, groundAfter.State);

        var airSim = TestHelpers.MakeSim();
        var air = TestHelpers.PlayerState();
        air.PY = GroundPY + 5f;
        air.IsGrounded = false;
        air.AirTimeTicks = 100;
        TestHelpers.RegisterPlayer(airSim, TestHelpers.FightGuyDef, air);
        var airAfter = TestHelpers.TickN(airSim, TestHelpers.Input(activeSlot: 6), 28);
        Assert.Equal(ActionState.Idle, airAfter.State);
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
                            OffY = 0.1f,
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
        // Entity-relative offset (no BoneName) still hits via the standard path now that
        // bone-attached hitboxes exist — Off* is anchor-relative (bone or entity origin).
        var entityLMB = new AbilitySpec
        {
            Name = "EntityLMB",
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
                            OffY = 0.8f,
                            OffZ = 1.2f,
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

        var def = TestHelpers.CloneDef(TestHelpers.BoneHitboxTestDef);
        def.LMB = entityLMB;
        def.BakedDataPath = ""; // no baked data — entity offset must not need it

        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.MankiDef);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, def, player);

        // NPC directly in front at the entity-offset hitbox position (OffZ = 1.2).
        var npc = TestHelpers.NpcState(0f, 1.2f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.MankiDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from entity-offset hitbox, got {npcAfter.DamagePercent}");
    }

    // ── RETIRED LMB (activeSlot=1) — no longer an attack input for FightGuy ──
    // LMB/AirLMB data specs were dropped; the normal tier (keys 1-4) carries the
    // melee kit. Pins the contract so retired LMB golden tests don't resurrect.

    [Fact]
    public void LMB_AttackSlot_IsRetired_NeverDispatches()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        sim.Tick(new() { { 1, new InputState { ActiveSlot = 1 } } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default } });

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State); // never entered an attack
        Assert.Equal((byte)0, s.AttackSlot);
        Assert.Equal((ushort)0, s.DamagePercent);
    }
}
