using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Pins the presentation contract for restarting an attack clip when IASA
/// starts the same slot again. PlayerRenderer consumes CharacterState directly;
/// these predicates mirror its change-detection rule without requiring Unity.
/// </summary>
public class AttackAnimationRestartTests
{
    private static bool LegacyDetectAttackRestart(
        ActionState lastAnimState,
        byte lastAttackSlot,
        byte lastComboStage,
        byte lastAttackSequence,
        CharacterState last,
        CharacterState current)
        => current.State != lastAnimState
            || (current.State == ActionState.Attacking
                && (current.AttackSlot != lastAttackSlot
                    || current.ComboStage != lastComboStage));

    private static bool FixedDetectAttackRestart(
        ActionState lastAnimState,
        byte lastAttackSlot,
        byte lastComboStage,
        byte lastAttackSequence,
        CharacterState last,
        CharacterState current)
        => current.State != lastAnimState
            || (current.State == ActionState.Attacking
                && (current.AttackSlot != lastAttackSlot
                    || current.ComboStage != lastComboStage
                    || current.AttackSequence != lastAttackSequence
                    || current.AttackElapsedTicks < last.AttackElapsedTicks));

    [Fact]
    public void SameSlotIasaRestart_RestartsAttackAnimation()
    {
        var previous = new CharacterState
        {
            State = ActionState.Attacking,
            AttackSlot = 3,
            ComboStage = 0,
            AttackSequence = 4,
            AttackElapsedTicks = 0,
        };
        var restarted = previous;
        restarted.AttackSequence = 5;
        Assert.False(LegacyDetectAttackRestart(
            previous.State,
            previous.AttackSlot,
            previous.ComboStage,
            previous.AttackSequence,
            previous,
            restarted));
        Assert.True(FixedDetectAttackRestart(
            previous.State,
            previous.AttackSlot,
            previous.ComboStage,
            previous.AttackSequence,
            previous,
            restarted));
    }

    [Fact]
    public void AttackCountdown_DoesNotRestartAnimation()
    {
        var previous = new CharacterState
        {
            State = ActionState.Attacking,
            AttackSlot = 3,
            ComboStage = 0,
            AttackSequence = 4,
            AttackElapsedTicks = 16,
        };
        var countdown = previous;
        countdown.AttackElapsedTicks = 17;

        Assert.False(FixedDetectAttackRestart(
            previous.State,
            previous.AttackSlot,
            previous.ComboStage,
            previous.AttackSequence,
            previous,
            countdown));
    }
}
