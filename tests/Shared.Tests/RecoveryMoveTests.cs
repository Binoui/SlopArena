using System.Collections.Generic;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Shared RecoveryMove capability tests (ADR-0015, issue #115 / #108).
/// The Smash up-B analog: upward velocity burst + FloatWindow reset + long per-entity
/// cooldown. The FloatWindow reset is gated on <see cref="AbilitySpec.IsRecoveryMove"/> —
/// ONLY recovery-designated moves reset AirTimeTicks; every other air move rides its
/// trajectory (see MomentumPreserveTests). No kit data uses it yet — synthetic spec only.
/// </summary>
public class RecoveryMoveTests
{
    private const ushort RecoveryCooldown = 360; // 6s — once per life-or-death

    /// <summary>Synthetic airborne recovery spec (Manki clone, slot 0 airborne).</summary>
    private static CharacterDefinition RecoveryDef(bool flagged)
    {
        var def = TestHelpers.CloneDef(TestHelpers.CombatDef);
        def.AirLMB = new AbilitySpec
        {
            Name = "Synthetic Recovery",
            Behavior = AbilityBehavior.MeleeCombo,
            CooldownTicks = RecoveryCooldown,
            IsRecoveryMove = flagged,
            Stages = new[] { new AttackStage { DurationTicks = 20, HitboxEvents = System.Array.Empty<HitboxEvent>() } },
            Params = new() { ["burst_vy"] = 12f },
        };
        return def;
    }

    private static CharacterState FallingAirborneState()
    {
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.VY = -5f;           // falling
        state.AirTimeTicks = 50;  // well past the float window
        state.IsGrounded = false;
        return state;
    }

    private static void ActivateRecovery(ServerSimulation sim, CharacterDefinition def)
    {
        var ability = new RecoveryMove();
        AbilityFactory.InitFromSpec(ability, def.AirLMB!, 0);
        sim.ActivateAbility(1, ability, 0, def);
    }

    [Fact]
    public void RecoveryMove_BurstsUpward_ResetsFloatWindow()
    {
        var sim = TestHelpers.MakeSim();
        var def = RecoveryDef(flagged: true);
        TestHelpers.RegisterPlayer(sim, def, FallingAirborneState());

        ActivateRecovery(sim, def);

        var after = sim.GetState(1);
        Assert.True(after.VY > 5f, $"burst must add upward velocity: VY={after.VY}");
        Assert.Equal((ushort)0, after.AirTimeTicks); // FloatWindow reset — recovery-only
        Assert.Equal(ActionState.Attacking, after.State);
    }

    [Fact]
    public void RecoveryMove_EndsAnd_AppliesLongCooldown()
    {
        var sim = TestHelpers.MakeSim();
        var def = RecoveryDef(flagged: true);
        TestHelpers.RegisterPlayer(sim, def, FallingAirborneState());

        ActivateRecovery(sim, def);

        // 20-tick move + margin; EndAbility applies the spec cooldown to the per-entity slot.
        for (int i = 0; i < 25; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var done = sim.GetState(1);
        Assert.Equal(ActionState.Idle, done.State);
        Assert.True(done.Cooldown0 > 300,
            $"long per-entity cooldown must be applied: Cooldown0={done.Cooldown0}");
    }

    [Fact]
    public void RecoveryMove_Cooldown_BlocksRepeatPress()
    {
        var sim = TestHelpers.MakeSim();
        var def = RecoveryDef(flagged: true);
        TestHelpers.RegisterPlayer(sim, def, FallingAirborneState());

        ActivateRecovery(sim, def);
        for (int i = 0; i < 25; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
        Assert.True(sim.GetState(1).Cooldown0 > 300, "precondition: cooldown live");

        // Repeat press while the cooldown is live: PreTickAbilities blocks the activation.
        sim.Tick(new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 1) } });
        var s = sim.GetState(1);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal(ActionState.Idle, s.State);
    }

    [Fact]
    public void NonRecoveryMove_DoesNotResetFloatWindow()
    {
        var sim = TestHelpers.MakeSim();
        var def = RecoveryDef(flagged: false); // same class, flag off
        TestHelpers.RegisterPlayer(sim, def, FallingAirborneState());

        ActivateRecovery(sim, def);

        var after = sim.GetState(1);
        Assert.True(after.AirTimeTicks >= 50,
            $"unflagged move must NOT reset AirTime: {after.AirTimeTicks}");
        Assert.True(after.VY > 5f,
            "burst is class behavior and still applies — the flag gates only the FloatWindow reset");
    }
}
