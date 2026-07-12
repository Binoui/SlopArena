using Xunit;

namespace SlopArena.Shared.Tests;

public class ClipExtrapolationTests
{
    // ── ExtrapolationMode enum ──

    [Fact]
    public void ExtrapolationMode_None_IsZero()
    {
        Assert.Equal(0, (int)ExtrapolationMode.None);
    }

    [Fact]
    public void ExtrapolationMode_Hold_IsOne()
    {
        Assert.Equal(1, (int)ExtrapolationMode.Hold);
    }

    [Fact]
    public void ExtrapolationMode_Continuous_IsTwo()
    {
        Assert.Equal(2, (int)ExtrapolationMode.Continuous);
    }

    // ── AnimationClipConfig defaults ──

    [Fact]
    public void AnimationClipConfig_DefaultExtrapolation_IsNone()
    {
        var cfg = new AnimationClipConfig { Name = "test" };
        Assert.Equal(ExtrapolationMode.None, cfg.Extrapolation);
    }

    [Fact]
    public void AnimationClipConfig_CanSetContinuousExtrapolation()
    {
        var cfg = new AnimationClipConfig
        {
            Name = "test",
            Extrapolation = ExtrapolationMode.Continuous
        };
        Assert.Equal(ExtrapolationMode.Continuous, cfg.Extrapolation);
    }

    // ── Math: velocity = (last - prev) * frameRate ──
    // These match the core logic in ClipExtrapolator.FromBakedData (Unity-side).

    [Fact]
    public void Velocity_FromLastTwoFrames_ComputesCorrectly()
    {
        // Simulating a bone moving +1 unit/frame along X at 30fps
        float prevX = 0f, prevY = 5f, prevZ = 0f;
        float lastX = 1f, lastY = 5f, lastZ = 2f;
        float frameRate = 30f;

        float velX = (lastX - prevX) * frameRate;
        float velY = (lastY - prevY) * frameRate;
        float velZ = (lastZ - prevZ) * frameRate;

        Assert.Equal(30f, velX);
        Assert.Equal(0f, velY);
        Assert.Equal(60f, velZ);
    }

    [Fact]
    public void Extrapolate_ProjectsPositionFromLastTwoFrames()
    {
        // Frame 0: x=0, Frame 1: x=1 → velocity = 30 units/s
        // At extraTime=0.1s past clip end: expected x = 1 + 30 * 0.1 = 4
        float prevX = 0f;
        float lastX = 1f;
        float frameRate = 30f;
        float extraTime = 0.1f;

        float velocity = (lastX - prevX) * frameRate;
        float extrapolatedX = lastX + velocity * extraTime;

        Assert.Equal(4f, extrapolatedX);
    }

    [Fact]
    public void Extrapolate_NegativeVelocity_ProjectsBackward()
    {
        float prevX = 10f;
        float lastX = 8f;
        float frameRate = 30f;
        float extraTime = 0.5f;

        float velocity = (lastX - prevX) * frameRate; // -60 units/s
        float extrapolatedX = lastX + velocity * extraTime; // 8 - 30 = -22

        Assert.Equal(-22f, extrapolatedX);
    }

    [Fact]
    public void Extrapolate_ZeroExtraTime_StaysAtLastFrame()
    {
        float prevX = 0f;
        float lastX = 5f;
        float frameRate = 30f;
        float extraTime = 0f;

        float velocity = (lastX - prevX) * frameRate;
        float extrapolatedX = lastX + velocity * extraTime;

        Assert.Equal(5f, extrapolatedX);
    }
}
