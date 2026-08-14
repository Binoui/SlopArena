using Xunit;
using SlopArena.Shared;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ADR-0021 authoring policy (§1 IASA, §3 landing lag + auto-cancel): every standard
/// normal authors an IASA early-out, and every standard aerial additionally authors its
/// landing-lag + both auto-cancel windows. These are the migration-safe contracts the kit
/// data must uphold — a designer deleting an IASA or landing-lag declaration fails here.
/// (Specials/recovery/charge keep 0 IASA and 0 landing lag by the same ADR; the RMB charge
/// rework to specials is a deferred balance-pass item, not asserted here.)
/// </summary>
public class AuthoringPolicyTests
{
    private static readonly (string Name, CharacterDefinition Def)[] Kits =
    {
        ("FightGuy", TestHelpers.FightGuyDef),
        ("Kistu", TestHelpers.KistuDef),
        ("Manki", TestHelpers.MankiDef),
        ("Nilus", TestHelpers.NilusDef),
    };

    [Fact]
    public void EveryGroundNormal_AuthorsIasa()
    {
        foreach (var (name, def) in Kits)
        {
            var lmb = def.LMB ?? def.Slot1;
            Assert.NotNull(lmb);
            Assert.True(lmb.Stages.Length > 0, $"{name} LMB has no stages");
            Assert.True(lmb.Stages[0].IasaTicks > 0,
                $"{name} LMB must author IasaTicks (ADR-0021 §1)");
            Assert.True(lmb.Stages[0].IasaTicks < lmb.Stages[0].DurationTicks,
                $"{name} LMB IasaTicks must precede the stage end");
        }
    }

    [Fact]
    public void EveryAirNormal_AuthorsIasaAndLandingLag()
    {
        foreach (var (name, def) in Kits)
        {
            var airLmb = def.AirLMB ?? def.AirSlot1;
            Assert.NotNull(airLmb);
            var stage = airLmb.Stages[0];
            Assert.True(stage.IasaTicks > 0,
                $"{name} AirLMB must author IasaTicks (ADR-0021 §1)");
            Assert.True(stage.LandingLagTicks > 0,
                $"{name} AirLMB must author LandingLagTicks (ADR-0021 §3)");
            Assert.True(stage.AutoCancelBeforeTicks > 0,
                $"{name} AirLMB must author AutoCancelBeforeTicks (ADR-0021 §3)");
            Assert.True(stage.AutoCancelAfterTicks > 0,
                $"{name} AirLMB must author AutoCancelAfterTicks (ADR-0021 §3)");
            Assert.True(stage.AutoCancelAfterTicks < stage.DurationTicks,
                $"{name} AirLMB AutoCancelAfterTicks must precede the stage end");
        }
    }

    [Fact]
    public void FightGuy_AirNormals_AuthorLandingLag()
    {
        // FightGuy is the only kit with a full normal tier (keys 1-4) — every air variant
        // must carry the same landing-lag + auto-cancel declarations as AirLMB.
        var def = TestHelpers.FightGuyDef;
        AssertAirNormal(def.AirSlot1!, "AirSlot1 (Double Punch)");
        AssertAirNormal(def.AirSlot2!, "AirSlot2 (Floating Kick)");
        AssertAirNormal(def.AirSlot3!, "AirSlot3 (High Kick)");
        AssertAirNormal(def.AirSlot4!, "AirSlot4 (Air Tornado)");
    }

    private static void AssertAirNormal(AbilitySpec spec, string label)
    {
        Assert.NotNull(spec);
        var stage = spec.Stages[0];
        Assert.True(stage.IasaTicks > 0, $"{label} must author IasaTicks");
        Assert.True(stage.LandingLagTicks > 0, $"{label} must author LandingLagTicks");
        Assert.True(stage.AutoCancelBeforeTicks > 0, $"{label} must author AutoCancelBeforeTicks");
        Assert.True(stage.AutoCancelAfterTicks > 0, $"{label} must author AutoCancelAfterTicks");
    }
}
