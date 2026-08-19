using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Issue #147 — move-data analysis contracts, validated against the real sim:
/// the DI escape-space mechanic (<see cref="Simulation.ApplyDirectionalInfluence"/>,
/// 18° cap, Melee sin² curve) and the true-combo timing criterion (a follow-up's
/// damage landing while the victim is still in hitstun). These are the game-behavior
/// contracts the MoveDataReport tool's --di and --truecombos modes build on.
/// </summary>
public class MoveDataAnalysisTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);

    private static float Deg(float rad) => rad * 180f / MathF.PI;

    private static float AngleDeg(float ax, float ay, float az, float bx, float by, float bz)
    {
        float ma = MathF.Sqrt(ax * ax + ay * ay + az * az);
        float mb = MathF.Sqrt(bx * bx + by * by + bz * bz);
        if (ma <= 0.0001f || mb <= 0.0001f) return 0f;
        return Deg(MathF.Acos(Math.Clamp((ax * bx + ay * by + az * bz) / (ma * mb), -1f, 1f)));
    }

    // ── DI rotation (Simulation.ApplyDirectionalInfluence) ──────────────────

    [Fact]
    public void DirectionalInfluence_PerpendicularHold_RotatesByFull18Degrees_MagnitudePreserved()
    {
        var s = TestHelpers.PlayerState();
        s.KVZ = 10f;          // launch along +Z
        s.DIX = 1f; s.DIY = 0f; // perpendicular hold — max bend

        Simulation.ApplyDirectionalInfluence(ref s);

        float mag = MathF.Sqrt(s.KVX * s.KVX + s.KVY * s.KVY + s.KVZ * s.KVZ);
        TestHelpers.AssertNear(10f, mag, 0.001f);
        // rotated toward +X by exactly the 18° cap
        TestHelpers.AssertNear(10f * MathF.Sin(18f * MathF.PI / 180f), s.KVX, 0.001f);
        TestHelpers.AssertNear(10f * MathF.Cos(18f * MathF.PI / 180f), s.KVZ, 0.001f);
        TestHelpers.AssertNear(0f, s.KVY, 0.001f);
    }

    [Fact]
    public void DirectionalInfluence_AlongAxisHold_DoesNotRotate()
    {
        // Melee sin² curve: holding WITH the launch (0°) or OPPOSITE it (180°) bends nothing.
        var with = TestHelpers.PlayerState();
        with.KVZ = 10f;
        with.DIX = 0f; with.DIY = 1f;
        Simulation.ApplyDirectionalInfluence(ref with);
        TestHelpers.AssertNear(0f, with.KVX, 0.001f);
        TestHelpers.AssertNear(10f, with.KVZ, 0.001f);

        var against = TestHelpers.PlayerState();
        against.KVZ = 10f;
        against.DIX = 0f; against.DIY = -1f;
        Simulation.ApplyDirectionalInfluence(ref against);
        TestHelpers.AssertNear(0f, against.KVX, 0.001f);
        TestHelpers.AssertNear(10f, against.KVZ, 0.001f);
    }

    [Fact]
    public void DirectionalInfluence_DiagonalHold_UsesSin2Curve()
    {
        // 45° from the launch direction → turn = 18° · sin²(45°) = 9°.
        var s = TestHelpers.PlayerState();
        s.KVZ = 10f;
        s.DIX = 1f; s.DIY = 1f;
        Simulation.ApplyDirectionalInfluence(ref s);

        float expected = 9f * MathF.PI / 180f;
        TestHelpers.AssertNear(10f * MathF.Sin(expected), s.KVX, 0.001f);
        TestHelpers.AssertNear(10f * MathF.Cos(expected), s.KVZ, 0.001f);
    }

    [Fact]
    public void DirectionalInfluence_DeviationNeverExceeds18Degrees()
    {
        // Sweep launch directions; the applied turn must never exceed the 18° cap.
        for (int i = 0; i <= 36; i++)
        {
            float ang = i * 5f * MathF.PI / 180f;
            float bx = 10f * MathF.Sin(ang), by = 5f, bz = 10f * MathF.Cos(ang);
            var s = TestHelpers.PlayerState();
            s.KVX = bx; s.KVY = by; s.KVZ = bz;
            s.DIX = 1f; s.DIY = 0f;
            Simulation.ApplyDirectionalInfluence(ref s);

            float dev = AngleDeg(s.KVX, s.KVY, s.KVZ, bx, by, bz);
            Assert.True(dev <= 18f + 0.01f, $"deviation {dev:F2}° exceeds the 18° cap at launch angle {i * 5}°");
        }
    }

    // ── DI trajectory delta (real sim: held DI through hitstun bends the flight) ──

    /// <summary>Launch the victim with g2 Straight Punch's authored knockback, apply the given DI hold at
    /// launch (via the sim's real ApplyDirectionalInfluence), hold the stick through hitstun, and
    /// return the landing point.</summary>
    private static (float PX, float PZ) FlightLanding(float diX, float diY)
    {
        var def = TestHelpers.FightGuyDef;
        var hit = def.Slot2!.Stages[0].HitboxEvents[0];
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.DamagePercent = (ushort)hit.Damage;
        Simulation.ApplyKnockback(ref state, 0f, 1f, hit.Knockback.Angle, hit.Knockback.BaseKnockback,
            hit.Knockback.KnockbackGrowth, hit.Damage, hit.StunTicks, def.Weight);
        state.DIX = diX; state.DIY = diY;
        Simulation.ApplyDirectionalInfluence(ref state);
        sim.RegisterEntity(1, def, state);

        var inputs = new Dictionary<ulong, InputState>();
        for (int t = 0; t < 600; t++)
        {
            var s0 = sim.GetState(1);
            // Hold the stick only while in hitstun — post-stun the victim is passive.
            inputs[1] = s0.HitstunTicks > 0 ? new InputState { MoveX = diX, MoveY = diY } : default;
            sim.Tick(inputs);
            var s = sim.GetState(1);
            if (s.IsGrounded && s.HitstunTicks == 0 && s.HitstopTicks == 0)
                return (s.PX, s.PZ);
        }
        throw new Xunit.Sdk.XunitException("victim never landed within 600 ticks");
    }

    [Fact]
    public void DiHeldThroughHitstun_BendsLaunch_ChangesLandingPoint()
    {
        var (px0, pz0) = FlightLanding(0f, 0f); // baseline, no DI
        var (px1, pz1) = FlightLanding(1f, 0f); // perpendicular hold — full 18° bend + ASDI

        // The bend must put the landing point off the launch axis...
        Assert.True(px1 > 0.5f, $"perpendicular DI should bend the launch off the +Z axis, got PX={px1:F2}");
        Assert.True(px1 > px0 + 0.3f, $"DI landing PX ({px1:F2}) should exceed baseline ({px0:F2})");
        // ...and steal forward travel (the rotated launch's +Z component shrinks).
        Assert.True(pz1 < pz0 - 0.2f, $"DI bend should reduce forward travel (baseline {pz0:F2} vs DI {pz1:F2})");
    }

    // ── True-combo timing criterion (real sim) ───────────────────────────────

    /// <summary>
    /// FightGuy with a synthetic fast-IASA jab: the sim derives hitstun from launch speed
    /// (0.7 × (unscaled KB magnitude + 20), ADR-0019 melee-shape — authored StunTicks is a
    /// zero/nonzero gate only), so a follow-up that stays in reach gets only a few stun ticks.
    /// The positive case therefore needs a jab whose IASA is ~1 tick after its trigger
    /// (recovery ~0) plus moderate KB: stun 9+ ticks vs a 5-tick follow-up budget. The
    /// authored kit has no such move; this synthetic one proves the timing criterion.
    /// </summary>
    private static CharacterDefinition FastIasaJabDef()
    {
        var def = TestHelpers.CloneDef(TestHelpers.FightGuyDef);
        def.Slot1 = new AbilitySpec
        {
            Name = "Fast Jab",
            CooldownTicks = 0,
            Stages = new[]
            {
                new AttackStage
                {
                    DurationTicks = 17, IasaTicks = 5,
                    HitboxEvents = new[]
                    {
                        new HitboxEvent
                        {
                            TriggerTick = 4, DurationTicks = 5, Radius = 0.35f, OffX = 0f, OffY = 0f, OffZ = 0.21f,
                            BoneName = "mixamorig:RightFoot", Damage = 4f,
                            Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 8, BaseKnockback = 5f, KnockbackGrowth = 14f },
                            StunTicks = 40, Interruptible = true,
                        },
                    },
                    AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                },
            },
            AnimationNames = new[] { "spell_g_1" },
        };
        return def;
    }

    /// <summary>
    /// Drive the real hit path: attacker presses slot <paramref name="slotByte"/> at t=0, then presses
    /// it again at the earliest legal frame — the game's IASA early-out (a press interrupts the
    /// recovery once the starter stage has passed its <c>IasaTicks</c>, even mid-attack), falling
    /// back to Idle. Victim is passive at z=<paramref name="victimZ"/>. Returns the tick the
    /// follow-up's damage landed and the victim's hitstun at that tick, or null if it never connected.
    /// </summary>
    private static (int Tick, int StunLeft)? ScriptedFollowUp(CharacterDefinition def, byte slotByte,
        int starterIasaTicks, float victimZ)
    {
        var sim = TestHelpers.MakeSim();
        var baked = TestHelpers.LoadBakedData(def);
        var atk = TestHelpers.PlayerState();
        atk.PY = GroundPY;
        sim.RegisterEntity(1, def, atk, baked);
        var vic = TestHelpers.NpcState(0f, victimZ);
        vic.PY = GroundPY;
        sim.RegisterEntity(100, def, vic, baked);

        var inputs = new Dictionary<ulong, InputState>();
        bool pressed2 = false;
        for (int t = 0; t < 400; t++)
        {
            var a = sim.GetState(1);
            var input = new InputState();
            if (t == 0) input.ActiveSlot = slotByte;
            else if (!pressed2 && a.HitstunTicks == 0 && a.HitstopTicks == 0
                     && a.LandingLagTicks == 0 && a.BurstRecoveryTicks == 0)
            {
                bool iasa = a.State == ActionState.Attacking && starterIasaTicks > 0
                    && a.AttackElapsedTicks >= starterIasaTicks;
                if ((a.AnimLockTicks == 0 || iasa)
                    && (a.State == ActionState.Idle || a.State == ActionState.Run || iasa))
                {
                    input.ActiveSlot = slotByte; // earliest legal follow-up press (IASA interrupt)
                    pressed2 = true;
                }
            }
            inputs[1] = input;
            inputs[100] = default;
            sim.Tick(inputs);

            var v = sim.GetState(100);
            if (pressed2 && v.DamagePercent > 4)
                return (t, v.HitstunTicks);
        }
        return null;
    }

    [Fact]
    public void TrueCombo_FollowUpLandsWhileVictimStunned_IsTrue()
    {
        var def = FastIasaJabDef();
        var result = ScriptedFollowUp(def, 3 /* key 1 = slot 1 */, starterIasaTicks: 5, victimZ: 1.0f);

        Assert.NotNull(result);
        Assert.True(result.Value.StunLeft > 0,
            $"follow-up connected at t={result.Value.Tick} but the victim was NOT in hitstun (stunLeft={result.Value.StunLeft})");
    }

    [Fact]
    public void TrueCombo_LowKickToLowKick_WhiffsUnderHitReactionFacing()
    {
        // g1→g1 was the dataset's paper-true edge under the adopted Melee-shape curve
        // (stun 0.7·(mag+20) = 31 at 0%, KV×0.11): the stun budget comfortably outlasts
        // the 13-tick IASA + 4-tick trigger chain. But the scripted follow-up NO LONGER
        // connects: the hit-reaction facing change (victim turns to face the attacker,
        // opposite the launch) rotates the victim's baked hit-pose hurtboxes 180°, and
        // the sprawling mixamorig hit pose reaches toward the attacker only when the
        // victim faces AWAY (the launch direction). At this scenario's reach margin the
        // follow-up whiffs even though the victim is still in hitstun (measured: stun 31
        // at t=10; the second jab's active window sees the victim at z≈1.5–1.9 m — out of
        // the jab's ~0.56 m reach, while the pre-change trace connected at z=1.64 via the
        // hit-pose limb pointing back at the attacker). The balance curve is unchanged; this pins the sim's real behavior so
        // the MoveDataReport tool's --truecombos claims stay in lockstep. In a live
        // match the attacker closes the gap, so this is a scripted-reach artifact, not
        // a balance regression.
        var result = ScriptedFollowUp(TestHelpers.FightGuyDef, 3, starterIasaTicks: 13, victimZ: 1.0f);

        Assert.Null(result);
    }

    [Fact]
    public void TrueCombo_StraightPunchToLowKick_IsNotTrueAtZero_UnderMeleeSoft()
    {
        // g2 Straight Punch → g1: under the shipped melee-soft tuning (issue #149), there are
        // no free true combos at 0%. Its hitstun cannot cover the actual 22-tick IASA plus
        // g1's trigger budget, so the follow-up lands after stun. Combos are meant to emerge
        // with damage %; this asserts the free-combo removal, not a bug.
        var result = ScriptedFollowUp(TestHelpers.FightGuyDef, 7 /* key 2 = slot 2 */, starterIasaTicks: 22, victimZ: 1.0f);

        Assert.False(result is { StunLeft: > 0 },
            "g2→g1 must NOT be a true combo at 0% under melee-soft (free combos are removed)");
    }
}
