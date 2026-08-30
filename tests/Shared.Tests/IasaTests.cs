using System.Collections.Generic;
using System.Linq;
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
    // Slot1: 21-tick lock, unlocks at tick 16 (5 early). Lunge 3.
    private const ushort Slot1Duration = 21;
    private const ushort Slot1Iasa = 16;
    private const float Slot1Lunge = 3f;
    // Slot2: longer move with a stronger lunge — the interrupt target.
    private const ushort Slot2Duration = 30;
    private const float Slot2Lunge = 5f;

    private static readonly CharacterDefinition Def = MakeIasaDef(iasa: true);

    private static CookedSlotDefinition TestSlot(
        int ordinal,
        string id,
        ushort durationTicks,
        ushort iasaTicks,
        params CookedTimelineOperation[] operations)
        => new(
            ordinal,
            id,
            false,
            "Test",
            "Test",
            "icon.test",
            AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None,
            0,
            false,
            false,
            new CookedTimeline(new[]
            {
                new CookedStage(
                    durationTicks,
                    iasaTicks,
                    0,
                    0,
                    0,
                    System.Array.Empty<string>(),
                    operations),
            }));

    private static CookedTimelineOperation[] VelocityOperations(float forwardSpeed, ushort ticks = 10)
        => Enumerable.Range(0, ticks)
            .Select(tick => (CookedTimelineOperation)new CookedSetVelocityOperation(
                (ushort)tick,
                AuthoringUnit.MetersPerSecond,
                AuthoringVelocityMode.Absolute,
                0f,
                0f,
                forwardSpeed))
            .ToArray();

    /// <summary>
    /// Kistu movement/body data with cooked Slot1 (IASA at 16) and Slot2 timelines.
    /// The synthetic cooked fixtures exercise the engine gate without legacy factory behavior.
    /// </summary>
    private static CharacterDefinition MakeIasaDef(bool iasa)
    {
        var def = TestHelpers.CloneDef(TestHelpers.KistuDef);
        var slots = TestHelpers.KistuDef.CookedSlots!.ToArray();
        slots[0] = TestSlot(0, "ground.1", Slot1Duration, iasa ? Slot1Iasa : (ushort)0, VelocityOperations(Slot1Lunge));
        slots[1] = TestSlot(1, "ground.2", Slot2Duration, 0, VelocityOperations(Slot2Lunge));
        def.CookedSlots = slots;
        return def;
    }

    private static float Gpy => TestHelpers.GroundPY(Def);

    /// <summary>
    /// Press Slot2 exactly at the IASA tick: Slot1's recovery is interrupted and the
    /// Slot2 move starts immediately (its lunge velocity + AttackSlot are captured mid-move).
    /// </summary>
    [Fact]
    public void Iasa_ActAtUnlock_InterruptsIntoSlot2()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "IASA Act At Unlock Interrupts Into Slot2",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1).Press(16, AbilitySlots.Slot2),
            Assert = _ => { },
            SnapshotTick = 18,   // mid Slot2 attack — lunge 5, AttackSlot 7
            TotalTicks = 60,     // Slot2 (30t, started t16) ends t46; final Idle
        });
    }

    /// <summary>
    /// Press Slot2 two ticks BEFORE the IASA tick: the old lock applies, Slot1 keeps
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
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1).Press(14, AbilitySlots.Slot2),
            Assert = _ => { },
            SnapshotTick = 18,   // still Slot1 — lunge 3, AttackSlot 3, press dropped
            TotalTicks = 60,     // Slot1 ends t21; final Idle (no second attack)
        });
    }

    /// <summary>
    /// No input at all: the IASA unlock never cuts the animation — Slot1 still runs
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
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1),
            Assert = _ => { },
            SnapshotTick = 19,   // last live tick — unlock never cut the animation
            TotalTicks = 60,
        });
    }

    /// <summary>
    /// Slot1 connects a hit at t10 (damage 2 → 8-tick attacker freeze per ADR-0012),
    /// with a very early IASA unlock (t4) so the unlock is live during the freeze.
    /// The hitstop hard-block: a Slot2 press at t14 (IASA-unlocked, attacker mid-freeze)
    /// must NOT interrupt. After the freeze expires (t18), the same press does interrupt.
    /// </summary>
    [Fact]
    public void Iasa_DoesNotInterruptDuringAttackerHitstop_ButDoesAfterFreeze()
    {
        var def = TestHelpers.CloneDef(TestHelpers.KistuDef);
        var slots = TestHelpers.KistuDef.CookedSlots!.ToArray();
        slots[0] = TestSlot(
            0,
            "ground.1",
            40,
            4,
            new CookedSpawnHitboxOperation(
                10,
                AuthoringUnit.Meters,
                new CookedHitbox(
                    AuthoringHitboxShape.Sphere,
                    1.2f,
                    0f,
                    0.4f,
                    1f,
                    0f,
                    0f,
                    0f,
                    null,
                    null,
                    2f,
                    15f,
                    2f,
                    1.5f,
                    20,
                    4,
                    true,
                    0)));
        slots[1] = TestSlot(1, "ground.2", Slot2Duration, 0, VelocityOperations(Slot2Lunge));
        def.CookedSlots = slots;

        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(100, TestHelpers.CombatDef,
            TestHelpers.NpcState() with { PX = 0, PZ = 1.2f, PY = TestHelpers.CombatGroundPY });

        // Slot1 at t0; Slot2 press at t14: past the IASA unlock (elapsed 10 >= 4) but
        // the attacker is still frozen by its own connecting hit (freeze t10..t17).
        var inputs = new Dictionary<ulong, InputState>();
        for (int t = 0; t <= 14; t++)
        {
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Slot1)
                : t == 14 ? TestHelpers.Input(AbilitySlots.Slot2)
                : default;
            inputs[100] = default;
            sim.Tick(inputs);
        }

        var frozen = sim.GetState(1);
        Assert.True(frozen.HitstopTicks > 0, "test window: attacker must still be mid-freeze");
        Assert.Equal(AbilitySlots.Slot1, frozen.AttackSlot);   // hitstop hard-blocks the press

        // Continue; fresh Slot2 press at t19 (freeze expired at t17) — the unlock is still
        // live (the freeze paused the elapsed clock, it did not cancel the attack).
        for (int t = 15; t <= 19; t++)
        {
            inputs[1] = t == 19 ? TestHelpers.Input(AbilitySlots.Slot2) : default;
            inputs[100] = default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        Assert.Equal((byte)0, after.HitstopTicks);
        Assert.Equal(AbilitySlots.Slot2, after.AttackSlot);  // interrupt fires once unfrozen
    }

    /// <summary>
    /// Engine-capability control: with IasaTicks = 0 the press at the would-be unlock
    /// tick does NOT interrupt — Slot1 keeps running on its original clock and the
    /// Slot2 press goes to the standard input buffer (fires at lock expiry).
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
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Slot1)
                : t == 16 ? TestHelpers.Input(AbilitySlots.Slot2)
                : default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        // No interrupt: Slot1 is still running on its original clock...
        Assert.Equal(AbilitySlots.Slot1, after.AttackSlot);
        Assert.Equal((ushort)17, after.AttackElapsedTicks); // an interrupt would reset to 1
        // ...and the press was buffered for the normal lock expiry, not consumed early.
        Assert.Equal(AbilitySlots.Slot2, after.BufferedSlot);
    }

    /// <summary>
    /// Counterpart of IasaTicksZero: with IasaTicks = 16 the same Slot2 press at tick 16
    /// is consumed by the interrupt and the Slot2 move starts immediately.
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
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Slot1)
                : t == 16 ? TestHelpers.Input(AbilitySlots.Slot2)
                : default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        Assert.Equal(AbilitySlots.Slot2, after.AttackSlot);   // new move took over
        Assert.Equal((ushort)1, after.AttackElapsedTicks);    // new attack, tick 1
        Assert.Equal((byte)0, after.BufferedSlot);            // press consumed, nothing buffered
    }
}
