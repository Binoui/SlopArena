using Xunit;

namespace SlopArena.Shared.Tests;

public class KistuKitRegressionTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.KistuDef;
    private static float Gpy => TestHelpers.GroundPY(Def);

    /// <summary>
    /// Build a hold-to-charge input sequence: press+aim on tick 0, keep aiming every
    /// tick through <paramref name="holdTicks"/>. Needed to reach RMB/E's charged
    /// path (ChargeAttackAbility requires IsAiming=true on every tick to accumulate
    /// charge — an unset tick defaults to not-aiming and releases via debounce).
    /// Unused here (every scenario below uses the tap/uncharged path, matching the
    /// convention every other kit's golden file follows — charged-vs-uncharged is
    /// covered by hand-written unit tests in KistuAbilityTests.cs instead), kept for
    /// the next person who needs a genuine charged-path golden.
    /// </summary>
    private static InputSequence HoldAim(byte activeSlot, int holdTicks)
    {
        var seq = new InputSequence().Set(0, new InputState { ActiveSlot = activeSlot, IsAiming = true });
        for (int t = 1; t <= holdTicks; t++)
            seq.Set(t, new InputState { IsAiming = true });
        return seq;
    }

    [Fact]
    public void LMB_Stage1_HitsNpcFor3Damage()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu LMB Hit Confirm",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 10,   // stage 1 hitbox active (trigger=6, dur=5)
            TotalTicks = 80,
        });
    }

    [Fact]
    public void AirLMB_AirSlash_HitsAirborneNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu Air LMB",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY + 2f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 8,   // stage 1 sweep hitbox active (trigger=5, dur=5)
            TotalTicks = 80,
        });
    }

    [Fact]
    public void RMB_UnchargedPoke_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu RMB Uncharged",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence()
                .Set(0, new InputState { ActiveSlot = 2, IsAiming = true })
                .Set(10, default),  // release after debounce → tap poke, not the charged spin
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 13,   // tap-poke hitbox active (release ~tick4, trigger=6, dur=5)
            TotalTicks = 50,
        });
    }

    [Fact]
    public void AirRMB_FallingSlash_SpikesNpcDownward()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu Air RMB Falling Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 1.5f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 2),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.0f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 14,   // tap hitbox active (release ~tick5, trigger=6, dur=14 → 11-24)
            TotalTicks = 60,
        });
    }

    [Fact]
    public void E_DashSlash_AimedRelease_HitsNpcAlongPath()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu E Dash Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            // Aim toward +Z (AimYaw=0) for 3 ticks, then release → 5 m dash along +Z.
            // (InputSequence ticks without a Set send default input, so every hold tick
            // must be explicit — a default tick would read as release.)
            Inputs = new InputSequence()
                .Set(0, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 0 })
                .Set(1, new InputState { IsAiming = true, AimYaw = 0 })
                .Set(2, new InputState { IsAiming = true, AimYaw = 0 })
                .Set(3, new InputState { IsAiming = false, AimYaw = 0 }),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 2.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 12,   // mid-dash: released tick 3, dash runs ticks 4-19, sweep hits the NPC (~tick 8)
            TotalTicks = 60,
        });
    }

    [Fact]
    public void R_RisingSlash_LaunchesGroundedNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu R Rising Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 5),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.0f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 9,    // sphere hitbox active (trigger=5, dur=8)
            TotalTicks = 40,
        });
    }

    [Fact]
    public void F_BladeFlurry_EarlyHitsConnectAgainstStationaryTarget()
    {
        // NOTE for the next balance pass: against a stationary target, only the first 3 of
        // 5 flurry hits land (DamagePercent settles at 9, not the theoretical 24). Kistu's
        // own forward drift (Params.forward_speed=7, KistuUltFlurry.cs) outpaces the target's
        // Light-knockback drift and carries her past the target before the trigger=44 finisher
        // fires — confirmed at two different starting distances (0.8 and 1.5), closer was worse.
        // Unlike LMB/AirLMB/AirRMB, F's stage sets no UseTargetLock/WarpRange to re-close the
        // gap per hit. This is real current behavior (numbers are explicitly first-pass
        // placeholders per KistuData.cs), pinned as-is rather than papered over — if F ever
        // gets tracking added so the finisher reliably connects, this golden should change
        // (DamagePercent 9 -> 24, a real launch on NpcFinal) and that diff is the point.
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu F Blade Flurry",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 6),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 46,   // past all trigger windows (8,16,24,32,44) — settled post-flurry state
            TotalTicks = 90,
        });
    }
}
