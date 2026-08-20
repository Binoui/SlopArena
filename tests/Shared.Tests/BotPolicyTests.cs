using System;
using Xunit;

namespace SlopArena.Shared.Tests;
using SlopArena.Shared.AI;

/// <summary>
/// Issue #148 — <see cref="HeuristicBotPolicy"/> decision correctness against crafted sim states:
/// approaches when far (world-space movement), faces the opponent (AimYaw via ADR-0017 snap),
/// attacks when in reach, never emits action input while locked, magnitudes ≤ 1, and is
/// deterministic for a fixed seeded RNG stream.
/// </summary>
public class BotPolicyTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static readonly HeuristicBotPolicy Policy = new();

    private static CharacterState Self(float x = 0f, float z = 0f)
    {
        var s = TestHelpers.PlayerState(x, z);
        s.PY = TestHelpers.GroundPY(Def);
        return s;
    }

    private static CharacterState Opponent(float x = 0f, float z = 0f)
    {
        var s = TestHelpers.PlayerState(x, z);
        s.PY = TestHelpers.GroundPY(Def);
        s.EntityId = 100;
        return s;
    }

    private static InputState Decide(CharacterState self, CharacterState target, int seed = 42, BotMemory? memory = null)
        => Policy.Decide(self, target, Def, new Random(seed), memory ?? new BotMemory());

    [Fact]
    public void FarOpponent_ApproachesWithWorldSpaceMovement_NoAttack()
    {
        var self = Self();
        var target = Opponent(z: 10f); // 10 m directly ahead on +Z

        var input = Decide(self, target);

        // Approaches toward the target: MoveY IS the world Z axis.
        Assert.Equal(0f, input.MoveX);
        Assert.Equal(1f, input.MoveY);
        Assert.Equal(0, input.ActiveSlot); // out of perceived range → no attack
        Assert.False(input.Dash);
        Assert.False(input.Jump);
    }

    [Fact]
    public void FarOpponentToTheSide_MovesAndFacesCorrectly()
    {
        var self = Self();
        var target = Opponent(x: 5f, z: 0f); // 5 m on the +X axis

        var input = Decide(self, target);

        Assert.Equal(1f, input.MoveX);
        Assert.Equal(0f, input.MoveY);
        // Facing: atan2(+X, 0) = 90° → AimYaw deg×100 = 9000 (reuse the game's facing snap).
        Assert.Equal(9000, input.AimYaw);
        Assert.True(input.FaceToCamera);
    }

    [Fact]
    public void OpponentInReach_AttacksWithASlot()
    {
        var self = Self();
        var target = Opponent(z: 0.5f); // well within connect range

        var input = Decide(self, target);

        Assert.True(input.ActiveSlot > 0, $"expected an attack press, got ActiveSlot={input.ActiveSlot}");
    }

    [Fact]
    public void InHitstun_NeverEmitsActionInput()
    {
        var self = Self(z: 0f);
        self.HitstunTicks = 10;
        var target = Opponent(z: 0.5f);

        var input = Decide(self, target);

        Assert.Equal(0, input.ActiveSlot);
        Assert.False(input.Dash);
        Assert.False(input.Jump);
    }

    [Fact]
    public void InHitstop_NeverEmitsActionInput()
    {
        var self = Self();
        self.HitstopTicks = 5;
        var target = Opponent(z: 0.5f);

        var input = Decide(self, target);

        Assert.Equal(0, input.ActiveSlot);
        Assert.False(input.Dash);
        Assert.False(input.Jump);
    }

    [Fact]
    public void MovementMagnitude_NeverExceedsOne()
    {
        var self = Self();
        var rng = new Random(1);
        for (int i = 0; i < 100; i++)
        {
            var target = Opponent((float)(rng.NextDouble() * 20 - 10), (float)(rng.NextDouble() * 20 - 10));
            var input = Decide(self, target, seed: i);
            Assert.True(MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY) <= 1f + 0.0001f,
                $"movement magnitude exceeded 1: ({input.MoveX},{input.MoveY})");
        }
    }

    [Fact]
    public void SameSeed_ProducesIdenticalDecisions()
    {
        var self = Self();
        var target = Opponent(x: 3f, z: 4f);

        var a = Decide(self, target, seed: 99);
        var b = Decide(self, target, seed: 99);

        Assert.Equal(a.ActiveSlot, b.ActiveSlot);
        Assert.Equal(a.MoveX, b.MoveX);
        Assert.Equal(a.MoveY, b.MoveY);
        Assert.Equal(a.AimYaw, b.AimYaw);
        Assert.Equal(a.Dash, b.Dash);
        Assert.Equal(a.Jump, b.Jump);
    }

    [Fact]
    public void LockedState_EmitsNoActionInput()
    {
        var self = Self();
        self.AnimLockTicks = 4;
        var target = Opponent(z: 0.5f);

        var input = Decide(self, target);

        Assert.Equal(0f, input.MoveX);
        Assert.Equal(0f, input.MoveY);
        Assert.Equal(0, input.ActiveSlot);
        Assert.Equal(0, input.AimYaw);
        Assert.False(input.FaceToCamera);
        Assert.False(input.Dash);
        Assert.False(input.Jump);
    }

    [Fact]
    public void ThreatResponse_HigherLevelCanDodgeWhileLowLevelWaits()
    {
        var self = Self();
        var target = Opponent(z: 0.5f);
        target.State = ActionState.Attacking;
        int highLevelDodges = 0;

        for (int seed = 0; seed < 100; seed++)
        {
            var lowMemory = new BotMemory { DifficultyLevel = 1 };
            var highMemory = new BotMemory { DifficultyLevel = 9 };
            var low = Policy.Decide(self, target, Def, new Random(seed), lowMemory);
            var high = Policy.Decide(self, target, Def, new Random(seed), highMemory);

            Assert.Equal(0, low.ActiveSlot);
            Assert.False(low.Dash);
            Assert.False(low.Jump);
            if (high.Dash) highLevelDodges++;
        }

        Assert.True(highLevelDodges > 0, "level 9 never selected a threat dodge across 100 seeds");
    }

    [Fact]
    public void ConfirmedHitMemory_EnablesMoreHighLevelFollowUps()
    {
        var self = Self();
        var target = Opponent(z: 0.5f);
        int lowAttacks = 0;
        int highAttacks = 0;

        for (int seed = 0; seed < 200; seed++)
        {
            var lowMemory = new BotMemory { DifficultyLevel = 1, LastAttackConnected = true };
            var highMemory = new BotMemory { DifficultyLevel = 9, LastAttackConnected = true };
            if (Policy.Decide(self, target, Def, new Random(seed), lowMemory).ActiveSlot > 0)
                lowAttacks++;
            if (Policy.Decide(self, target, Def, new Random(seed), highMemory).ActiveSlot > 0)
                highAttacks++;
        }

        Assert.True(highAttacks > lowAttacks,
            $"expected more level-9 follow-ups, got low={lowAttacks} high={highAttacks}");
    }

    [Fact]
    public void AttackPress_IsNotFollowedByMandatoryRetreatWindow()
    {
        var self = Self();
        var target = Opponent(z: 0.5f);
        var memory = new BotMemory();
        var rng = new Random(42);

        var first = Policy.Decide(self, target, Def, rng, memory);
        Assert.True(first.ActiveSlot > 0);

        var next = Policy.Decide(self, target, Def, rng, memory);

        Assert.Equal(0, next.ActiveSlot);
        Assert.Equal(0f, next.MoveX);
        Assert.Equal(0f, next.MoveY);
    }
}
