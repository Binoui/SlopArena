using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ADR-0016 slot constants (issue #116): named ActiveSlot values, the 11-slot cooldown
/// helper roundtrip, and the CharacterDefinition slot resolution (data-less slots 6-10
/// resolve to null until the kit-expansion tickets land).
/// </summary>
public class AbilitySlotsTests
{
    [Fact]
    public void SlotConstants_AreOrderedAndDistinct()
    {
        byte[] slots =
        {
            AbilitySlots.Lmb, AbilitySlots.Rmb, AbilitySlots.Slot1, AbilitySlots.E,
            AbilitySlots.R, AbilitySlots.F, AbilitySlots.Slot2, AbilitySlots.Slot3,
            AbilitySlots.Slot4, AbilitySlots.Slot5, AbilitySlots.A,
        };
        Assert.Equal(11, slots.Length);
        Assert.Equal(11, AbilitySlots.Count);
        Assert.Equal(11, slots.Distinct().Count());
        // The six historical slots keep their wire values (kit data + tests depend on them).
        Assert.Equal(1, AbilitySlots.Lmb);
        Assert.Equal(2, AbilitySlots.Rmb);
        Assert.Equal(3, AbilitySlots.Slot1); // key "1" — the former Q slot
        Assert.Equal(4, AbilitySlots.E);
        Assert.Equal(5, AbilitySlots.R);
        Assert.Equal(6, AbilitySlots.F);
    }

    [Fact]
    public void CooldownHelpers_Roundtrip_AllElevenSlots()
    {
        var s = new CharacterState();
        for (byte slot = 1; slot <= AbilitySlots.Count; slot++)
        {
            s.SetCooldown(slot, (ushort)(slot * 10));
            Assert.Equal((ushort)(slot * 10), s.GetCooldown(slot));
        }
        Assert.Equal((ushort)0, s.GetCooldown(0));
        Assert.Equal((ushort)0, s.GetCooldown(12));
    }

    [Fact]
    public void GetSlotAbility_DataLessSlots_ReturnNull()
    {
        var def = TestHelpers.MankiDef;
        // Data-less ADR-0016 slots (keys 2-5, A) must resolve to null — no throw.
        for (int slotIndex = 6; slotIndex < AbilitySlots.Count; slotIndex++)
            Assert.Null(def.GetSlotAbility(slotIndex));
        Assert.Null(def.GetSlotAbility(6, airborne: true));
        // The historical slots still resolve.
        Assert.NotNull(def.GetSlotAbility(0));
        Assert.NotNull(def.GetSlotAbility(2)); // Slot1 (former Q) — Manki Round Bomb
    }

    [Fact]
    public void Slot1_IsTheFormerQAbility()
    {
        // The Q ability survived the rekey onto key "1" — data identity unchanged.
        var manki = TestHelpers.MankiDef;
        Assert.Same(manki.Slot1, manki.GetSlotAbility(2));
        Assert.Equal(AbilityBehavior.AimedProjectile, manki.Slot1?.Behavior);
    }
}
