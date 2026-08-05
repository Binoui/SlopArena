using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Behaviour tests for Nilus' kit. Task 2 covers registration + the four
/// data-driven slots; Tasks 3-6 append tests for Q/E/R/F.
/// </summary>
public class NilusAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.NilusDef);
    private static CharacterDefinition Def => TestHelpers.NilusDef;

    private static ServerSimulation SimWithPlayer()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, Def, player);
        return sim;
    }

    [Fact]
    public void Registry_ReturnsNilus()
    {
        Assert.Equal(CharacterClass.Nilus, Def.Class);
        Assert.Equal("Nilus", Def.DisplayName);
    }

    /// <summary>
    /// Activation for the four data-driven slots.
    ///
    /// <c>State == Attacking</c> and <c>AttackSlot == slot</c> after one tick are NOT
    /// discriminators: Simulation.cs:275-281 sets exactly those two fields straight from input,
    /// gated only on the slot's cooldown, with no spec lookup and no ability instance — and
    /// PreTickAbilities leaves ActiveSlot unconsumed when the factory returns null. Both hold
    /// with Nilus removed from <c>AbilityFactory</c> AND with all four specs nulled in
    /// <c>NilusData</c>; their effective claim is "Cooldown0/Cooldown1 start at 0".
    ///
    /// What only a real ability instance produces is the END. Nothing in the generic path ever
    /// leaves Attacking — activation sets <c>StateTicks = 0</c>, so TickTimers' generic expiry
    /// (Simulation.cs:413-419) never fires — so a slot with no class behind it attacks forever.
    /// Completing and handing the slot back is the assertion with teeth.
    ///
    /// Per-slot behaviour lives in dedicated tests: <see cref="Lmb_DamagesEnemyInClawRange"/>,
    /// <see cref="Rmb_ChargedHitsHarderThanTap"/>,
    /// <see cref="AirLmb_VoidRake_ChainsToStage2_AndLandsBothHits"/> and
    /// <see cref="AirRmb_Collapse_DrivesNilusDownward_DuringTheAttack"/>.
    /// </summary>
    [Theory]
    [InlineData((byte)1)] // LMB
    [InlineData((byte)2)] // RMB
    public void DataDrivenGroundSlot_ActivatesAndRunsToCompletion(byte slot)
    {
        var sim = SimWithPlayer();
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot, aiming: true), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal(slot, t0.AttackSlot);

        for (int i = 0; i < 200; i++) sim.Tick(new() { { 1, default } });

        var done = sim.GetState(1);
        Assert.Equal(ActionState.Idle, done.State);
        Assert.Equal((byte)0, done.AttackSlot);
    }

    /// <inheritdoc cref="DataDrivenGroundSlot_ActivatesAndRunsToCompletion"/>
    [Theory]
    [InlineData((byte)1)] // AirLMB
    [InlineData((byte)2)] // AirRMB
    public void AirSlot_ActivatesAndRunsToCompletion(byte slot)
    {
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 5f;
        s.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, s);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal(slot, t0.AttackSlot);

        for (int i = 0; i < 200; i++) sim.Tick(new() { { 1, default } });

        var done = sim.GetState(1);
        Assert.Equal(ActionState.Idle, done.State);
        Assert.Equal((byte)0, done.AttackSlot);
    }

    /// <summary>
    /// Void Rake is a 2-stage aerial chain and had literally zero behavioural coverage: the only
    /// thing touching it was the tautological <c>AirSlot_Activates(1)</c>, so neither the chain,
    /// nor the 3/5 damage ladder, nor stage 2's <c>Launcher</c> knockback was tested.
    ///
    /// The chain is the discriminator no generic path can fake: <c>ComboStage</c> only advances
    /// inside <see cref="Abilities.StageChainAbility"/> when a second press is buffered inside
    /// stage 1's 9-tick ChainWindow. Damage on a body is what makes the ladder load-bearing —
    /// 3 from stage 1, then 5 more from stage 2.
    /// </summary>
    [Fact]
    public void AirLmb_VoidRake_ChainsToStage2_AndLandsBothHits()
    {
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 4f;
        s.IsGrounded = false;
        s.JumpsLeft = 0;
        TestHelpers.RegisterPlayer(sim, Def, s);

        var npc = TestHelpers.NpcState(0f, 1.2f);
        npc.PY = TestHelpers.CombatGroundPY + 4f;
        npc.IsGrounded = false;
        sim.RegisterEntity(100, TestHelpers.CombatDef, npc);

        var press = TestHelpers.Input(activeSlot: 1);
        sim.Tick(new() { { 1, press }, { 100, default } });    // stage 1 opens
        sim.Tick(new() { { 1, press }, { 100, default } });    // buffered inside the chain window

        byte reachedStage = 0;
        ushort firstHit = 0;
        float launchKvy = 0f;
        for (int tick = 2; tick < 60; tick++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });

            var caster = sim.GetState(1);
            if (caster.ComboStage > reachedStage) reachedStage = caster.ComboStage;

            var dummy = sim.GetState(100);
            if (firstHit == 0) firstHit = dummy.DamagePercent;
            if (launchKvy <= 0f && dummy.DamagePercent >= 8) launchKvy = dummy.KVY;
        }

        Assert.Equal((byte)1, reachedStage);                       // the chain fired
        Assert.Equal((ushort)3, firstHit);                         // stage 1's 3
        Assert.Equal((ushort)8, sim.GetState(100).DamagePercent);  // + stage 2's 5
        Assert.True(launchKvy > 0f,
            $"stage 2 is a Launcher, so its hit must throw the dummy upward; KVY={launchKvy:F2}");
    }

    /// <summary>
    /// Collapse is a committed downward slam: <c>AirRMB.Stages[1].MoveY = -14</c> (tap) and
    /// <c>ChargedStages[0].MoveY = -18</c> are its identity, and
    /// <see cref="Abilities.AirChargeAttack"/> is the one class in the engine that honours
    /// <c>AttackStage.MoveY</c>.
    ///
    /// The assertion is deliberately taken DURING the attack, not after it. Nilus has
    /// <c>AirFloatGravity = 0</c> for a 40-tick float window, so a 36-tick aerial attack from
    /// a hover ends before gravity ever engages: "he is lower afterwards" passes with the
    /// field completely unread, because the float window simply expires on tick 40 and he
    /// falls like anyone else. Only in-attack descent distinguishes a live MoveY from a dead
    /// one. Measured with the field wired: the tap attack opens on tick 5 (5-tick release
    /// debounce), so by tick 16 the -14 m/s slam has run 11 ticks: PY 5.75 -> ~3.18.
    /// </summary>
    [Fact]
    public void AirRmb_Collapse_DrivesNilusDownward_DuringTheAttack()
    {
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 5f;
        s.IsGrounded = false;
        s.JumpsLeft = 0;
        TestHelpers.RegisterPlayer(sim, Def, s);
        float startY = s.PY;

        float moveY = Def.AirRMB!.Stages[1].MoveY;
        Assert.True(moveY < 0f, $"Collapse must declare a downward MoveY, got {moveY}");

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 2) } });
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, default } });

        var mid = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, mid.State);
        Assert.Equal(moveY, mid.VY, 3);
        Assert.True(mid.PY < startY - 2f,
            $"Collapse must lose height while the attack is still running: " +
            $"{startY:F3} -> {mid.PY:F3} on tick 16 (state {mid.State})");
    }

    /// <summary>
    /// The charged Collapse is the air kill move, so the charged branch must actually be
    /// reachable: tap resolves <c>Stages[1]</c> (10 dmg), holding past ChargeHoldTicks (45)
    /// auto-releases <c>ChargedStages[0]</c> (14 dmg), which hits substantially harder.
    /// </summary>
    [Fact]
    public void AirRmb_ChargedHitsHarderThanTap()
    {
        // Tap: press then release immediately (uncharged Stages[1]). NPC hovers at +3 m,
        // in the tap hitbox's path (release ~tick5, hitbox runner ticks 13-20).
        var tapSim = SimWithPlayer();
        var tapPlayer = tapSim.GetState(1);
        tapPlayer.PY = GroundPY + 5f;
        tapPlayer.IsGrounded = false;
        tapPlayer.JumpsLeft = 0;
        tapSim.SetState(1, tapPlayer);
        var tapNpc = TestHelpers.NpcState(0f, 0.5f);
        tapNpc.PY = GroundPY + 3f;
        tapNpc.IsGrounded = false;
        TestHelpers.RegisterNpc(tapSim, Def, tapNpc);

        tapSim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 2) }, { 100, default } });
        for (int i = 0; i < 40; i++) tapSim.Tick(new() { { 1, default }, { 100, default } });
        ushort tapDmg = tapSim.GetState(100).DamagePercent;

        // Hold: keep aiming past the charge threshold (auto-releases charged). Nilus floats
        // for the 40-tick float window, so the charged hitbox (runner ticks 53-62) finds the
        // NPC higher up than the tap does — spawn it at +5 m.
        var holdSim = SimWithPlayer();
        var holdPlayer = holdSim.GetState(1);
        holdPlayer.PY = GroundPY + 5f;
        holdPlayer.IsGrounded = false;
        holdPlayer.JumpsLeft = 0;
        holdSim.SetState(1, holdPlayer);
        var holdNpc = TestHelpers.NpcState(0f, 0.5f);
        holdNpc.PY = GroundPY + 5f;
        holdNpc.IsGrounded = false;
        TestHelpers.RegisterNpc(holdSim, Def, holdNpc);

        var holdInput = new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 2, aiming: true) }, { 100, default } };
        for (int i = 0; i < 70; i++) holdSim.Tick(holdInput);
        for (int i = 0; i < 40; i++) holdSim.Tick(new() { { 1, default }, { 100, default } });
        ushort holdDmg = holdSim.GetState(100).DamagePercent;

        Assert.Equal((ushort)10, tapDmg);
        Assert.Equal((ushort)14, holdDmg);
        Assert.True(holdDmg > tapDmg,
            $"charged Collapse must out-damage the tap: tap={tapDmg} charged={holdDmg}");
    }

    /// <summary>
    /// The charged Collapse declares its own slam speed (<c>ChargedStages[0].MoveY = -18</c>,
    /// faster than the tap's -14) and <see cref="Abilities.AirChargeAttack"/> must honour it
    /// during the charged attack, not just the tap.
    /// </summary>
    [Fact]
    public void AirRmb_ChargedCollapse_DescendsDuringTheAttack()
    {
        var sim = SimWithPlayer();
        var player = sim.GetState(1);
        player.PY = GroundPY + 5f;
        player.IsGrounded = false;
        player.JumpsLeft = 0;
        sim.SetState(1, player);
        float startY = player.PY;

        float moveY = Def.AirRMB!.ChargedStages![0].MoveY;
        Assert.True(moveY < 0f, $"charged Collapse must declare a downward MoveY, got {moveY}");

        // Hold past ChargeHoldTicks (45): auto-release at tick 45, then 10 attack ticks.
        var holdInputs = new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 2, aiming: true) } };
        for (int i = 0; i < 55; i++) sim.Tick(holdInputs);

        var mid = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, mid.State);
        Assert.Equal(moveY, mid.VY, 3);
        Assert.True(mid.PY < startY - 2f,
            $"charged Collapse must lose height while the attack is still running: " +
            $"{startY:F3} -> {mid.PY:F3} on tick 55 (state {mid.State})");
    }

    // Lmb_DamagesEnemyInClawRange used to live here, asserting DamagePercent > 0 for a dummy at
    // z = 1.2. Nilus_LMB_Hit_Confirm.json runs the same cast against a dummy at the same z and
    // pins the damage at exactly 3 (NpcSnap and NpcFinal both), so the `> 0` form could only
    // ever fail where the golden already fails with a better message. Removed rather than kept
    // as a weaker duplicate.

    /// <summary>
    /// Entropy Lance is the kill move, so the charged branch must actually be
    /// reachable: tap resolves Stages[1], holding past ChargeHoldTicks (50)
    /// auto-releases ChargedStages[0], which hits substantially harder.
    /// </summary>
    [Fact]
    public void Rmb_ChargedHitsHarderThanTap()
    {
        // Tap: press then release immediately (uncharged Stages[1]).
        var tapSim = SimWithPlayer();
        var tapNpc = TestHelpers.NpcState(0f, 1.4f); tapNpc.PY = GroundPY;
        TestHelpers.RegisterNpc(tapSim, Def, tapNpc);
        tapSim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 2, aiming: true) }, { 100, default } });
        for (int i = 0; i < 40; i++) tapSim.Tick(new() { { 1, default }, { 100, default } });
        ushort tapDmg = tapSim.GetState(100).DamagePercent;

        // Hold: keep aiming past the charge threshold (auto-releases charged).
        var holdSim = SimWithPlayer();
        var holdNpc = TestHelpers.NpcState(0f, 1.4f); holdNpc.PY = GroundPY;
        TestHelpers.RegisterNpc(holdSim, Def, holdNpc);
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 110; i++) holdSim.Tick(new() { { 1, hold }, { 100, default } });
        ushort holdDmg = holdSim.GetState(100).DamagePercent;

        Assert.True(tapDmg > 0, $"tap should deal damage, got {tapDmg}");
        Assert.True(holdDmg > tapDmg, $"charged ({holdDmg}) should exceed tap ({tapDmg})");
    }

    // ── Q: Void Rift ──

    /// <summary>
    /// Q must actually be served by <see cref="NilusVoidRift"/>. State/AttackSlot/IsAiming
    /// are NOT discriminators: Simulation copies IsAiming from input and opens the Q spec's
    /// stage before abilities tick, and ServerSimulation's null-ability guard does not
    /// consume ActiveSlot — so all three hold even with a null factory arm. The ability's
    /// own two-phase machine is the discriminator: ComboStage advancing to the throw phase
    /// on release, and the seed hitbox it spawns at throw_trigger_tick.
    /// </summary>
    [Fact]
    public void Q_Activates_AndEntersAimingState()
    {
        var sim = SimWithPlayer();
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 400);
        sim.Tick(new() { { 1, aim } });

        var t0 = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
        Assert.True(t0.IsAiming);

        // Hold past the 8-tick aim lock, then release and run past throw_trigger_tick (10).
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, aim } });
        var release = TestHelpers.Input(activeSlot: 0, aiming: false, aimDistance: 400);
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, release } });

        Assert.Equal((byte)1, sim.GetState(1).ComboStage);

        // The SEED specifically, not "some hitbox exists": the seed is the only gravity-carrying
        // hitbox Nilus ever spawns, and the only one that ignores entities.
        Hitbox? seed = null;
        foreach (var hb in sim.Resolver.GetActiveHitboxes())
            if (hb.Gravity > 0f && hb.IgnoresEntities) seed = hb;
        Assert.True(seed.HasValue, "no void seed hitbox was spawned");
        Assert.True(seed!.Value.Explosion.HasValue, "the seed must carry the rift as its payload");
    }

    [Fact]
    public void Q_RiftDamagesRepeatedly_AfterTheCastEnds()
    {
        const float floorY = 0f; // TestHelpers.TestArena's heightmap is flat at 0.

        var sim = SimWithPlayer();
        // The seed flies along +z at x = 0 (aim yaw 0) and grounds near z = 3.84. The NPC
        // stands 2.5 m to the SIDE of that arc. Hitbox.IgnoresEntities now makes that
        // clearance unnecessary — Q_Seed_PassesThroughABody_AndStillPlantsTheRiftOnTheGround
        // covers the on-arc case directly — but it is kept because it is the CLEAN geometry
        // for this test: nothing but CheckGroundCollision can possibly have planted the rift.
        // 2.5 m beats seed radius 0.5 + hurtbox ~1.0 and is still inside the landed rift's
        // 3 m radius. Pushing the NPC further along +z instead would put it outside that
        // radius and it would take no damage at all.
        var npc = TestHelpers.NpcState(2.5f, 3.84f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        // Hold to aim at ~4m, then release.
        // aimDistance is in CENTIMETRES on InputState (400 = 4m); the sim converts
        // it to CharacterState.AimTargetDistance in metres.
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 400);
        sim.Tick(new() { { 1, aim }, { 100, default } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, aim }, { 100, default } });

        // Releasing drops IsAiming but keeps AimDistance — SimulateTick rewrites
        // s.AimTargetDistance from the input every tick, before abilities tick, so a
        // zeroed AimDistance on the release frame would erase the aim. Same convention
        // as CombatPipelineTests' Manki-Q release pull.
        var release = TestHelpers.Input(activeSlot: 0, aiming: false, aimDistance: 400);
        for (int i = 0; i < 60; i++) sim.Tick(new() { { 1, release }, { 100, default } });

        // The rift is the only rehitting hitbox alive; it exists because
        // CheckGroundCollision grounded the seed. Its Y is what proves the ground path ran:
        // a body-impact rift would float at the seed/hurtbox contact height (~1.32 m).
        Hitbox? found = null;
        foreach (var hb in sim.Resolver.GetActiveHitboxes())
            if (hb.RehitIntervalTicks > 0) found = hb;
        Assert.True(found.HasValue, "no lingering rift hitbox found");
        Hitbox rift = found!.Value;
        Assert.Equal(3f, rift.Radius, 3);
        Assert.Equal((ushort)240, rift.DurationTicks);
        Assert.True(MathF.Abs(rift.Y - floorY) < 0.05f,
            $"rift must spawn at ground level ({floorY}), not at body-impact height; got Y={rift.Y:F3}");

        ushort afterCast = sim.GetState(100).DamagePercent;
        Assert.True(afterCast > 0, $"rift should have ticked at least once, got {afterCast}");

        // Nilus is out of Attacking, yet the rift keeps damaging.
        Assert.NotEqual(ActionState.Attacking, sim.GetState(1).State);
        for (int i = 0; i < 120; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > afterCast,
            "rift must keep ticking after the ability instance is gone");
    }

    /// <summary>
    /// The seed is documented as inert, and "inert" has to mean it does not interact with
    /// bodies at all — not "it deals 0 damage on contact".
    ///
    /// Half one: the free ability-cancel. Without <see cref="Hitbox.IgnoresEntities"/> the
    /// contact still produces a HitResult, and ApplyKnockback with magnitude 0 takes the else
    /// branch that sets <c>State = Idle</c> (Simulation.cs:937). The next TickAbilities then
    /// discards whatever the victim was running while still charging its cooldown.
    ///
    /// The victim is mid-Event-Horizon on purpose: 540 ticks of cooldown is the most
    /// expensive thing the cancel could steal, and the ult is rooted (VX = VZ = 0 every tick)
    /// so it holds still on the arc for the whole flight. Measured pre-fix: the ult drops to
    /// Idle on the tick the seed clips it, and the rift is stranded in the same instant.
    /// </summary>
    [Fact]
    public void Q_Seed_DoesNotCancelTheAbilityOfABodyItPassesThrough()
    {
        var sim = SimWithPlayer();
        TestHelpers.RegisterNpc(sim, Def, OnTheSeedArc());

        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 400);
        sim.Tick(new() { { 1, aim }, { 100, TestHelpers.Input(activeSlot: 6) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, aim }, { 100, default } });
        Assert.Equal(ActionState.Attacking, sim.GetState(100).State);

        // Only for as long as the seed is in the air: the landed rift is inside the victim's
        // 3 m radius and legitimately damages it, so the flight is the window in which the
        // seed must be provably inert. The whole flight sits inside the ult's 72-tick
        // telegraph, so nothing but the seed can touch the victim here.
        var release = TestHelpers.Input(activeSlot: 0, aiming: false, aimDistance: 400);
        int flightTicks = 0;
        for (Hitbox? rift = null; rift == null && flightTicks < 60; flightTicks++)
        {
            sim.Tick(new() { { 1, release }, { 100, default } });

            var v = sim.GetState(100);
            Assert.Equal((ushort)0, v.DamagePercent);
            Assert.Equal(ActionState.Attacking, v.State);
            Assert.True(v.AnimLockTicks > 0,
                $"flight tick {flightTicks}: the seed cancelled the victim's ult");
            Assert.Equal((ushort)0, v.Cooldown5);

            foreach (var hb in sim.Resolver.GetActiveHitboxes())
                if (hb.RehitIntervalTicks > 0) rift = hb;
        }

        Assert.True(flightTicks > 30,
            $"the seed must have survived a full flight past the body, not {flightTicks} ticks");
    }

    /// <summary>
    /// Half two: the stranded rift. Without <see cref="Hitbox.IgnoresEntities"/>, clipping a
    /// body sets <c>hb.Active = false</c> (SpellResolver.cs:250), which sends the seed down the
    /// EXPIRY path — and that path queues the explosion at the PRE-MOVE position (:257-262)
    /// without ever reaching CheckGroundCollision. The 3 m rift then hangs at chest height for
    /// all 240 ticks. Measured pre-fix with this fixture: the rift blooms at Y = 2.109, head
    /// height, 0.8 m short of where the arc would have put it.
    ///
    /// The dummy carries no ability here, so the rift's landing height is the only thing this
    /// test can fail on.
    /// </summary>
    [Fact]
    public void Q_Seed_PassesThroughABody_AndStillPlantsTheRiftOnTheGround()
    {
        var sim = SimWithPlayer();
        TestHelpers.RegisterNpc(sim, Def, OnTheSeedArc());

        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 400);
        sim.Tick(new() { { 1, aim }, { 100, default } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, aim }, { 100, default } });

        var release = TestHelpers.Input(activeSlot: 0, aiming: false, aimDistance: 400);
        Hitbox? rift = null;
        for (int flight = 0; flight < 60 && rift == null; flight++)
        {
            sim.Tick(new() { { 1, release }, { 100, default } });
            foreach (var hb in sim.Resolver.GetActiveHitboxes())
                if (hb.RehitIntervalTicks > 0) rift = hb;
        }

        Assert.True(rift.HasValue, "the seed must still bloom into a rift");
        Assert.True(MathF.Abs(rift!.Value.Y) < 0.05f,
            $"the rift must land on the ground, not at body-impact height; got Y={rift.Value.Y:F3}");
        // And where the arc said it would, not where the body stood.
        TestHelpers.AssertNear(3.84f, rift.Value.Z, 0.1f);
    }

    /// <summary>
    /// A dummy standing squarely in the 4 m seed's descent, at z = 2.9. The seed's 0.5 m sphere
    /// first reaches the 0.22 m head capsule with 0.05 m to spare and then ploughs through the
    /// 0.3 m torso capsule with 0.6 m of margin over half a dozen ticks, so "it clips" does not
    /// hang on the collision maths' last decimal — unlike z = 2, where only the head is ever
    /// reachable and the margin is 4 mm.
    /// </summary>
    private static CharacterState OnTheSeedArc()
    {
        var s = TestHelpers.NpcState(0f, 2.9f);
        s.PY = GroundPY;
        return s;
    }

    // ── E: Riftwalk ──

    /// <summary>
    /// E must run the 8 ticks its stage declares, not the 4 the old
    /// <c>_ticks &gt;= s.AnimLockTicks</c> form gave it. TickTimers DECREMENTS AnimLockTicks
    /// (Simulation.cs:405) before TickAbilities runs, so an up-counter compared against it
    /// crosses at ceil(N/2) — and the consequence is not cosmetic: EndAbility leaves
    /// AnimLockTicks alone (ServerAbility.cs:236) while ProcessNormalMovement runs for any
    /// Idle entity (Simulation.cs:300-303), so E handed air control back on tick 5 and undid
    /// the blink's own <c>VX = VZ = 0</c>.
    ///
    /// Airborne with a held sideways shove is what makes that observable: an Attacking entity
    /// never reaches ProcessNormalMovement, so VX can only become non-zero if the instance is
    /// already gone. This also removes the zero-tick margin under <c>burst_tick = 4</c>, which
    /// used to sit exactly ON the truncated end tick.
    /// </summary>
    [Fact]
    public void E_CommitsForItsFullAuthoredDuration_NotHalfOfIt()
    {
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 6f;
        s.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, s);

        var strafing = TestHelpers.Input(moveX: 1f);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });   // ability tick 1
        int endTick = -1;
        for (int tick = 2; tick <= 20 && endTick < 0; tick++)
        {
            sim.Tick(new() { { 1, strafing } });
            var mid = sim.GetState(1);
            if (mid.State != ActionState.Attacking) { endTick = tick; break; }
            Assert.Equal(0f, mid.VX, 3);
        }

        Assert.Equal(Def.E!.Stages[0].DurationTicks, (ushort)endTick);
        Assert.Equal(8, endTick);
    }

    [Fact]
    public void E_BlinksForwardBySpecDistance()
    {
        var sim = SimWithPlayer();
        float startZ = sim.GetState(1).PZ;

        // FacingYaw 0 => +Z forward.
        for (int i = 0; i < 12; i++)
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 4) : default } });

        float travelled = sim.GetState(1).PZ - startZ;
        TestHelpers.AssertNear(6f, travelled, 0.75f);
    }

    /// <summary>
    /// The two-charge pool is the whole cost model, so all three states must hold:
    /// two casts land, the third is refused while the pool is dry. ChargeStockSpent
    /// is also the sharpest discriminator against a null factory arm — the sim spends
    /// the charge only AFTER CreateServer returns non-null (ServerSimulation.cs:422-436).
    /// </summary>
    [Fact]
    public void E_SpendsChargeAndBlocksWhenPoolEmpty()
    {
        var sim = SimWithPlayer();

        // First blink
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default } });
        Assert.Equal((byte)1, sim.GetState(1).ChargeStockSpent);

        // Second blink
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default } });
        Assert.Equal((byte)2, sim.GetState(1).ChargeStockSpent);

        // Third is blocked — pool exhausted
        float beforeZ = sim.GetState(1).PZ;
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default } });

        Assert.Equal((byte)2, sim.GetState(1).ChargeStockSpent);
        TestHelpers.AssertNear(beforeZ, sim.GetState(1).PZ, 0.2f);
    }

    /// <summary>
    /// A dry pool must be temporary, not terminal: Simulation regenerates one charge
    /// per "charge_regen_ticks" (300) and the recovered charge has to be spendable
    /// again. Without this, "blocked when empty" would also pass for an ability that
    /// is simply broken after two casts.
    /// </summary>
    [Fact]
    public void E_ChargeRegeneratesAfterRegenTicks()
    {
        var sim = SimWithPlayer();

        // Burn both charges.
        for (int cast = 0; cast < 2; cast++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
            for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default } });
        }
        Assert.Equal((byte)2, sim.GetState(1).ChargeStockSpent);

        // Idle out one full regen period (300 ticks); exactly one charge comes back.
        for (int i = 0; i < 300; i++) sim.Tick(new() { { 1, default } });
        Assert.Equal((byte)1, sim.GetState(1).ChargeStockSpent);

        // And the recovered charge actually blinks.
        float beforeZ = sim.GetState(1).PZ;
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default } });

        Assert.Equal((byte)2, sim.GetState(1).ChargeStockSpent);
        TestHelpers.AssertNear(6f, sim.GetState(1).PZ - beforeZ, 0.75f);
    }

    /// <summary>
    /// Riftwalk is Nilus' only recovery, so it must fire off the ground. Slot 3 has no
    /// air-variant spec (GetSlotAbility (3, _) => E), so the same class serves both.
    ///
    /// What Riftwalk itself owns airborne is PURELY horizontal displacement: no height
    /// gain, no upward velocity, no free ground snap. It deliberately does NOT assert
    /// that he keeps falling, because that is not Riftwalk's to give or take —
    /// ServerSimulation.ActivateAbility (ServerSimulation.cs:76-79) zeroes downward VY
    /// and resets AirTimeTicks for EVERY aerial ability activation, and Nilus' 40-tick
    /// AirFloatGravity-0 float window then holds VY at 0 for the whole 8-tick cast. So
    /// every Riftwalk stalls the fall by engine rule; the ability adds nothing upward
    /// on top of that, which is what this test pins.
    /// </summary>
    [Fact]
    public void E_BlinksWhileAirborne_WithoutGainingHeight()
    {
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 6f;
        s.IsGrounded = false;
        s.VY = -5f; // the real recovery case: already falling when he blinks
        TestHelpers.RegisterPlayer(sim, Def, s);

        float startZ = sim.GetState(1).PZ;
        float startY = sim.GetState(1).PY;
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 4; i++) sim.Tick(new() { { 1, default } });

        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ChargeStockSpent);
        TestHelpers.AssertNear(6f, after.PZ - startZ, 0.75f);
        Assert.False(after.IsGrounded);
        Assert.True(after.VY <= 0f, $"blink must not push him upward; VY={after.VY:F3}");
        Assert.True(after.PY <= startY + 0.01f,
            $"blink must gain no height; PY={after.PY:F3} start={startY:F3}");
    }

    /// <summary>
    /// The burst has to actually connect with a body, not merely exist.
    ///
    /// This is NOT subsumed by <see cref="E_ArrivalBurstSpawnsSpecSizedSphereAtDestination"/>
    /// (which reads the hitbox's own Damage field without ever resolving it) nor by
    /// Nilus_E_Riftwalk.json, which is the one Nilus golden with no NPC at all. The damage is
    /// pinned to the exact spec value rather than <c>&gt; 0</c> so a retune has to come here.
    /// </summary>
    [Fact]
    public void E_ArrivalBurstDamagesNearbyEnemy()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 6f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 16; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.Equal((ushort)4, sim.GetState(100).DamagePercent);   // burst_damage
    }

    /// <summary>
    /// The burst must be a spec-SIZED sphere centred on the ARRIVAL point. Damage alone
    /// cannot pin burst_radius: the enemy above stands exactly where Nilus lands, so any
    /// radius down to 0.05 still connects (verified by mutation). Inspecting the live
    /// hitbox is what makes burst_radius, burst_damage and burst_stun_ticks load-bearing.
    /// </summary>
    [Fact]
    public void E_ArrivalBurstSpawnsSpecSizedSphereAtDestination()
    {
        var sim = SimWithPlayer();

        Hitbox? burst = null;
        for (int i = 0; i < 12 && burst == null; i++)
        {
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 4) : default } });
            foreach (var hb in sim.Resolver.GetActiveHitboxes())
                if (hb.Shape == HitboxShape.Sphere && hb.OwnerId == 1 && hb.DurationTicks == 4)
                    burst = hb;
        }

        Assert.True(burst.HasValue, "no arrival burst hitbox was spawned");
        Hitbox b = burst!.Value;
        Assert.Equal(HitboxShape.Sphere, b.Shape);
        TestHelpers.AssertNear(1.6f, b.Radius, 0.001f);   // burst_radius
        TestHelpers.AssertNear(4f, b.Damage, 0.001f);      // burst_damage
        Assert.Equal((ushort)12, b.StunTicks);             // burst_stun_ticks

        // Centred on the destination (z = blink_distance), not on the origin.
        TestHelpers.AssertNear(0f, b.X, 0.05f);
        TestHelpers.AssertNear(6f, b.Z, 0.05f);
    }

    // ── E: Riftwalk vs terrain ──

    /// <summary>Z at which <see cref="RiseArena"/>'s surface steps up.</summary>
    private const float RiseZ = 10f;

    /// <summary>
    /// A 40x40 arena, surface 0 for z &lt; <see cref="RiseZ"/> and <paramref name="riseY"/>
    /// from there on. A fixture rather than a shipped arena because ArenaRegistry's
    /// definitions carry NO baked heightmap (Heightmap.Data is null — it is built from
    /// the .tscn collision mesh at load time), so The Split and Sanctum would sample
    /// float.MinValue everywhere and prove nothing about geometry. Local fixture arenas
    /// are the suite's existing idiom (TestHelpers.TestArena,
    /// ServerSimulationTests.MakeTestArena).
    ///
    /// Note the rise is a 1 m-wide bilinear ramp between the z=9 and z=RiseZ cell rows,
    /// exactly like a real baked heightmap — not a vertical face.
    /// </summary>
    private static ArenaDefinition RiseArena(float riseY)
    {
        const int w = 40, h = 40;
        var data = new float[w * h];
        for (int z = 0; z < h; z++)
        {
            float y = z >= (int)RiseZ ? riseY : 0f;
            for (int x = 0; x < w; x++) data[z * w + x] = y;
        }
        return new ArenaDefinition
        {
            Name = "rise",
            DisplayName = "Rise Fixture",
            KillHeight = -20f,
            SpawnPoints = new[] { new SpawnPoint { X = 5f, Y = 0f, Z = 5f, Yaw = 0f } },
            Heightmap = new ArenaHeightmap
            {
                Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f,
            },
        };
    }

    /// <summary>Nilus on the low floor at (5, 5), facing +Z toward the rise.</summary>
    private static ServerSimulation SimOnRise(float riseY)
    {
        var sim = TestHelpers.MakeSim(RiseArena(riseY));
        var player = TestHelpers.PlayerState(5f, 5f);
        player.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, Def, player);
        return sim;
    }

    /// <summary>
    /// THE regression. A 3 m rise is far deeper than PlatformSnapTolerance, so ground
    /// resolution will NOT snap him up onto it (the force-snap at Simulation.cs:348-353
    /// is inside the Hitstun branch only). An untraced blink therefore lands him INSIDE
    /// the geometry, ungrounded, and gravity drills him to the blast zone — a self-KO on
    /// shipped stages (The Split's 3 m platform). The trace must stop him at the ramp
    /// foot instead, still standing on the low floor a minute of ticks later.
    /// </summary>
    [Fact]
    public void E_BlinkIntoWall_StopsShort_AndDoesNotSinkThroughTheStage()
    {
        var sim = SimOnRise(3f);

        for (int i = 0; i < 12; i++)
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 4) : default } });

        var arrived = sim.GetState(1);
        Assert.True(arrived.PZ < RiseZ,
            $"blink must stop before the rise at z={RiseZ}; got PZ={arrived.PZ:F3}");
        Assert.True(arrived.PZ > 5f,
            $"blink must still cover the clear ground; got PZ={arrived.PZ:F3}");
        Assert.True(arrived.IsGrounded, "must arrive grounded on the low floor");

        // The untraced version passed this point at PY=0.825/IsGrounded=false and was at
        // PY=-0.595/VY=-9.35 sixty ticks on.
        for (int i = 0; i < 60; i++) sim.Tick(new() { { 1, default } });

        var later = sim.GetState(1);
        Assert.True(later.IsGrounded, $"must not fall through the stage; PY={later.PY:F3}");
        TestHelpers.AssertNear(GroundPY, later.PY, 0.001f);
        Assert.Equal((byte)0, later.Deaths);
    }

    /// <summary>
    /// The trace must not eat the designed recovery risk: blinking off the heightmap
    /// (past the stage edge) is VALID, covers the full distance, and leaves him falling.
    /// TestArena's heightmap ends at z=199, so a blink from z=195 leaves the grid.
    /// </summary>
    [Fact]
    public void E_BlinkOffTheHeightmap_StillCoversFullDistance_AndFalls()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState(5f, 195f);
        player.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, Def, player);

        for (int i = 0; i < 12; i++)
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 4) : default } });

        var arrived = sim.GetState(1);
        TestHelpers.AssertNear(201f, arrived.PZ, 0.01f);
        Assert.False(arrived.IsGrounded, "off the heightmap he must be airborne");

        // Past Nilus' 40-tick zero-gravity float window, gravity has to be pulling him down.
        for (int i = 0; i < 60; i++) sim.Tick(new() { { 1, default } });

        var later = sim.GetState(1);
        Assert.False(later.IsGrounded);
        Assert.True(later.VY < 0f, $"must be falling; VY={later.VY:F3}");
        Assert.True(later.PY < GroundPY - 1f, $"must have lost height; PY={later.PY:F3}");
        Assert.Equal((byte)0, later.Deaths);
    }

    /// <summary>
    /// The PlatformSnapTolerance allowance: a 0.4 m step is one ground resolution WILL
    /// snap him onto, so the trace must not treat it as a wall. Full distance, and he
    /// ends standing on top of the step.
    /// </summary>
    [Fact]
    public void E_BlinkOverSmallStep_TraversesAndLandsOnTop()
    {
        const float stepY = 0.4f;
        var sim = SimOnRise(stepY);

        for (int i = 0; i < 12; i++)
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 4) : default } });

        var after = sim.GetState(1);
        TestHelpers.AssertNear(11f, after.PZ, 0.01f);
        Assert.True(after.IsGrounded, "a snappable step must leave him grounded");
        TestHelpers.AssertNear(TestHelpers.GroundPY(Def, stepY), after.PY, 0.001f);
    }

    /// <summary>
    /// Common case regression guard: on flat ground the trace must cost nothing — the
    /// full blink_distance, exactly, not a step-quantised approximation of it.
    /// </summary>
    [Fact]
    public void E_BlinkOnFlatGround_CoversFullDistanceExactly()
    {
        var sim = SimWithPlayer();
        float startZ = sim.GetState(1).PZ;

        for (int i = 0; i < 12; i++)
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 4) : default } });

        TestHelpers.AssertNear(6f, sim.GetState(1).PZ - startZ, 0.01f);
    }

    /// <summary>
    /// The spec is explicit that truncation is not a refund: the charge is spent and the
    /// arrival burst fires anyway — at where he ACTUALLY ended, not the intended
    /// destination. Bursting at the intended point would place a hitbox inside the rise,
    /// hitting enemies Nilus never reached.
    /// </summary>
    [Fact]
    public void E_TruncatedBlink_StillSpendsCharge_AndBurstsAtFinalPosition()
    {
        var sim = SimOnRise(3f);

        Hitbox? burst = null;
        for (int i = 0; i < 12; i++)
        {
            sim.Tick(new() { { 1, i == 0 ? TestHelpers.Input(activeSlot: 4) : default } });
            if (burst == null)
                foreach (var hb in sim.Resolver.GetActiveHitboxes())
                    if (hb.Shape == HitboxShape.Sphere && hb.OwnerId == 1 && hb.DurationTicks == 4)
                        burst = hb;
        }

        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ChargeStockSpent);
        Assert.True(burst.HasValue, "a truncated blink must still burst");

        Hitbox b = burst!.Value;
        TestHelpers.AssertNear(after.PZ, b.Z, 0.05f);
        Assert.True(b.Z < RiseZ,
            $"burst must not spawn at the intended destination inside the rise; Z={b.Z:F3}");
    }

    // ── R: Nether Grasp ──

    /// <summary>
    /// R commits for the 34 ticks its stage declares, not the 17 the old
    /// <c>_ticks &gt;= s.AnimLockTicks</c> form gave it. TickTimers DECREMENTS AnimLockTicks
    /// (Simulation.cs:405) before TickAbilities runs, so an up-counter compared against it
    /// crosses at ceil(N/2). No golden could see this — they all sample position after the
    /// lock — but it halved the grab's commitment: EndAbility leaves AnimLockTicks alone
    /// (ServerAbility.cs:236) and ProcessNormalMovement runs for any Idle entity
    /// (Simulation.cs:300-303), so Nilus walked freely from tick 18 of 34. It would also have
    /// silently eaten any HitboxEvent past tick 17, since the trigger match is <c>==</c>.
    ///
    /// Held forward input is the discriminator: an Attacking entity never reaches
    /// ProcessNormalMovement, so PZ can only move if the instance is already gone. Measured
    /// pre-fix: Idle from tick 18, and 1.35 m of drift by tick 34.
    /// </summary>
    [Fact]
    public void R_CommitsForItsFullAuthoredDuration_NotHalfOfIt()
    {
        var sim = SimWithPlayer();
        float startZ = sim.GetState(1).PZ;
        var walking = TestHelpers.Input(moveY: 1f);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });   // ability tick 1
        int endTick = -1;
        for (int tick = 2; tick <= 50 && endTick < 0; tick++)
        {
            sim.Tick(new() { { 1, walking } });
            if (sim.GetState(1).State != ActionState.Attacking) endTick = tick;
        }

        Assert.Equal(Def.R!.Stages[0].DurationTicks, (ushort)endTick);
        Assert.Equal(34, endTick);

        // No free walking tail, and the 480-tick cooldown is charged on the real final tick.
        TestHelpers.AssertNear(startZ, sim.GetState(1).PZ, 0.05f);
        Assert.Equal((ushort)480, sim.GetState(1).Cooldown4);
    }

    [Fact]
    public void R_PullsTargetTowardNilus()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 6f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        float startDistance = sim.GetState(100).PZ - sim.GetState(1).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 40; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        float endDistance = sim.GetState(100).PZ - sim.GetState(1).PZ;

        // DamagePercent is NOT asserted here: Nilus_R_Nether_Grasp.json pins it at exactly 8,
        // so a `> 0` check here can only fail where the golden already fails louder.
        Assert.True(endDistance < startDistance - 1f,
            $"target should be dragged inward: {startDistance:F2}m -> {endDistance:F2}m");
    }

    /// <summary>
    /// The yank has no airborne special case, so an opponent caught mid-jump gets dragged in
    /// too — the intended anti-air answer. The height matters: the spec's claw capsule runs
    /// PY+0.8 to PY+1.6 with radius 0.6, so it tops out ~2.2 m above Nilus' centre. Measured
    /// envelope: a target connects from 0.00 m up to 2.75 m above ground and misses from
    /// 3.00 m. 2 m is comfortably inside it and unambiguously off the floor.
    /// </summary>
    [Fact]
    public void R_PullsAirborneTargetToo()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = GroundPY + 2f;
        npc.IsGrounded = false;
        TestHelpers.RegisterNpc(sim, Def, npc);

        float startDistance = sim.GetState(100).PZ - sim.GetState(1).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 40; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        var end = sim.GetState(100);
        Assert.False(end.IsGrounded, "the target must still be an airborne case, not a floor duplicate");
        Assert.True(end.PZ - sim.GetState(1).PZ < startDistance - 0.5f);
    }

    /// <summary>
    /// THE regression for this slot. The yank must be knockback, not a velocity write:
    /// ProcessHitstun rewrites VX/VZ from KVX/KVZ every tick (Simulation.cs:470-471), so a
    /// plain target.VZ assignment in OnHitEntity is erased before it ever integrates into
    /// position, and the target would simply stand still.
    ///
    /// Every assertion here needs NilusNetherGrasp: the R spec's HitboxEvent carries zero
    /// knockback on purpose, so ResolveHits' own ApplyKnockback (ServerSimulation.cs:722)
    /// produces magnitude 0 — KVZ stays 0 and State is left Idle, not Hitstun. Only
    /// OnHitEntity's inward ApplyKnockback puts the target in hitstun with negative KVZ and
    /// keeps closing the gap tick after tick.
    ///
    /// The distance is pinned as a band, not a point: pull_force is expected to be retuned,
    /// but 3-5 m brackets the spec's "~4 m" and still fails on the 8 m over-yank the
    /// original pull_force = 14 produced.
    /// </summary>
    [Fact]
    public void R_YankIsKnockback_SoItSurvivesHitstunAndKeepsClosing()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 6f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        float nilusZ = sim.GetState(1).PZ;
        float startZ = sim.GetState(100).PZ;

        // Cast, then run until the claw connects.
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        int hitTick = -1;
        for (int i = 0; i < 20 && hitTick < 0; i++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });
            if (sim.GetState(100).DamagePercent > 0) hitTick = i;
        }
        Assert.True(hitTick >= 0, "the claw must connect at 6m");

        var onHit = sim.GetState(100);
        Assert.Equal(ActionState.Hitstun, onHit.State);
        Assert.True(onHit.KVZ < -1f,
            $"knockback must point back at Nilus (-Z), got KVZ={onHit.KVZ:F2}");
        ushort stunWindow = onHit.HitstunTicks;
        Assert.True(stunWindow > 0, "the grasp must stun");

        // Position, tick by tick. A velocity write would leave PZ frozen from here on;
        // knockback keeps integrating for the whole stun window and then coasts to rest.
        float prevZ = onHit.PZ;
        for (int i = 0; i < 45; i++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });
            float z = sim.GetState(100).PZ;
            Assert.True(z <= prevZ + 1e-4f,
                $"tick {i}: target drifted outward {prevZ:F3} -> {z:F3}");
            if (i < stunWindow)
                Assert.True(z < prevZ - 1e-3f,
                    $"stun tick {i}: the yank stopped moving the target ({prevZ:F3} -> {z:F3}) — " +
                    "a plain VX/VZ write is erased by ProcessHitstun");
            prevZ = z;
        }

        float dragged = startZ - prevZ;
        Assert.InRange(dragged, 3f, 5f);
        Assert.True(prevZ > nilusZ && prevZ < startZ,
            $"the target must end between Nilus and where it stood, not through him: " +
            $"PZ={prevZ:F3}, Nilus {nilusZ:F3}, start {startZ:F3}");
    }

    // ── F: Event Horizon ──

    /// <summary>
    /// The ult's length is stated twice and the two statements must agree. NilusEventHorizon
    /// derives its own lifecycle from windup_ticks + drag_duration_ticks, while
    /// Stages[0].DurationTicks independently drives the caster's baked hurtbox pose pacing
    /// (ServerSimulation.cs:318-323). Retune one without the other and the detonation lands on
    /// a tick where the caster's hurtboxes are posed for a different frame of the animation.
    ///
    /// NilusData now DERIVES the stage duration from the two Params, so asserting the two
    /// constants against each other (as this test used to) can no longer fail — and it never
    /// exercised NilusEventHorizon at all. What this asserts instead is the coupling that
    /// actually matters and that the derivation cannot guarantee on its own: the ability's
    /// real, simulated end tick equals the stage duration the animation is paced against.
    /// </summary>
    [Fact]
    public void F_EndsExactlyOnItsStageDuration_SoTheBakedPosePacingStaysInSync()
    {
        var sim = SimWithPlayer();

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });   // ability tick 1
        int endTick = -1;
        for (int tick = 2; tick <= 200 && endTick < 0; tick++)
        {
            sim.Tick(new() { { 1, default } });
            if (sim.GetState(1).State != ActionState.Attacking) endTick = tick;
        }

        Assert.Equal(Def.F!.Stages[0].DurationTicks, (ushort)endTick);
        Assert.Equal(132, endTick);
    }

    /// <summary>
    /// A 540-tick ultimate whose Description says it "drags everything inward, then detonates"
    /// has to damage everything, not one arbitrary body.
    ///
    /// The velocity drag already iterated all of SimulationStates, but both hitboxes it spawns
    /// left <c>RehitIntervalTicks</c> at 0 — and SpellResolver deactivates such a hitbox after
    /// its FIRST connect (SpellResolver.cs:250-251), with dictionary iteration order rather
    /// than distance choosing the victim. So the ult vacuumed everyone within 6 m and then
    /// damaged one of them. Both dummies here start inside the radius; both must take every
    /// drag pulse and the detonation.
    ///
    /// Unobservable in the two-entity modes that ship today, which is why no existing test or
    /// golden covers it — every Nilus scenario has at most one dummy.
    /// </summary>
    [Fact]
    public void F_DragPulsesAndDetonation_DamageEveryTargetInRadius()
    {
        var sim = SimWithPlayer();

        var near = TestHelpers.NpcState(0f, 3f);
        near.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(100, TestHelpers.CombatDef, near);

        // Off-axis and further out, so neither the drag direction nor the resolver's iteration
        // order can make the two indistinguishable. Both stay inside TestArena's heightmap
        // (origin 0,0) — a negative coordinate samples float.MinValue and the dummy falls away.
        var far = TestHelpers.NpcState(4.5f, 2f);
        far.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(101, TestHelpers.CombatDef, far);

        var idle = new Dictionary<ulong, InputState> { { 1, default }, { 100, default }, { 101, default } };

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default }, { 101, default } });  // tick 1
        for (int i = 0; i < 71; i++) sim.Tick(idle);                                                      // → tick 72
        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        Assert.Equal((ushort)0, sim.GetState(101).DamagePercent);

        sim.Tick(idle);                                                                                   // tick 73
        Assert.Equal((ushort)3, sim.GetState(100).DamagePercent);
        Assert.Equal((ushort)3, sim.GetState(101).DamagePercent);

        for (int tick = 74; tick <= 132; tick++) sim.Tick(idle);                                          // → tick 132

        // Six pulses of 3 plus the 18-damage detonation, for both.
        Assert.Equal((ushort)36, sim.GetState(100).DamagePercent);
        Assert.Equal((ushort)36, sim.GetState(101).DamagePercent);
        Assert.Equal(ActionState.Hitstun, sim.GetState(100).State);
        Assert.Equal(ActionState.Hitstun, sim.GetState(101).State);
    }

    /// <summary>
    /// The telegraph IS the commitment: Nilus cannot walk out of his own ult.
    ///
    /// State/AttackSlot are NOT discriminators here — ServerSimulation's null-ability guard
    /// (ServerSimulation.cs:424) does not consume ActiveSlot, so SimulateTick's generic
    /// attack path still sets Attacking/AttackSlot=6, and an Attacking character never runs
    /// ProcessNormalMovement, so it would stand still with no ability at all. AnimLockTicks
    /// is the discriminator: only NilusEventHorizon.OnStart takes the input lock, and only
    /// the real ability ever releases it — the generic path leaves it at 0 forever.
    /// </summary>
    [Fact]
    public void F_LocksCasterInPlaceDuringWindup()
    {
        var sim = SimWithPlayer();
        float startZ = sim.GetState(1).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });
        // Try to walk out of it — the lock must hold.
        var walking = TestHelpers.Input(activeSlot: 0);
        walking.MoveY = 1f;
        for (int i = 0; i < 40; i++) sim.Tick(new() { { 1, walking } });

        var mid = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, mid.State);
        TestHelpers.AssertNear(startZ, mid.PZ, 0.3f);
        Assert.True(mid.AnimLockTicks > 0,
            "the ult must hold the input lock for its whole duration; a data-driven " +
            "fallback attack never sets AnimLockTicks at all");
    }

    /// <summary>
    /// Phase boundary, pinned to the tick. windup_ticks = 72 is a pure telegraph: an enemy
    /// standing on top of Nilus takes nothing for 72 ticks, and the drag's first damage
    /// pulse lands on tick 73 — the very first tick of the drag window.
    /// </summary>
    [Fact]
    public void F_TelegraphDealsNoDamage_ThenTheDragOpensOnTick73()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 3f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });   // tick 1
        for (int i = 0; i < 71; i++) sim.Tick(new() { { 1, default }, { 100, default } }); // → tick 72

        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);

        sim.Tick(new() { { 1, default }, { 100, default } });                              // tick 73
        Assert.Equal((ushort)3, sim.GetState(100).DamagePercent);
    }

    /// <summary>
    /// The drag: six pulses of drag_damage on a drag_interval_ticks cadence, each one also
    /// pulling the target inward. Position over time is the assertion — the gap must close
    /// monotonically across the whole 60-tick window, never widen.
    ///
    /// The pull is a plain VX/VZ write, which is only legal because the pulse carries no
    /// knockback and no stun: nothing puts the target in hitstun, so ProcessHitstun never
    /// runs and never overwrites VX/VZ from KVX/KVZ (Simulation.cs:468-471).
    /// </summary>
    [Fact]
    public void F_DragClosesTheGapEveryPulse_AndStacksTickDamage()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 5.5f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        float startZ = sim.GetState(100).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });   // tick 1

        var damageTicks = new List<int>();
        float prevZ = startZ;
        for (int tick = 2; tick <= 131; tick++)
        {
            ushort before = sim.GetState(100).DamagePercent;
            sim.Tick(new() { { 1, default }, { 100, default } });
            var now = sim.GetState(100);
            if (now.DamagePercent != before) damageTicks.Add(tick);

            Assert.True(now.PZ <= prevZ + 1e-4f,
                $"tick {tick}: the drag pushed the target outward ({prevZ:F4} -> {now.PZ:F4})");
            Assert.NotEqual(ActionState.Hitstun, now.State);
            prevZ = now.PZ;
        }

        // Pulses phased off the drag's first tick: 73, 83, 93, 103, 113, 123.
        Assert.Equal(new List<int> { 73, 83, 93, 103, 113, 123 }, damageTicks);
        Assert.Equal((ushort)18, sim.GetState(100).DamagePercent);

        // Nothing has detonated yet — the caster is still committed.
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.True(startZ - prevZ > 0.5f,
            $"the drag must visibly close the gap: {startZ:F3} -> {prevZ:F3}");
    }

    /// <summary>
    /// THE regression for this slot, and the one thing that can go silently missing.
    ///
    /// The detonation lands on the ability's LAST tick (132 = windup 72 + drag 60), which is
    /// only true because the lifecycle runs off a private up-counter against durations cached
    /// at OnStart. The house idiom `_ticks >= s.AnimLockTicks` compares an up-counter against
    /// a counter TickTimers decrements (Simulation.cs:405); the two cross at half the stage
    /// duration, so an ult written that way would be discarded on tick 66 and would never
    /// detonate at all — no damage spike, no knockback, no cooldown.
    ///
    /// The knockback must also be a real hitbox, not a velocity write: it is the only channel
    /// that survives, because ProcessHitstun rewrites VX/VZ from KVX/KVZ on every hitstun
    /// tick. And it must point OUTWARD — the exact opposite of the drag that preceded it.
    /// </summary>
    [Fact]
    public void F_DetonatesOnTheFinalTick_ThrowingTargetsOutwardAndUp()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        float nilusZ = sim.GetState(1).PZ;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });   // tick 1

        // The detonation is detected by the ABILITY ENDING, not by a damage threshold. The old
        // `DamagePercent - before >= 18` form coupled the tick assertion to detonation_damage,
        // so lowering that number reported "Assert.Equal(132, -1)" — "the detonation tick moved"
        // — which it had not. NilusEventHorizon spawns the blast and calls EndAbility on the
        // same tick and nowhere else, so leaving Attacking IS the detonation.
        int detonationTick = -1;
        float zAtDetonation = 0f;
        ushort damageBeforeBlast = 0;
        for (int tick = 2; tick <= 200 && detonationTick < 0; tick++)
        {
            ushort before = sim.GetState(100).DamagePercent;
            float beforeZ = sim.GetState(100).PZ;
            sim.Tick(new() { { 1, default }, { 100, default } });
            if (sim.GetState(1).State != ActionState.Attacking)
            {
                detonationTick = tick;
                zAtDetonation = beforeZ;
                damageBeforeBlast = before;
            }
        }

        Assert.Equal(132, detonationTick);

        // And the blast really is a damage spike on that tick, separately from when it lands.
        Assert.Equal((ushort)18, damageBeforeBlast);                              // 6 drag pulses
        Assert.Equal((ushort)36, sim.GetState(100).DamagePercent);                // + detonation

        var hit = sim.GetState(100);
        Assert.Equal(ActionState.Hitstun, hit.State);
        Assert.Equal((ushort)40, hit.HitstunTicks);
        Assert.True(hit.KVZ > 1f,
            $"the blast must throw the target OUTWARD (+Z, away from Nilus), got KVZ={hit.KVZ:F2} — " +
            "a drag pulse resolving after the detonation would zero this");
        Assert.True(hit.KVY > 1f, $"and upward, got KVY={hit.KVY:F2}");
        Assert.False(hit.IsGrounded, "an outward-and-up blast takes the target off the floor");

        // The ability completed itself: Idle, and the 540t cooldown charged. Neither happens
        // on the null-ability fallback path, and neither happens if the instance was dropped.
        var caster = sim.GetState(1);
        Assert.Equal(ActionState.Idle, caster.State);
        Assert.Equal((ushort)540, caster.Cooldown5);

        // Position over time, and the blast must keep OWNING the victim for its whole stun
        // window. This is what catches a drag pulse that shares the detonation tick: the
        // detonation resolves first, but the pulse survives to the NEXT tick, and its
        // ApplyKnockback (zero base, zero growth) wipes KVX/KVY/KVZ and hands the victim
        // straight back to Idle one tick into a 40-tick send.
        float prevZ = sim.GetState(100).PZ;
        for (int i = 1; i <= 20; i++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });
            var flying = sim.GetState(100);
            Assert.Equal(ActionState.Hitstun, flying.State);
            Assert.True(flying.KVZ > 1f,
                $"stun tick {i}: the blast's knockback was zeroed (KVZ={flying.KVZ:F2})");
            Assert.True(flying.PZ > prevZ,
                $"stun tick {i}: the target stopped travelling outward ({prevZ:F3} -> {flying.PZ:F3})");
            prevZ = flying.PZ;
        }

        for (int i = 0; i < 20; i++) sim.Tick(new() { { 1, default }, { 100, default } });
        float endZ = sim.GetState(100).PZ;
        Assert.True(endZ - zAtDetonation > 2f,
            $"the detonation must throw the target clear: {zAtDetonation:F3} -> {endZ:F3}");
        Assert.True(endZ > nilusZ, "outward means away from the centre, never through it");
    }

    /// <summary>
    /// The input lock must outlast the ability by one tick. TickTimers decrements
    /// AnimLockTicks (Simulation.cs:405) before TickAbilities runs, so a lock of exactly 132
    /// is already 0 on ability tick 132 — and a player still holding dash would StartDash on
    /// the detonation tick, leave Attacking, and have the instance discarded
    /// (ServerSimulation.cs:143) one tick short of the blast. Losing the payoff of a 540-tick
    /// cooldown to your own movement input is not an acceptable failure mode for an ult.
    /// </summary>
    [Fact]
    public void F_HoldingDashThroughTheUltCannotCancelTheDetonation()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        var mashing = TestHelpers.Input(dash: true);
        mashing.MoveY = 1f;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });   // tick 1
        for (int i = 0; i < 131; i++) sim.Tick(new() { { 1, mashing }, { 100, default } }); // → tick 132

        var hit = sim.GetState(100);
        Assert.Equal((ushort)36, hit.DamagePercent);       // 6 drag pulses + the detonation
        Assert.Equal(ActionState.Hitstun, hit.State);
        Assert.True(hit.KVZ > 1f, $"the blast still has to land, got KVZ={hit.KVZ:F2}");
        TestHelpers.AssertNear(0f, sim.GetState(1).PZ, 0.3f);
    }

    /// <summary>
    /// Jump is now gated on <c>AnimLockTicks == 0</c> (Simulation.cs:220), same as dash.
    /// Pressing jump during Event Horizon keeps Nilus in Attacking and the detonation fires.
    /// </summary>
    [Fact]
    public void F_JumpIsBlocked_DuringEventHorizon()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });

        // Run through windup into the drag phase (windup=72, so tick ~80 is mid-drag).
        for (int i = 0; i < 79; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        // Mid-drag: Nilus is locked in Attacking.
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.True(sim.GetState(1).AnimLockTicks > 0);
        ushort dragDmg = sim.GetState(100).DamagePercent;
        Assert.True(dragDmg > 0, $"drag should have ticked; got {dragDmg}");

        // Press jump — blocked by AnimLockTicks > 0.
        sim.Tick(new() { { 1, TestHelpers.Input(jump: true) }, { 100, default } });

        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal((ushort)6, sim.GetState(1).AttackSlot);

        // Let F run to completion (detonation + recovery).
        for (int i = 0; i < 70; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.True(sim.GetState(100).DamagePercent > dragDmg,
            $"detonation should have added damage beyond {dragDmg}; got {sim.GetState(100).DamagePercent}");
    }

    /// <summary>
    /// <see cref="Abilities.AirChargeAttack"/> refuses a DOWNWARD <c>AttackStage.MoveY</c> while
    /// grounded. Collapse's -14 (tap) / -18 (charged) is only 0.233-0.3 m/tick against
    /// <c>PlatformSnapTolerance = 0.5</c>, so the unguarded write was harmless for everything
    /// that ships — but at |MoveY| &gt; 30 the post-integration PY lands below the snap window
    /// (Simulation.cs:363), control falls to <c>IsGrounded = false</c>, and the ability
    /// re-dirties VY every tick: the character drills through the floor to the blast zone.
    /// No golden could catch that, because no shipped stage declares a value anywhere near it.
    ///
    /// The fixture is a test-local clone with MoveY = -48 (0.8 m/tick). Nothing in NilusData
    /// moves; the shipped -14 tap is asserted to be inside the safe band by the same maths.
    /// </summary>
    [Fact]
    public void AirRmb_GroundedDownwardMoveY_CannotDrillThroughTheFloor()
    {
        var shipped = Def.AirRMB!.Stages[1];
        Assert.True(MathF.Abs(shipped.MoveY) / 60f < 0.5f,
            $"the shipped Collapse must stay inside PlatformSnapTolerance; " +
            $"{shipped.MoveY} gives {MathF.Abs(shipped.MoveY) / 60f:F3} m/tick");

        // Clone the SHIPPING AirRMB shape (charge phase + tap) and drill the tap stage:
        // a single-stage fixture would auto-release with _wasCharged on tick 1 (vacuous
        // 0 >= 0 comparison with ChargeHoldTicks unset), exercising a shape that never ships.
        var tap = shipped;
        tap.MoveY = -48f;   // 0.8 m/tick, past the 0.5 m snap window
        var def = TestHelpers.CloneDef(Def);
        def.AirRMB = new AbilitySpec
        {
            Name = Def.AirRMB.Name,
            CooldownTicks = Def.AirRMB.CooldownTicks,
            Behavior = AbilityBehavior.ChargeAttack,
            ChargeHoldTicks = Def.AirRMB.ChargeHoldTicks,
            Stages = new[] { Def.AirRMB.Stages[0], tap },
            ChargedStages = Def.AirRMB.ChargedStages,
            AnimationNames = Def.AirRMB.AnimationNames,
        };

        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 0.6f;   // one tick of the drill above the floor
        s.IsGrounded = false;
        s.JumpsLeft = 0;
        TestHelpers.RegisterPlayer(sim, def, s);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 2) } });
        for (int i = 0; i < 60; i++) sim.Tick(new() { { 1, default } });

        var after = sim.GetState(1);
        Assert.True(after.IsGrounded, $"the slam must stop at the floor; PY={after.PY:F3}");
        TestHelpers.AssertNear(GroundPY, after.PY, 0.001f);
        Assert.Equal((byte)0, after.Deaths);
    }

    /// <summary>
    /// The spec's escape clause, verbatim: "a target that dashes out during the drag keeps
    /// the tick damage already dealt and takes nothing else." The drag ticks and the
    /// detonation come from the same instance and are both radius-gated, so leaving is a real
    /// out — there is deliberately no tether and no re-capture.
    /// </summary>
    [Fact]
    public void F_TargetThatLeavesTheRadius_KeepsOnlyTheDamageAlreadyDealt()
    {
        var sim = SimWithPlayer();
        var npc = TestHelpers.NpcState(0f, 5.5f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        var fleeing = TestHelpers.Input();
        fleeing.MoveY = 1f;   // straight out along +Z, away from the rift

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });   // tick 1
        for (int i = 0; i < 71; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        // Take the first pulse (tick 73) on the chin, then run.
        sim.Tick(new() { { 1, default }, { 100, fleeing } });
        ushort dealtBeforeEscaping = sim.GetState(100).DamagePercent;
        Assert.Equal((ushort)3, dealtBeforeEscaping);

        // Clear of drag_radius plus the fattest hurtbox capsule — no pulse can still reach.
        ushort dealtWhenClear = 0;
        for (int tick = 74; tick <= 132; tick++)
        {
            sim.Tick(new() { { 1, default }, { 100, fleeing } });
            if (dealtWhenClear == 0 && sim.GetState(100).PZ - sim.GetState(1).PZ > 7.5f)
                dealtWhenClear = sim.GetState(100).DamagePercent;
        }

        Assert.True(dealtWhenClear > 0, "the target must actually get clear of the radius");

        var escaped = sim.GetState(100);
        Assert.Equal(dealtWhenClear, escaped.DamagePercent);
        Assert.NotEqual(ActionState.Hitstun, escaped.State);
        TestHelpers.AssertNear(0f, escaped.KVZ, 1e-4f);
        Assert.True(escaped.DamagePercent < 18,
            $"escaping must beat the 18-damage detonation outright, got {escaped.DamagePercent}");

        // Escaping is not an interrupt: the ult still ran its full course and went on cooldown.
        Assert.Equal((ushort)540, sim.GetState(1).Cooldown5);
    }
}
