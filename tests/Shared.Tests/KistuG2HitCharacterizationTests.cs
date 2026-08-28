using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Characterizes Kistu key 2 through the real server hit path, not only a static geometry query.
/// The test records authoritative capsule endpoints, target placement, first hit tick, and damage.
/// </summary>
public sealed class KistuG2HitCharacterizationTests
{
    private readonly ITestOutputHelper _output;

    public KistuG2HitCharacterizationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void G2_ServerHitPath_ConnectsAgainstTargetsAtEveryActiveTick()
    {
        var def = TestHelpers.KistuDef;
        var baked = TestHelpers.LoadBakedData(def);
        Assert.NotNull(baked);

        var spec = def.Slot2!;
        var stage = Assert.Single(spec.Stages);
        var evt = stage.HitboxEvents[0];
        var record = new StringBuilder();
        record.AppendLine("tick,bakedFrame,targetX,targetY,targetZ,hiltX,hiltY,hiltZ,tipX,tipY,tipZ,firstHitTick,damage");

        for (ushort targetTick = evt.TriggerTick; targetTick < evt.TriggerTick + evt.DurationTicks; targetTick++)
        {
            var pose = TestHelpers.PlayerState();
            pose.PY = TestHelpers.GroundPY(def);
            pose.AttackElapsedTicks = targetTick;
            HitboxGeometry.ResolvePositions(
                pose, evt, baked, def, spec.AnimationNames, 0, slot: 6, airborne: false,
                out float hx, out float hy, out float hz,
                out float tx, out float ty, out float tz);

            float targetX = (hx + tx) * 0.5f;
            float targetY = (hy + ty) * 0.5f;
            float targetZ = (hz + tz) * 0.5f;
            var sim = TestHelpers.MakeSim();
            var attacker = TestHelpers.PlayerState();
            attacker.PY = TestHelpers.GroundPY(def);
            sim.RegisterEntity(1, def, attacker, baked);
            var target = TestHelpers.NpcState(targetX, targetZ);
            target.PY = targetY;
            sim.RegisterEntity(100, def, target, baked);

            int firstHitTick = -1;
            ushort damage = 0;
            for (int simTick = 0; simTick <= targetTick + 2; simTick++)
            {
                var inputs = new Dictionary<ulong, InputState>
                {
                    [1] = simTick == 0 ? TestHelpers.Input(activeSlot: 7) : default,
                    [100] = default,
                };
                sim.Tick(inputs);
                var after = sim.GetState(100);
                if (firstHitTick < 0 && after.DamagePercent > 0)
                {
                    firstHitTick = simTick;
                    damage = after.DamagePercent;
                }
            }

            int bakedFrame = targetTick * baked!.FrameCountFor("anim.kistu.g2") / stage.DurationTicks;
            record.AppendLine($"{targetTick},{bakedFrame},{targetX:F3},{targetY:F3},{targetZ:F3}," +
                              $"{hx:F3},{hy:F3},{hz:F3},{tx:F3},{ty:F3},{tz:F3},{firstHitTick},{damage}");
            Assert.True(firstHitTick >= 0,
                $"G2 target at authoritative tick {targetTick} was not hit.\n{record}");
        }

        _output.WriteLine(record.ToString());
    }

    [Fact]
    public void G2_FixedTarget_RecordsActualHitRegistration()
    {
        var def = TestHelpers.KistuDef;
        var baked = TestHelpers.LoadBakedData(def);
        Assert.NotNull(baked);

        var spec = def.Slot2!;
        var stage = Assert.Single(spec.Stages);
        var evt = stage.HitboxEvents[0];
        ushort targetTick = (ushort)(evt.TriggerTick + evt.DurationTicks / 2);
        var pose = TestHelpers.PlayerState();
        pose.PY = TestHelpers.GroundPY(def);
        pose.AttackElapsedTicks = targetTick;
        HitboxGeometry.ResolvePositions(
            pose, evt, baked, def, spec.AnimationNames, 0, slot: 6, airborne: false,
            out float hx, out float hy, out float hz,
            out float tx, out float ty, out float tz);

        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, attacker, baked);
        var target = TestHelpers.NpcState((hx + tx) * 0.5f, (hz + tz) * 0.5f);
        target.PY = (hy + ty) * 0.5f;
        sim.RegisterEntity(100, def, target, baked);

        var record = new StringBuilder("simTick,attackElapsed,targetDamage,state");
        int firstHit = -1;
        for (int tick = 0; tick < 30; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 ? TestHelpers.Input(activeSlot: 7) : default,
                [100] = default,
            });
            var attackerAfter = sim.GetState(1);
            var targetAfter = sim.GetState(100);
            record.AppendLine($"\n{tick},{attackerAfter.AttackElapsedTicks},{targetAfter.DamagePercent},{targetAfter.State}");
            if (firstHit < 0 && targetAfter.DamagePercent > 0) firstHit = tick;
        }

        Assert.True(firstHit >= 0, $"Representative baked G2 target was never hit.\n{record}");
    }
}
