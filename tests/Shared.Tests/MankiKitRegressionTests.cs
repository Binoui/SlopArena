using Xunit;

namespace SlopArena.Shared.Tests;

public class MankiKitRegressionTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float Gpy = MankiGpy;

    [Fact]
    public void LMB_Stage1_HitsNpcFor4Damage()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki LMB Hit Confirm",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 15,   // hitbox active (trigger=12, dur=8)
            TotalTicks = 80,
        });
    }

    [Fact]
    public void AirLMB_HitsAirborneNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki Air LMB",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY + 2f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 10,   // stage 1 hitbox active (trigger=6, dur=6)
            TotalTicks = 80,
        });
    }

    [Fact]
    public void Overclock_Applies480TickBuff()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki Overclock",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 6),
            Assert = _ => { },
            SnapshotTick = 10,   // buff applied, BuffRemainingTicks should be ~470
            TotalTicks = 60,
        });
    }

    [Fact]
    public void QBomb_ThrowAnimation_CompletesWithCooldown()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki Q Bomb",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = Gpy, FacingYaw = 0 },
            Inputs = new InputSequence()
                .Set(0, new InputState { ActiveSlot = 3, IsAiming = true, AimYaw = 0, AimDistance = 500 }),
            Assert = _ => { },
            SnapshotTick = 30,   // mid-throw, cooldown active
            TotalTicks = 100,
        });
    }
}
