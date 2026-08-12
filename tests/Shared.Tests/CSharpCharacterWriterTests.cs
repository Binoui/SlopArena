using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for CSharpCharacterWriter — the source write-back that persists Ability Lab
/// hitbox edits directly into src/Shared/Characters/*Data.cs. The golden tests run
/// against the REAL MankiData.cs and assert exactly one block changes.
/// </summary>
public class CSharpCharacterWriterTests
{
    // ── Helpers ──

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate repo file: {relative}");
    }

    private static string RealMankiSource() => File.ReadAllText(FindRepoFile("src/Shared/Characters/MankiData.cs"));

    /// <summary>Line-diff count between two sources — the real-file tests assert it's exactly 1.</summary>
    private static int CountChangedLines(string a, string b)
    {
        var al = a.Split('\n');
        var bl = b.Split('\n');
        int min = Math.Min(al.Length, bl.Length);
        int diffs = Enumerable.Range(0, min).Count(i => al[i] != bl[i]);
        return diffs + Math.Abs(al.Length - bl.Length);
    }

    private static HitboxEvent SingleSphereEvent() => new()
    {
        TriggerTick = 20,
        DurationTicks = 10,
        Radius = 0.8f,
        OffY = 0.5f,
        OffZ = 1.2f,
        Damage = 6f,
        Knockback = new KnockbackData { Profile = KnockbackProfile.Light },
        StunTicks = 18,
        Interruptible = true,
    };

    // ── Property mapping ──

    [Theory]
    [InlineData(0, false, "LMB")] [InlineData(0, true, "AirLMB")]
    [InlineData(1, false, "RMB")] [InlineData(1, true, "AirRMB")]
    [InlineData(2, false, "Slot1")] [InlineData(2, true, "AirSlot1")]
    [InlineData(3, false, "E")] [InlineData(3, true, "AirE")]
    [InlineData(4, false, "R")] [InlineData(4, true, "AirR")]
    [InlineData(5, false, "F")] [InlineData(5, true, "AirF")]
    [InlineData(6, false, "Slot2")] [InlineData(6, true, "AirSlot2")]
    [InlineData(7, false, "Slot3")] [InlineData(7, true, "AirSlot3")]
    [InlineData(8, false, "Slot4")] [InlineData(8, true, "AirSlot4")]
    [InlineData(9, false, "Slot5")] [InlineData(9, true, "AirSlot5")]
    [InlineData(10, false, "A")] [InlineData(10, true, "AirA")]
    public void PropertyName_MapsEverySlotField(int slot, bool airborne, string expected)
        => Assert.Equal(expected, CSharpCharacterWriter.PropertyName(slot, airborne));

    [Fact]
    public void PropertyName_UnknownSlot_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CSharpCharacterWriter.PropertyName(11, false));

    // ── Formatting ──

    [Fact]
    public void Format_SingleSphereEvent_MatchesRepoStyle()
    {
        string text = CSharpCharacterWriter.FormatHitboxEvents(new[] { SingleSphereEvent() }, "");
        Assert.Equal(
            "new HitboxEvent[] { new() { TriggerTick = 20, DurationTicks = 10, Radius = 0.8f, OffX = 0f, OffY = 0.5f, OffZ = 1.2f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 18, Interruptible = true } }",
            text);
    }

    [Fact]
    public void Format_CapsuleWithCustomKnockback_EmitsShapeAndEndOffsets()
    {
        var evt = new HitboxEvent
        {
            TriggerTick = 6,
            DurationTicks = 5,
            Shape = HitboxShape.Capsule,
            Radius = 0.4f,
            OffY = 0.7f,
            OffZ = 0.7f,
            EndOffY = 0.7f,
            EndOffZ = 1.8f,
            Damage = 3f,
            Knockback = new KnockbackData { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 8f, KnockbackGrowth = 5f },
            StunTicks = 16,
            Interruptible = true,
        };
        string text = CSharpCharacterWriter.FormatHitboxEvents(new[] { evt }, "");
        Assert.Equal(
            "new HitboxEvent[] { new() { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.4f, OffX = 0f, OffY = 0.7f, OffZ = 0.7f, EndOffX = 0f, EndOffY = 0.7f, EndOffZ = 1.8f, Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 8f, KnockbackGrowth = 5f }, StunTicks = 16, Interruptible = true } }",
            text);
    }

    [Fact]
    public void Format_ZeroedCustomKnockback_KeepsTheFields()
    {
        // Nilus' deliberately INERT hitbox: Custom with all-zero values must not
        // collapse into a live profile on round-trip.
        var evt = new HitboxEvent
        {
            TriggerTick = 7,
            DurationTicks = 10,
            Shape = HitboxShape.Capsule,
            Radius = 0.55f,
            OffY = 0.8f,
            OffZ = 0.6f,
            EndOffY = 0.8f,
            EndOffZ = 8f,
            Damage = 8f,
            Knockback = new KnockbackData { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 0f, KnockbackGrowth = 0f },
            StunTicks = 20,
            Interruptible = true,
        };
        string text = CSharpCharacterWriter.FormatHitboxEvents(new[] { evt }, "");
        Assert.Contains("Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 0f, KnockbackGrowth = 0f }", text);
    }

    [Fact]
    public void Format_BoneAttachedEvent_EmitsBoneNameAndOffsets()
    {
        var evt = new HitboxEvent
        {
            TriggerTick = 1,
            DurationTicks = 4,
            Radius = 0.5f,
            BoneName = "mixamorig:RightHand",
            BoneOffZ = 0.3f,
            Damage = 9f,
            StunTicks = 14,
            Interruptible = false,
        };
        string text = CSharpCharacterWriter.FormatHitboxEvents(new[] { evt }, "");
        Assert.Contains("BoneName = \"mixamorig:RightHand\", BoneOffZ = 0.3f", text);
        Assert.Contains("Interruptible = false", text);
    }

    [Fact]
    public void Format_MultipleEvents_UsesMultilineArrayWithIndent()
    {
        var evts = new[] { SingleSphereEvent(), SingleSphereEvent() };
        string text = CSharpCharacterWriter.FormatHitboxEvents(evts, "    ");
        Assert.Equal(
            "new HitboxEvent[]\n    {\n        new() { TriggerTick = 20, DurationTicks = 10, Radius = 0.8f, OffX = 0f, OffY = 0.5f, OffZ = 1.2f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 18, Interruptible = true },\n        new() { TriggerTick = 20, DurationTicks = 10, Radius = 0.8f, OffX = 0f, OffY = 0.5f, OffZ = 1.2f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 18, Interruptible = true },\n    }",
            text);
    }

    [Fact]
    public void Format_EmptyEvents_EmitsArrayEmpty()
        => Assert.Equal("Array.Empty<HitboxEvent>()", CSharpCharacterWriter.FormatHitboxEvents(Array.Empty<HitboxEvent>(), ""));

    // ── Real-file golden tests ──

    [Fact]
    public void Replace_RealManki_LmbStage0_ChangesExactlyOneLine()
    {
        string src = RealMankiSource();
        var newEvents = new[]
        {
            new HitboxEvent { TriggerTick = 20, DurationTicks = 10, Radius = 1.2f, OffZ = 2f, Damage = 8f, StunTicks = 22, Interruptible = true },
        };
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(src, "LMB", 0, newEvents, out string result));
        Assert.Contains("TriggerTick = 20, DurationTicks = 10", result);
        Assert.DoesNotContain("TriggerTick = 12, DurationTicks = 8", result);
        Assert.Equal(1, CountChangedLines(src, result));
    }

    [Fact]
    public void Replace_RealManki_AirLmb_ChangesOnlyThatSpec()
    {
        string src = RealMankiSource();
        var newEvents = new[] { SingleSphereEvent() };
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(src, "AirLMB", 0, newEvents, out string result));
        Assert.Equal(1, CountChangedLines(src, result));
        // Ground LMB must be untouched.
        Assert.Contains("TriggerTick = 12, DurationTicks = 8", result);
        Assert.DoesNotContain("TriggerTick = 6, DurationTicks = 6", result);
    }

    [Fact]
    public void Replace_RealManki_RmbStage0_ArrayEmptyBecomesEvents()
    {
        string src = RealMankiSource();
        var newEvents = new[] { SingleSphereEvent() };
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(src, "RMB", 0, newEvents, out string result));
        // Stage 0 (charge phase) had no events; the replacement is a single line
        // swapping a single line, so the file diff is exactly one line.
        Assert.Equal(1, CountChangedLines(src, result));
        Assert.Contains("TriggerTick = 20, DurationTicks = 10", result);
    }

    [Fact]
    public void Replace_RealManki_SecondStage_DoesNotMatchChargedStages()
    {
        // RMB stage 1 is a multi-line block (12 lines → 1), so the index-wise line
        // metric is meaningless; assert structure instead: prefix and suffix identical,
        // new events in, old events out, ChargedStages untouched.
        string src = RealMankiSource();
        var newEvents = new[] { SingleSphereEvent() };
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(src, "RMB", 1, newEvents, out string result));

        string stage1Marker = "new() { DurationTicks = 58,";
        Assert.StartsWith(src.Substring(0, src.IndexOf(stage1Marker, StringComparison.Ordinal)), result);
        Assert.EndsWith(src.Substring(src.IndexOf("ChargedStages = new AttackStage[]", StringComparison.Ordinal)), result);

        Assert.Contains("TriggerTick = 20, DurationTicks = 10", result);
        Assert.DoesNotContain("TriggerTick = 8, DurationTicks = 38", result); // old stage-1 event
        // The charged flame keeps its own events.
        Assert.Contains("TriggerTick = 10, DurationTicks = 30", result);
    }

    // ── Structural edge cases (synthetic sources) ──

    private const string TwoStageSource = @"
        LMB = new AbilitySpec
        {
            Stages = new AttackStage[]
            {
                new() { DurationTicks = 40, HitboxEvents = new[] { new HitboxEvent { TriggerTick = 1, DurationTicks = 2, Radius = 0.5f, Damage = 1f, StunTicks = 10, Interruptible = true } } },
                new() { DurationTicks = 40, HitboxEvents = new[] { new HitboxEvent { TriggerTick = 1, DurationTicks = 2, Radius = 0.5f, Damage = 1f, StunTicks = 10, Interruptible = true } } },
            },
            AnimationNames = new[] { ""a"", ""b"" },
        },
";

    [Fact]
    public void Replace_IdenticalBlocks_OnlyTargetStageChanges()
    {
        var newEvents = new[] { new HitboxEvent { TriggerTick = 9, DurationTicks = 3, Radius = 2f, Damage = 5f, StunTicks = 12, Interruptible = true } };
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(TwoStageSource, "LMB", 1, newEvents, out string result));
        Assert.Contains("TriggerTick = 9", result);
        Assert.Contains("TriggerTick = 1", result); // stage 0 keeps its block
        Assert.Equal(1, CountChangedLines(TwoStageSource, result));
    }

    [Fact]
    public void Replace_StageWithoutHitboxEvents_InsertsProperty()
    {
        const string src = @"
        RMB = new AbilitySpec
        {
            Stages = new AttackStage[]
            {
                new() { DurationTicks = 60 },
            },
        },
";
        var newEvents = new[] { SingleSphereEvent() };
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(src, "RMB", 0, newEvents, out string result));
        Assert.Contains("DurationTicks = 60, HitboxEvents = new HitboxEvent[] { new() {", result);
    }

    [Fact]
    public void Replace_EmptyElement_InsertsWithoutLeadingComma()
    {
        const string src = @"
        F = new AbilitySpec
        {
            Stages = new AttackStage[]
            {
                new() { },
            },
        },
";
        var newEvents = new[] { SingleSphereEvent() };
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(src, "F", 0, newEvents, out string result));
        Assert.Contains("new() { HitboxEvents = new HitboxEvent[] { new() {", result);
        Assert.DoesNotContain("{ ,", result);
    }

    [Fact]
    public void Replace_WithEmptyEvents_WritesArrayEmpty()
    {
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(TwoStageSource, "LMB", 0, Array.Empty<HitboxEvent>(), out string result));
        Assert.Contains("HitboxEvents = Array.Empty<HitboxEvent>()", result);
    }

    [Fact]
    public void Replace_CommentWithCommasBetweenStages_DoesNotSplitElement()
    {
        const string src = @"
        LMB = new AbilitySpec
        {
            Stages = new AttackStage[]
            {
                new() { DurationTicks = 30, HitboxEvents = new[] { new HitboxEvent { TriggerTick = 2, DurationTicks = 4, Radius = 0.5f, Damage = 1f, StunTicks = 10, Interruptible = true } } },
                // Stage 1: uncharged attack (quick release, less damage/stun)
                new() { DurationTicks = 35, HitboxEvents = new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 4, Radius = 0.5f, Damage = 2f, StunTicks = 10, Interruptible = true } } },
            },
        },
";
        var newEvents = new[] { SingleSphereEvent() };
        Assert.True(CSharpCharacterWriter.TryReplaceHitboxEvents(src, "LMB", 1, newEvents, out string result));
        Assert.Equal(1, CountChangedLines(src, result));
    }

    [Fact]
    public void Key_RoundTrip_EverySlotAndAirborne()
    {
        // Regression: an assignment-inside-&& made TryParseKey reject every
        // airborne=false key ("4:0:0" reported malformed on save).
        for (int slot = 0; slot <= 10; slot++)
        {
            foreach (bool airborne in new[] { false, true })
            {
                foreach (int stage in new[] { 0, 3 })
                {
                    string key = CSharpCharacterWriter.Key(slot, airborne, stage);
                    Assert.True(CSharpCharacterWriter.TryParseKey(key, out int s, out bool a, out int st), $"failed: {key}");
                    Assert.Equal(slot, s);
                    Assert.Equal(airborne, a);
                    Assert.Equal(stage, st);
                }
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("4:0")]
    [InlineData("x:0:0")]
    [InlineData("4:y:0")]
    [InlineData("4:2:0")]
    [InlineData("4:0:z")]
    [InlineData("4:0:0:1")]
    public void TryParseKey_MalformedInput_ReturnsFalse(string key)
        => Assert.False(CSharpCharacterWriter.TryParseKey(key, out _, out _, out _));

    [Fact]
    public void Replace_UnknownProperty_ReturnsFalse()
        => Assert.False(CSharpCharacterWriter.TryReplaceHitboxEvents(TwoStageSource, "Warp", 0, new[] { SingleSphereEvent() }, out _));

    [Fact]
    public void Replace_StageOutOfRange_ReturnsFalse()
        => Assert.False(CSharpCharacterWriter.TryReplaceHitboxEvents(TwoStageSource, "LMB", 5, new[] { SingleSphereEvent() }, out _));

    [Fact]
    public void Replace_NoStagesArray_ReturnsFalse()
    {
        const string src = "LMB = new AbilitySpec { Name = \"X\" }, ";
        Assert.False(CSharpCharacterWriter.TryReplaceHitboxEvents(src, "LMB", 0, new[] { SingleSphereEvent() }, out _));
    }
}
