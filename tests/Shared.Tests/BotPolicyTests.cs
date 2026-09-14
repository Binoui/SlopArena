using System;
using System.Collections.Generic;

using Xunit;

namespace SlopArena.Shared.Tests;
using SlopArena.Shared.AI;

/// <summary>
/// CPU policy decisions are driven by delayed, runner-owned opponent observations. The tests
/// inspect only the public InputState seam while retaining the existing movement, attack, lock,
/// and seeded-determinism coverage.
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

    private static void Prime(BotMemory memory, CharacterState target)
    {
        int delay = BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks;
        for (int i = 0; i <= delay; i++)
            memory.ObserveOpponent(target);
    }

    private static InputState Decide(CharacterState self, CharacterState target, int seed = 42, BotMemory? memory = null)
    {
        memory ??= new BotMemory();
        Prime(memory, target);
        return Policy.Decide(self, target, Def, new Random(seed), memory);
    }
    private static void LockNonAimSlots(ref CharacterState state)
    {
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        })
            state.SetCooldown(slot, 999);
    }

    [Fact]
    public void FarOpponent_ApproachesWithWorldSpaceMovement_NoAttack()
    {
        var self = Self();
        var target = Opponent(z: 50f); // well beyond every resolved move envelope on +Z

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
        var target = Opponent(x: 50f, z: 0f); // well beyond every resolved move envelope on +X

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
    public void ThreatResponse_HarderDifficultyCanDodgeWhileEasyWaits()
    {
        var self = Self();
        var target = Opponent(z: 0.5f);
        target.State = ActionState.Attacking;
        int hardDodges = 0;

        for (int seed = 0; seed < 100; seed++)
        {
            var easyMemory = new BotMemory { Difficulty = CpuDifficulty.Easy };
            var hardMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(easyMemory, target);
            Prime(hardMemory, target);
            var easy = Policy.Decide(self, target, Def, new Random(seed), easyMemory);
            var hard = Policy.Decide(self, target, Def, new Random(seed), hardMemory);

            Assert.False(easy.Jump);
            if (hard.Dash) hardDodges++;
        }

        Assert.True(hardDodges > 0, "Hard never selected a threat dodge across 100 seeds");
    }

    [Theory]
    [InlineData(CpuDifficulty.Easy, 24)]
    [InlineData(CpuDifficulty.Normal, 18)]
    [InlineData(CpuDifficulty.Hard, 12)]
    public void ReactionDelay_UsesOnlyInformationAtOrBeyondNamedBoundary(
        CpuDifficulty difficulty, int expectedDelay)
    {
        var self = Self();
        var oldTarget = Opponent(z: 10f);
        var changedTarget = Opponent(z: 0.5f);
        changedTarget.State = ActionState.Attacking;
        var oldMemory = new BotMemory { Difficulty = difficulty };
        var changedMemory = new BotMemory { Difficulty = difficulty };
        var oldRng = new Random(184);
        var changedRng = new Random(184);

        for (int tick = 0; tick < expectedDelay; tick++)
        {
            var oldInput = Policy.Decide(self, oldTarget, Def, oldRng, oldMemory);
            var changedInput = Policy.Decide(self, changedTarget, Def, changedRng, changedMemory);
            Assert.Equal(oldInput.MoveX, changedInput.MoveX);
            Assert.Equal(oldInput.MoveY, changedInput.MoveY);
            Assert.Equal(oldInput.ActiveSlot, changedInput.ActiveSlot);
            Assert.Equal(oldInput.Dash, changedInput.Dash);
            Assert.Equal(oldInput.Jump, changedInput.Jump);
            Assert.Equal(oldInput.AimYaw, changedInput.AimYaw);
            Assert.Equal(oldInput.FaceToCamera, changedInput.FaceToCamera);
        }

        var oldAtBoundary = Policy.Decide(self, oldTarget, Def, oldRng, oldMemory);
        var changedAtBoundary = Policy.Decide(self, changedTarget, Def, changedRng, changedMemory);
        bool changed = oldAtBoundary.MoveX != changedAtBoundary.MoveX
            || oldAtBoundary.MoveY != changedAtBoundary.MoveY
            || oldAtBoundary.ActiveSlot != changedAtBoundary.ActiveSlot
            || oldAtBoundary.Dash != changedAtBoundary.Dash
            || oldAtBoundary.Jump != changedAtBoundary.Jump;
        Assert.True(changed, "opponent change did not become observable at the delay boundary");
    }

    [Fact]
    public void ConfirmedHitMemory_EnablesMoreHardDifficultyFollowUps()
    {
        var self = Self();
        var target = Opponent(z: 0.5f);
        int easyAttacks = 0;
        int hardAttacks = 0;

        for (int seed = 0; seed < 100; seed++)
        {
            var easyMemory = new BotMemory { Difficulty = CpuDifficulty.Easy };
            var hardMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(easyMemory, target);
            Prime(hardMemory, target);
            easyMemory.RecordOpponentHit(AbilitySlots.Slot1, target, 18, 0);
            hardMemory.RecordOpponentHit(AbilitySlots.Slot1, target, 18, 0);
            for (int i = 0; i < BotDifficultyProfile.ForDifficulty(easyMemory.Difficulty).ReactionDelayTicks; i++)
                easyMemory.ObserveOpponent(target);
            for (int i = 0; i < BotDifficultyProfile.ForDifficulty(hardMemory.Difficulty).ReactionDelayTicks; i++)
                hardMemory.ObserveOpponent(target);
            if (Policy.Decide(self, target, Def, new Random(seed), easyMemory).ActiveSlot > 0)
                easyAttacks++;
            if (Policy.Decide(self, target, Def, new Random(seed), hardMemory).ActiveSlot > 0)
                hardAttacks++;
        }

        Assert.True(hardAttacks > easyAttacks,
            $"expected more Hard follow-ups, got easy={easyAttacks} hard={hardAttacks}");
    }

    [Fact]
    public void ConfirmedHit_SurvivesReactionDelayAndActionLock()
    {
        var self = Self();
        self.State = ActionState.Attacking;
        self.AttackSlot = AbilitySlots.Slot1;
        self.AnimLockTicks = 6;
        var target = Opponent(z: 0.5f);
        target.State = ActionState.Attacking;
        target.AttackSlot = AbilitySlots.Slot1;
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);
        memory.RecordOpponentHit(AbilitySlots.Slot1, target, 0, 4);

        int delay = BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks;
        var rng = new Random(42);
        for (int i = 0; i < delay; i++)
            Assert.Equal(0, Policy.Decide(self, target, Def, rng, memory).ActiveSlot);

        self.State = ActionState.Idle;
        self.AttackSlot = 0;
        self.AnimLockTicks = 0;
        var followUp = Policy.Decide(self, target, Def, rng, memory);

        Assert.True(followUp.ActiveSlot > 0,
            "confirmed hit was forgotten while the CPU was action-locked");
    }

    [Fact]
    public void AttackPress_IsNotFollowedByMandatoryRetreatWindow()
    {
        var self = Self();
        var target = Opponent(z: 0.5f);
        var memory = new BotMemory();
        var rng = new Random(42);

        Prime(memory, target);
        var first = Policy.Decide(self, target, Def, rng, memory);
        Assert.True(first.ActiveSlot > 0);

        var next = Policy.Decide(self, target, Def, rng, memory);

        Assert.Equal(0, next.ActiveSlot);
        Assert.Equal(0f, next.MoveX);
        Assert.Equal(0f, next.MoveY);
    }
    [Fact]
    public void CanonicalSecondaryGroundSlot_IsEligibleWhenOtherSlotsAreLocked()
    {
        var self = Self();
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        })
            self.SetCooldown(slot, 999);

        var target = Opponent(z: 0.75f);
        bool selected = false;
        for (int seed = 0; seed < 32 && !selected; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            selected = Policy.Decide(self, target, Def, new Random(seed), memory).ActiveSlot
                == AbilitySlots.Slot2;
        }

        Assert.True(selected, "canonical grounded slot 2 was never considered");
    }

    [Fact]
    public void EmptyAerialSlot_IsUnavailable()
    {
        var def = BuiltInContentResolver.Resolve(CharacterClass.Bonk).Definition;
        var self = TestHelpers.PlayerState();
        self.PY = TestHelpers.GroundPY(def);
        self.IsGrounded = false;
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        })
            self.SetCooldown(slot, 999);

        var target = TestHelpers.PlayerState(z: 0.5f);
        target.EntityId = 100;
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);

        var input = Policy.Decide(self, target, def, new Random(1), memory);

        Assert.Equal(0, input.ActiveSlot);
    }

    [Fact]
    public void AimedMove_HoldsThenReleasesTheResolvedAimPlan()
    {
        var def = TestHelpers.ResolveDef(CharacterClass.Manki);
        var self = TestHelpers.PlayerState();
        self.PY = TestHelpers.GroundPY(def);
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        })
            self.SetCooldown(slot, 999);

        var target = TestHelpers.PlayerState(z: 0.75f);
        target.EntityId = 100;
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);
        var initial = Policy.Decide(self, target, def, new Random(0), memory);

        Assert.Equal(AbilitySlots.A, initial.ActiveSlot);
        Assert.True(initial.IsAiming);

        self.State = ActionState.Aiming;
        self.AttackSlot = AbilitySlots.A;
        var held = Policy.Decide(self, def, new Random(0), memory);
        Assert.True(held.IsAiming);
        Assert.Equal(initial.AimYaw, held.AimYaw);
        Assert.Equal(initial.AimPitch, held.AimPitch);
        Assert.Equal(initial.AimDistance, held.AimDistance);

        InputState released = default;
        for (int i = 0; i < 12; i++)
            released = Policy.Decide(self, def, new Random(0), memory);
        Assert.False(released.IsAiming);
        Assert.Equal(0, released.ActiveSlot);
    }

    [Fact]
    public void ActionPlan_InvalidatesOnInterruptionDeathAndReset()
    {
        var def = TestHelpers.MankiDef;
        var target = Opponent(z: 0.75f);

        var interruptedMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var interruptedSelf = Self();
        LockNonAimSlots(ref interruptedSelf);
        Prime(interruptedMemory, target);
        var initial = Policy.Decide(interruptedSelf, target, def, new Random(0), interruptedMemory);
        Assert.Equal(AbilitySlots.A, initial.ActiveSlot);
        Assert.True(initial.IsAiming);

        interruptedSelf.State = ActionState.Hitstun;
        interruptedSelf.HitstunTicks = 4;
        var interrupted = Policy.Decide(interruptedSelf, def, new Random(0), interruptedMemory);
        Assert.Equal(0, interrupted.ActiveSlot);
        Assert.False(interrupted.IsAiming);

        var landingMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var landingSelf = Self();
        LockNonAimSlots(ref landingSelf);
        Prime(landingMemory, target);
        Assert.True(Policy.Decide(landingSelf, target, def, new Random(0), landingMemory).IsAiming);
        landingSelf.State = ActionState.Aiming;
        landingSelf.AttackSlot = AbilitySlots.A;
        landingSelf.IsGrounded = false;
        Assert.True(Policy.Decide(landingSelf, def, new Random(0), landingMemory).IsAiming);
        landingSelf.IsGrounded = true;
        var afterLanding = Policy.Decide(landingSelf, def, new Random(0), landingMemory);
        Assert.Equal(0, afterLanding.ActiveSlot);
        Assert.False(afterLanding.IsAiming);

        var deadMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var deadSelf = Self();
        LockNonAimSlots(ref deadSelf);
        Prime(deadMemory, target);
        Assert.True(Policy.Decide(deadSelf, target, def, new Random(0), deadMemory).IsAiming);
        deadSelf.Deaths = 1;
        var afterDeath = Policy.Decide(deadSelf, def, new Random(0), deadMemory);
        Assert.Equal(0, afterDeath.ActiveSlot);
        Assert.False(afterDeath.IsAiming);

        var resetMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var resetSelf = Self();
        LockNonAimSlots(ref resetSelf);
        Prime(resetMemory, target);
        Assert.True(Policy.Decide(resetSelf, target, def, new Random(0), resetMemory).IsAiming);
        resetMemory.Reset();
        resetSelf.State = ActionState.Aiming;
        resetSelf.AttackSlot = AbilitySlots.A;
        var afterReset = Policy.Decide(resetSelf, def, new Random(0), resetMemory);
        Assert.Equal(0, afterReset.ActiveSlot);
        Assert.False(afterReset.IsAiming);
    }

    [Fact]
    public void AimedPlan_ReachesResolverThroughPolicyAcrossSeeds()
    {
        var def = TestHelpers.MankiDef;
        for (int seed = 0; seed < 8; seed++)
        {
            var sim = TestHelpers.MakeSim();
            var self = Self();
            self.PY = TestHelpers.GroundPY(def);
            foreach (var slot in new[]
            {
                AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
                AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
            })
                self.SetCooldown(slot, 999);

            var target = Opponent(z: 2f);
            target.PY = TestHelpers.GroundPY(def);
            TestHelpers.RegisterPlayer(sim, def, self);
            TestHelpers.RegisterNpc(sim, def, target);

            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            var rng = new Random(seed);
            bool aimed = false;
            bool hit = false;
            for (int tick = 0; tick < 180 && !hit; tick++)
            {
                var currentSelf = sim.GetState(1);
                var currentTarget = sim.GetState(100);
                var input = Policy.Decide(currentSelf, currentTarget, def, rng, memory);
                aimed |= input.IsAiming;
                sim.Tick(new Dictionary<ulong, InputState>
                {
                    [1] = input,
                    [100] = default,
                });
                hit = sim.LastTickHits.Exists(x =>
                    x.OwnerEntityId == 1 && x.TargetEntityId == 100 && x.Damage > 0f);
            }

            Assert.True(aimed, $"seed {seed} never entered the aim plan");
            Assert.True(hit, $"seed {seed} never produced a resolver hit");
        }
    }

    [Fact]
    public void DirectionalPlan_ExecutesMovementThroughSimulationAcrossSeeds()
    {
        var def = TestHelpers.KistuDef;
        for (int seed = 0; seed < 8; seed++)
        {
            var sim = TestHelpers.MakeSim();
            var self = Self();
            self.PY = TestHelpers.GroundPY(def);
            foreach (var slot in new[]
            {
                AbilitySlots.Lmb, AbilitySlots.Rmb, AbilitySlots.Slot1, AbilitySlots.E,
                AbilitySlots.F, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
                AbilitySlots.Slot5, AbilitySlots.A,
            })
                self.SetCooldown(slot, 999);

            var target = Opponent(z: 2f);
            target.PY = TestHelpers.GroundPY(def);
            TestHelpers.RegisterPlayer(sim, def, self);
            TestHelpers.RegisterNpc(sim, def, target);

            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            var rng = new Random(seed);
            bool aimed = false;
            bool moved = false;
            for (int tick = 0; tick < 180 && !moved; tick++)
            {
                var input = Policy.Decide(sim.GetState(1), sim.GetState(100), def, rng, memory);
                aimed |= input.IsAiming;
                sim.Tick(new Dictionary<ulong, InputState>
                {
                    [1] = input,
                    [100] = default,
                });
                moved = sim.GetState(1).PZ > 1f;
            }

            Assert.True(aimed, $"seed {seed} never entered the directional plan");
            Assert.True(moved, $"seed {seed} never moved through the directional dash");
        }
    }

}
