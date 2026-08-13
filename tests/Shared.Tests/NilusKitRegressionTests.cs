using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Golden kit regression for Nilus. Nothing in the suite enumerates
/// <see cref="CharacterClass"/> — every harness is wired per character by hand — so a new
/// character gets zero snapshot coverage until a file like this exists.
///
/// The GOLDEN is the assertion. <see cref="KitScenario.Assert"/> is never invoked by
/// <c>AssertGoldenScenario</c> (only <c>NpcAssert</c> is, from inside
/// <see cref="ScenarioRunner"/>), so every player-side lambda here is deliberately
/// <c>_ => { }</c>, matching the Manki and FightGuy files. Per-ability behaviour is asserted
/// properly by <see cref="NilusAbilityTests"/>; what these scenarios buy is a full-state diff
/// on every field of <c>EntitySnapshot</c> at one mid-ability tick plus the settled end state.
///
/// Every <c>SnapshotTick</c> below was measured, not guessed: each one lands on a frame where
/// the ability is still doing something. A snapshot on an idle or settled frame pins nothing.
/// </summary>
public class NilusKitRegressionTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.NilusDef;
    private static float Gpy => NilusGpy;

    /// <summary>
    /// Third chain link mid-lunge. Tick 45 is 5 ticks into stage 3, whose LungeForce is 7
    /// (stages 1-2 use 5), so the snapshot pins ComboStage = 2 with VZ = 7 still undecayed.
    /// ComboStage is a 0-based index capped at 2 — it is 0 again by the settled tick 199.
    /// </summary>
    /// <summary>
    /// Stage 1 connects on tick 5. Hitstop (ADR-0015 timing, F = 1 + 1.5·damage) freezes
    /// the dummy 1 + 1.5·3 ≈ 5 ticks, so tick 9 is just past the freeze: the dummy is
    /// pinned at its spawn, 3% taken, launch queued — the snapshot pins the frozen
    /// receiver rather than a flight vector.
    /// </summary>
    [Fact]
    public void LMB_Stage1_HitConfirm()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus LMB Hit Confirm",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.2f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = n => Assert.Equal((ushort)3, n.DamagePercent),
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 9,
            TotalTicks = 80,
        });
    }

    /// <summary>
    /// Void Rake, single press: stage 1's rake connects on tick 5 for 3%. Hitstop
    /// (ADR-0015 timing, F = 1 + 1.5·damage) freezes both bodies ~5 ticks, so tick 9
    /// is just past the freeze: the dummy pinned at spawn with 3%, launch queued.
    /// The dummy must start airborne — a rake is a juggle tool, and from hover height
    /// it cannot reach a grounded capsule (honest whiff, per ADR-0015).
    /// </summary>
    [Fact]
    public void AirLMB_VoidRake_HitsAirborneDummy()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus Air LMB",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = Gpy + 4f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.2f, PY = TestHelpers.CombatGroundPY + 4f, IsGrounded = false },
            NpcAssert = n => Assert.Equal((ushort)3, n.DamagePercent),
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 9,
            TotalTicks = 120,
        });
    }

    /// <summary>
    /// The blink is instant — PZ is already 6 at the end of tick 0 — and the ability only
    /// holds Nilus for 3 more ticks. Tick 2 is the last frame still Attacking; tick 6 (and
    /// anything later) is a plain idle frame that pins neither the lock nor the charge spend.
    /// </summary>
    [Fact]
    public void E_Riftwalk_Blinks()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus E Riftwalk",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 4),
            Assert = _ => { },
            SnapshotTick = 2,
            TotalTicks = 120,
        });
    }

    /// <summary>
    /// The claw lands on tick 6 for 8% and 12 ticks of hitstun (pull_stun_ticks 20 is capped
    /// by ApplyKnockback). Hitstop (ADR-0012) freezes BOTH bodies 2 + 2·8 = 18 ticks first:
    /// tick 10 is mid-freeze — the dummy is still at 6 m with the yank queued (its KV lives
    /// in KVX/KVY/KVZ, which EntitySnapshot does not carry). The NpcAssert at TotalTicks
    /// still sees the drag: ~95 ticks of yank travel put the dummy well inside 5.5 m. Tick 20
    /// would sit inside the freeze too; only the final state pins the pull.
    /// </summary>
    [Fact]
    public void R_NetherGrasp_PullsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus R Nether Grasp",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 5),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 6f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = n => Assert.True(n.PZ < 5.5f,
                $"grasp should drag the NPC inward from 6m, PZ={n.PZ}"),
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 10,
            TotalTicks = 120,
        });
    }

    /// <summary>
    /// The rift is the one piece of Nilus' kit that outlives its own cast, so this scenario
    /// runs long enough (340 ticks) for the whole zone to expire: 8 pulses at a 30-tick rehit
    /// interval x 3% = 24% total, which the NpcAssert pins. The snapshot sits on tick 111 —
    /// the third pulse — where Nilus is deliberately Idle and free to act while the dummy is in
    /// rift hitstop (ADR-0012: zone hits freeze the receiver only). That idle caster IS the
    /// contract.
    ///
    /// It was tick 106 until Hitbox.IgnoresEntities landed. This dummy stands squarely on the
    /// seed's arc, so the seed used to clip it at flight tick 26 and burst the rift MID-AIR at
    /// (0, 1.315, 3.100); it now completes the flight and grounds at (0, 0, 3.845) five ticks
    /// later, moving every pulse tick +5. Tick 106 became a between-pulses frame with the dummy
    /// idle and grounded, which pins nothing.
    ///
    /// AimDistance is centimetres and CharacterState.AimTargetDistance is refreshed from
    /// input every tick before TickAbilities, so the release input keeps carrying 400 —
    /// dropping it would cache 0 and land the seed at minimum range.
    /// </summary>
    [Fact]
    public void Q_VoidRift_LingersAfterTheCast()
    {
        var inputs = new InputSequence();
        for (int t = 0; t < 10; t++)
            inputs.Set(t, TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 400));
        inputs.Set(10, TestHelpers.Input(aiming: false, aimDistance: 400));

        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus Q Void Rift",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = inputs,
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 4f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = n => Assert.Equal((ushort)24, n.DamagePercent),
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 111,
            TotalTicks = 340,
        });
    }

    /// <summary>
    /// <c>SnapshotTick = 131</c> is the 0-BASED RUNNER tick for ABILITY tick 132, the
    /// detonation. Both numbers name the same frame in different frames of reference:
    /// NilusEventHorizon increments _ticks first, so the blast fires on ability tick
    /// 132 = windup 72 + drag 60, which ScenarioRunner observes at the end of loop iteration
    /// 131 (KitScenario.cs:111-129). The spec and docs quote 132; this field quotes 131.
    ///
    /// It is the one frame that shows the whole ult at once:
    /// the dummy has been dragged from 5 m by six drag pulses (18%), and the
    /// detonation has just added 18% more for 36% total — with the dummy frozen in the
    /// 12-tick blast hitstop (ADR-0012: 1 + 1.5·18 = 28 → cap 12, receiver-only). The 24-tick
    /// launch stun lands at freeze expiry. (The launch vector itself lives in KVX/KVY/KVZ,
    /// which EntitySnapshot does not carry; the settled NpcFinal — flat send along the ground
    /// at 25° — is what pins its magnitude.) Anywhere inside the 72-tick telegraph pins an
    /// untouched dummy and a stationary caster.
    /// </summary>
    [Fact]
    public void F_EventHorizon_DragsThenDetonates()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Nilus F Event Horizon",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 6),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = n => Assert.True(n.PZ > 6f && n.PY > 2f,
                $"detonation should launch the NPC outward and up (flat 25° send), PZ={n.PZ} PY={n.PY}"),
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 131,
            TotalTicks = 200,
        });
    }
}
