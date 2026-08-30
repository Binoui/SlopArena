using Xunit;

namespace SlopArena.Shared.Tests;

public class FacingDirectionTests
{

    /// <summary>Facing yaws are mod 2π (π ≡ −π); compare wrapped angle deltas, not raw values.</summary>
    private static void AssertNearAngle(float a, float b)
    {
        float diff = MathF.Abs(a - b);
        while (diff > MathF.PI) diff -= 2f * MathF.PI;
        Assert.True(diff < 0.003f, $"facing {b:F3} should match expected {a:F3} (diff {diff:F3})");
    }

}
