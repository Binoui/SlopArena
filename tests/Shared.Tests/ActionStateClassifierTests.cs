using Xunit;

namespace SlopArena.Shared.Tests;

public class ActionStateClassifierTests
{
    [Theory]
    [InlineData(ActionState.Idle, true)]
    [InlineData(ActionState.Dashing, true)]
    [InlineData(ActionState.JumpSquat, true)]
    [InlineData(ActionState.AirDodging, true)]
    [InlineData(ActionState.Run, true)]
    [InlineData(ActionState.Attacking, false)]
    [InlineData(ActionState.Aiming, false)]   // depends on the ServerAbility instance (release detection)
    [InlineData(ActionState.Hitstun, false)]
    [InlineData(ActionState.Warping, false)]
    [InlineData(ActionState.LedgeHang, false)] // Complex for opponents (occupancy is multi-entity)
    [InlineData(ActionState.Sliding, false)] // unused by any code path — not a Predictable member
    public void IsPredictable_MatchesADR0011Partition(ActionState state, bool expected)
    {
        Assert.Equal(expected, SlopArena.Shared.Rollback.ActionStateClassifier.IsPredictable(state));
    }

    [Fact]
    public void IsSnapSafe_LedgeHangTrue_AimingFalse_ElseMatchesIsPredictable()
    {
        Assert.True(SlopArena.Shared.Rollback.ActionStateClassifier.IsSnapSafe(ActionState.LedgeHang));
        Assert.False(SlopArena.Shared.Rollback.ActionStateClassifier.IsSnapSafe(ActionState.Aiming));
        foreach (ActionState state in System.Enum.GetValues<ActionState>())
        {
            if (state == ActionState.LedgeHang) continue;
            Assert.Equal(
                SlopArena.Shared.Rollback.ActionStateClassifier.IsPredictable(state),
                SlopArena.Shared.Rollback.ActionStateClassifier.IsSnapSafe(state));
        }
    }
}
