using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// IASA early-out (issue #124): attack stages can declare an IasaTicks unlock point.
/// From that tick on, any ability input interrupts the recovery; before it the full
/// ADR-0014 lock applies; with no input the move completes at its normal duration.
/// IasaTicks = 0 (default) preserves the full-lock behavior — no existing ability
/// changes until a stage authors the field.
/// </summary>
public class IasaTests : KitScenarioTests
{
    // Jab-like LMB: 21-tick lock, unlocks at tick 16 (5 early). Lunge 3.
    private const ushort LmbDuration = 21;
    private const ushort LmbIasa = 16;
    private const float LmbLunge = 3f;
    // Slot1 (key "1"): longer move with a stronger lunge — the interrupt target.
    private const ushort Slot1Duration = 30;
    private const float Slot1Lunge = 5f;

    private static readonly CharacterDefinition Def = MakeIasaDef(iasa: true);

    /// <summary>
    /// FightGuy with LMB (jab, IASA at 16) and Slot1 (long move) both driven by
    /// LmbCombo/StageChainAbility — the standard stage machinery, so the test
    /// exercises the engine gate rather than a bespoke ability.
    /// </summary>
    private static CharacterDefinition MakeIasaDef(bool iasa)
    {
        var def = TestHelpers.CloneDef(TestHelpers.KistuDef);
        def.LMB = new AbilitySpec
        {
            Name = "IASA Jab",
            CooldownTicks = 0,
            AnimationNames = new[] { "iasa_jab" },
            Stages = new[]
            {
                new AttackStage
                {
                    DurationTicks = LmbDuration,
                    IasaTicks = iasa ? LmbIasa : (ushort)0,
                    LungeForce = LmbLunge,
                    HitboxEvents = System.Array.Empty<HitboxEvent>(),
                },
            },
        };
        def.Slot1 = new AbilitySpec
        {
            Name = "IASA Long",
            CooldownTicks = 0,
            AnimationNames = new[] { "iasa_long" },
            Stages = new[]
            {
                new AttackStage
                {
                    DurationTicks = Slot1Duration,
                    IasaTicks = 0,
                    LungeForce = Slot1Lunge,
                    HitboxEvents = System.Array.Empty<HitboxEvent>(),
                },
            },
        };
        return def;
    }

    private static float Gpy => TestHelpers.GroundPY(Def);

    /// <summary>
    /// Press Slot1 exactly at the IASA tick: the jab's recovery is interrupted and the
    /// Slot1 move starts immediately (its lunge velocity + AttackSlot are captured mid-move).
    /// </summary>
    [Fact]
    public void Iasa_ActAtUnlock_InterruptsIntoSlot1()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "IASA Act At Unlock Interrupts Into Slot1",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, AbilitySlots.Lmb).Press(16, AbilitySlots.Slot1),
            Assert = _ => { },
            SnapshotTick = 18,   // mid Slot1 attack — lunge 5, AttackSlot 3
            TotalTicks = 60,     // Slot1 (30t, started t16) ends t46; final Idle
        });
    }

    /// <summary>
    /// Press Slot1 two ticks BEFORE the IASA tick: the old lock applies, the jab keeps
    /// running (press is outside the 6-tick buffer window, so it is dropped entirely)
    /// and no second attack follows.
    /// </summary>
    [Fact]
    public void Iasa_LockedBeforeUnlock_SameAttackContinues()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "IASA Locked Before Unlock Attack Continues",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, AbilitySlots.Lmb).Press(14, AbilitySlots.Slot1),
            Assert = _ => { },
            SnapshotTick = 18,   // still the jab — lunge 3, AttackSlot 1, press dropped
            TotalTicks = 60,     // jab ends t21; final Idle (no second attack)
        });
    }

    /// <summary>
    /// No input at all: the IASA unlock never cuts the animation — the jab still runs
    /// its full 21-tick duration (snapshot one tick before the end) and returns to Idle.
    /// </summary>
    [Fact]
    public void Iasa_NoInput_MoveCompletesAtFullDuration()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "IASA No Input Attack Completes At Full Duration",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, AbilitySlots.Lmb),
            Assert = _ => { },
            SnapshotTick = 19,   // last live tick — unlock never cut the animation
            TotalTicks = 60,
        });
    }

    /// <summary>
    /// Jab that connects a hit at t10 (damage 2 → 8-tick attacker freeze per ADR-0012),
    /// with a very early IASA unlock (t4) so the unlock is live during the freeze.
    /// The hitstop hard-block: a press at t14 (IASA-unlocked, attacker mid-freeze) must
    /// NOT interrupt — only the AnimLockTicks term relaxes. After the freeze expires
    /// (t18), the same press does interrupt.
    /// </summary>
    [Fact]
    public void Iasa_DoesNotInterruptDuringAttackerHitstop_ButDoesAfterFreeze()
    {
        var def = TestHelpers.CloneDef(TestHelpers.KistuDef);
        def.LMB = new AbilitySpec
        {
            Name = "IASA Jab Hit",
            CooldownTicks = 0,
            AnimationNames = new[] { "iasa_jab" },
            Stages = new[]
            {
                new AttackStage
                {
                    DurationTicks = 40,
                    IasaTicks = 4,
                    LungeForce = 0f,
                    HitboxEvents = new[]
                    {
                        new HitboxEvent
                        {
                            TriggerTick = 10,
                            DurationTicks = 4,
                            Radius = 1.2f,
                            OffY = 0.4f,
                            OffZ = 1.0f,
                            Damage = 2f,
                            Knockback = new KnockbackData { Profile = KnockbackProfile.Light },
                            StunTicks = 20,
                            Interruptible = true,
                        },
                    },
                },
            },
        };
        def.Slot1 = new AbilitySpec
        {
            Name = "IASA Long",
            CooldownTicks = 0,
            AnimationNames = new[] { "iasa_long" },
            Stages = new[]
            {
                new AttackStage
                {
                    DurationTicks = 30,
                    IasaTicks = 0,
                    LungeForce = 5f,
                    HitboxEvents = System.Array.Empty<HitboxEvent>(),
                },
            },
        };

        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(100, TestHelpers.CombatDef,
            TestHelpers.NpcState() with { PX = 0, PZ = 1.2f, PY = TestHelpers.CombatGroundPY });

        // LMB at t0; Slot1 press at t14: past the IASA unlock (elapsed 10 >= 4) but the
        // attacker is still frozen by its own connecting hit (freeze t10..t17).
        var inputs = new Dictionary<ulong, InputState>();
        for (int t = 0; t <= 14; t++)
        {
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Lmb)
                : t == 14 ? TestHelpers.Input(AbilitySlots.Slot1)
                : default;
            inputs[100] = default;
            sim.Tick(inputs);
        }

        var frozen = sim.GetState(1);
        Assert.True(frozen.HitstopTicks > 0, "test window: attacker must still be mid-freeze");
        Assert.Equal(AbilitySlots.Lmb, frozen.AttackSlot);   // hitstop hard-blocks the press

        // Continue; fresh Slot1 press at t19 (freeze expired at t17) — the unlock is still
        // live (the freeze paused the elapsed clock, it did not cancel the attack).
        for (int t = 15; t <= 19; t++)
        {
            inputs[1] = t == 19 ? TestHelpers.Input(AbilitySlots.Slot1) : default;
            inputs[100] = default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        Assert.Equal((byte)0, after.HitstopTicks);
        Assert.Equal(AbilitySlots.Slot1, after.AttackSlot);  // interrupt fires once unfrozen
    }

    /// <summary>
    /// Engine-capability control: with IasaTicks = 0 the press at the would-be unlock
    /// tick does NOT interrupt — the jab keeps running on its original clock and the
    /// press goes to the standard input buffer (fires at lock expiry, pre-IASA behavior).
    /// </summary>
    [Fact]
    public void IasaTicksZero_KeepsFullLock_PressAtUnlockTickIsNotAnInterrupt()
    {
        var def = MakeIasaDef(iasa: false);
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });

        var inputs = new Dictionary<ulong, InputState>();
        for (int t = 0; t <= 16; t++)
        {
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Lmb)
                : t == 16 ? TestHelpers.Input(AbilitySlots.Slot1)
                : default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        // No interrupt: the jab is still running on its original clock...
        Assert.Equal(AbilitySlots.Lmb, after.AttackSlot);
        Assert.Equal((ushort)17, after.AttackElapsedTicks); // an interrupt would reset to 1
        // ...and the press was buffered for the normal lock expiry, not consumed early.
        Assert.Equal(AbilitySlots.Slot1, after.BufferedSlot);
    }

    /// <summary>
    /// Counterpart of IasaTicksZero: with IasaTicks = 16 the same press at tick 16 is
    /// consumed by the interrupt — the Slot1 move starts immediately.
    /// </summary>
    [Fact]
    public void IasaTicksSet_PressAtUnlockTick_ConsumedByInterrupt()
    {
        var def = MakeIasaDef(iasa: true);
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });

        var inputs = new Dictionary<ulong, InputState>();
        for (int t = 0; t <= 16; t++)
        {
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Lmb)
                : t == 16 ? TestHelpers.Input(AbilitySlots.Slot1)
                : default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        Assert.Equal(AbilitySlots.Slot1, after.AttackSlot);   // new move took over
        Assert.Equal((ushort)1, after.AttackElapsedTicks);    // new attack, tick 1
        Assert.Equal((byte)0, after.BufferedSlot);            // press consumed, nothing buffered
    }
}
