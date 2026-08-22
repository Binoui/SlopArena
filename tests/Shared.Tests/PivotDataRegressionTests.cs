using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Data-discipline regression for issue #114 (ADR-0015 pivot core).
/// Pins the three data acceptance criteria so a future tuning pass can't
/// silently revive the DKO-copy layer:
///   1. No attack ever triggers warp — every stage's WarpRange is 0.
///      The machinery stays dormant (not deleted): one data flip re-enables it.
///   2. Launcher profile at most one move per kit (kill-move territory).
///   3. StunTicks in the ~10-25 band (short hitstun, reset to neutral), except
///      sustained zone hits (DurationTicks >= 40), which tick-stun at 6 by design.
/// </summary>
public class PivotDataRegressionTests
{
    private static readonly CharacterClass[] Kits =
    {
        CharacterClass.FightGuy, CharacterClass.Kistu, CharacterClass.Manki, CharacterClass.Nilus,
    };

    private static IEnumerable<AttackStage> AllStages(CharacterDefinition def)
    {
        var specs = new[] { def.LMB, def.RMB, def.AirLMB, def.AirRMB, def.Slot1, def.E, def.R, def.F };
        foreach (var spec in specs)
        {
            if (spec == null) continue;
            if (spec.Stages != null)
                foreach (var s in spec.Stages) yield return s;
            if (spec.ChargedStages != null)
                foreach (var s in spec.ChargedStages) yield return s;
        }
    }

    [Fact]
    public void NoAttack_TriggersWarp_AllStagesHaveZeroWarpRange()
    {
        foreach (var c in Kits)
        {
            var def = CharacterRegistry.Get(c);
            foreach (var stage in AllStages(def))
            {
                Assert.True(stage.WarpRange == 0f,
                    $"{def.DisplayName}: a stage has WarpRange={stage.WarpRange} — warp must never initiate (ADR-0015, issue #114)");
            }
        }
    }

    [Fact]
    public void LauncherProfile_AtMostOneMovePerKit()
    {
        foreach (var c in Kits)
        {
            var def = CharacterRegistry.Get(c);
            int launchers = 0;
            foreach (var stage in AllStages(def))
            {
                if (stage.HitboxEvents == null) continue;
                foreach (var h in stage.HitboxEvents)
                    if (h.Knockback.Profile == KnockbackProfile.Launcher) launchers++;
            }
            Assert.True(launchers <= 1,
                $"{def.DisplayName}: {launchers} hitboxes use the Launcher profile — at most one per kit, kill moves only (ADR-0015)");
        }
    }

    [Fact]
    public void StunTicks_InTenToTwentyFiveBand_ExceptSustainedZoneHits()
    {
        foreach (var c in Kits)
        {
            var def = CharacterRegistry.Get(c);
            foreach (var stage in AllStages(def))
            {
                if (stage.HitboxEvents == null) continue;
                foreach (var h in stage.HitboxEvents)
                {
                    bool sustainedZone = h.DurationTicks >= 40; // sustained zone hit: tick-stun by design
                    Assert.True(sustainedZone || (h.StunTicks >= 10 && h.StunTicks <= 25),
                        $"{def.DisplayName}: StunTicks={h.StunTicks} outside the 10-25 band (ADR-0015 reset-to-neutral)");
                }
            }
        }
    }
}
