using System;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Records the authoritative Kistu G2 sword capsule over its active server ticks.
/// This deliberately resolves through HitboxGeometry, the same path used by ServerAbility.
/// </summary>
public sealed class KistuServerPoseRecordingTests
{
    [Fact]
    public void G2_ServerHitboxRecord_UsesTickMappedBakedFrames()
    {
        var def = TestHelpers.KistuDef;
        var baked = TestHelpers.LoadBakedData(def);
        Assert.NotNull(baked);
        var spec = def.Slot2!;
        var stage = Assert.Single(spec.Stages);
        var evt = stage.HitboxEvents[0];
        int frameCount = baked!.FrameCountFor("anim.kistu.g2");
        Assert.Equal(72, frameCount);

        var output = new StringBuilder();
        output.AppendLine("move=g2 hit=1 source=anim.kistu.g2 durationTicks=34 bakeFps=60");
        output.AppendLine("tick,bakedFrame,hiltX,hiltY,hiltZ,tipX,tipY,tipZ,bladeY");

        float triggerBladeY = 0f;
        for (ushort tick = evt.TriggerTick; tick < evt.TriggerTick + evt.DurationTicks; tick++)
        {
            var state = TestHelpers.PlayerState();
            state.PY = def.CapsuleHeight * 0.5f;
            state.AttackElapsedTicks = tick;

            HitboxGeometry.ResolvePositions(
                state, evt, baked, def, spec.AnimationNames, 0, slot: 6, airborne: false,
                out float hx, out float hy, out float hz,
                out float tx, out float ty, out float tz);

            int bakedFrame = Math.Min(tick * frameCount / stage.DurationTicks, frameCount - 1);
            output.AppendLine($"{tick},{bakedFrame},{hx:F3},{hy:F3},{hz:F3},{tx:F3},{ty:F3},{tz:F3},{ty - hy:F3}");
            if (tick == evt.TriggerTick) triggerBladeY = ty - hy;
        }

        Console.WriteLine(output.ToString());
        Assert.True(triggerBladeY < 0f,
            $"Server trigger tick {evt.TriggerTick} must use the side/downward G2 pose, " +
            $"but blade Y delta was {triggerBladeY:F3}.\n{output}");
    }
}
