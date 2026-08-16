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
    public void G1_QuickSlash_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu G1 Quick Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 3),
            Assert = _ => { },
            // PZ 0.6, not the usual 1.5: g_1's slash is a HIGH, close-range sweep — the
            // hand starts behind center and the blade stays at chest/head height (Y ~1.5-1.8),
            // so it clips an opponent only within ~0.6 m (the old entity capsule reached
            // 1.8 m). PZ 0.6 pins the blade connection at the sweep's apex (tick 11);
            // reach tuning is an Ability Lab item, the mechanism is the deliverable.
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 0.6f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 11,   // stage hitbox active (trigger=9, dur=5)
            TotalTicks = 60,
        });
    }

    [Fact]
    public void G2_DoubleSlash_SecondHitConnects()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu G2 Double Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 7),
            Assert = _ => { },
            // PZ 0.7, not 1.5: hit 2's blade is forward only at tick 25 (reach ~1.2 m;
            // the old entity capsule reached 1.9 m), and hit 1's knockback drifts the
            // NPC ~0.4 m by then — starting at 1.5 puts it out of hit 2's reach.
            // PZ 0.7 keeps both hits connecting (pins 3+6=9).
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 0.7f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 32,   // hit 2 connects at tick 31 (3rd pulse; blade forward at elapsed 25) — pins 3+6=9
            TotalTicks = 70,
        });
    }

    [Fact]
    public void G3_UpSlash_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu G3 Up Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 8),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 9,    // stage hitbox active (trigger=6, dur=7)
            TotalTicks = 60,
        });
    }

    [Fact]
    public void G4_HeavyDownSlash_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu G4 Heavy Down Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 9),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            // SnapshotTick 27, not 24: g_4's blade slams forward at the END of the
            // window (tip reaches Z 1.52 at tick 26) — the tip sweetspot connects at
            // tick 26 with its higher 14 dmg, proving the opt-in sweetspot mechanism.
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 27,   // sweetspot connects at tick 26 (window 21-26)
            TotalTicks = 70,
        });
    }

    [Fact]
    public void A1_AirSlash_HitsAirborneNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu A1 Air Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 3),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY + 2f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 8,    // stage hitbox active (trigger=6, dur=5)
            TotalTicks = 70,
        });
    }

    [Fact]
    public void A2_ReverseSlash_HitsAirborneNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu A2 Reverse Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 7),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY + 2f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 6,    // stage hitbox active (trigger=4, dur=5)
            TotalTicks = 70,
        });
    }

    [Fact]
    public void A3_AirUpSlash_HitsAirborneNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu A3 Air Up Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 8),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY + 2f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 5,    // stage hitbox active (trigger=3, dur=6)
            TotalTicks = 70,
        });
    }

    [Fact]
    public void A4_AirHeavyDownSlash_SpikesNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu A4 Air Heavy Down Slash",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 9),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY + 2f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 15,   // stage hitbox active (trigger=12, dur=7)
            TotalTicks = 80,
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
