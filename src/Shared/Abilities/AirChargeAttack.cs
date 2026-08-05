using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// AirRMB (slot 1, airborne): hold-to-charge aerial heavy shared by all characters.
/// Reuses <see cref="ChargeAttackAbility"/>'s two-phase lifecycle with the AIRBORNE slot
/// spec: tap (release before <c>ChargeHoldTicks</c>) fires <c>Stages[1]</c>, a full hold
/// auto-releases <c>ChargedStages[0]</c> (or after 5s failsafe).
///
/// Aerial extras over the ground charge classes:
///   - <see cref="OnChargeStart"/> zeroes vertical velocity — pressing mid-ascent stops the
///     climb and the character charges in place (the sim only cancels DOWNWARD VY).
///   - <see cref="OnAttackStart"/> applies the resolved stage's LungeForce as a forward burst.
///   - <see cref="OnAttackTick"/> drives the stage's per-tick MoveX/MoveY/MoveZ velocity
///     every attack tick — the Collapse-style downward slams (see the note in AttackData.cs
///     on why a single write at OnStart is eaten). A downward MoveY write is refused while
///     grounded, mirroring the old AirRmbAttack guard so a slam can't drill through the floor.
/// </summary>
public sealed class AirChargeAttack : ChargeAttackAbility
{
    protected override bool IsAirborne => true;

    protected override void OnChargeStart(ref CharacterState s, CharacterDefinition def)
    {
        // Stop a jump's ascent the moment the charge begins. ActivateAbility only cancels
        // DOWNWARD velocity, and float gravity (AirFloatGravity=0) never bleeds upward
        // momentum off, so pressing air RMB mid-rise would otherwise carry the climb through
        // the entire hold — "fly up in the air" while charging. The charge is a stop-and-
        // wind-up stance (deliberately unlike air LMB, which keeps momentum for its combo).
        if (!s.IsGrounded)
            s.VY = 0f;
    }

    protected override void OnAttackStart(ref CharacterState s, CharacterDefinition def, AttackStage stage)
    {
        // Mirror the sim's aerial-attack reset (ServerSimulation.ActivateAbility) at the
        // moment the attack actually begins. The charge hold lets gravity re-accumulate
        // downward velocity and burn the float window, so without this the air RMB fires
        // from a fall while air LMB fires from a hover (StageChainAbility re-applies the
        // same reset — VY zero + AirTimeTicks=0 — on activation and every chain stage).
        if (!s.IsGrounded && s.VY < 0f)
            s.VY = 0f;
        s.AirTimeTicks = 0;

        if (stage.LungeForce != 0f)
            SetVelocityInFacing(ref s, stage.LungeForce);
    }

    protected override void OnAttackTick(ref CharacterState s, CharacterDefinition def, AttackStage stage)
    {
        // Per-tick stage velocity (AttackStage.MoveX/MoveY/MoveZ), re-applied EVERY tick:
        // ServerSimulation.ActivateAbility zeroes downward VY on activation, and gravity +
        // friction run before TickAbilities each tick, so a single write would be eaten
        // immediately. Components are applied individually so a stage that declares only
        // MoveY (Nilus' Collapse) keeps whatever horizontal velocity LungeForce gave it.
        //
        // MoveY is refused while GROUNDED unless it points up. Grounded is reachable here:
        // the ability is activated airborne but nothing ends it on landing, so the remaining
        // ticks keep writing. Descent per tick is |MoveY| / 60 against PlatformSnapTolerance
        // = 0.5 (Simulation.cs:85), so at |MoveY| > 30 the post-integration PY lands BELOW
        // the snap window (Simulation.cs:363), control falls through to IsGrounded = false,
        // and the character leaves the floor downward with this write re-dirtying VY every
        // tick — a fall-through, not a cosmetic reading. Upward writes stay allowed: those
        // are jump arcs, and ground resolution handles them.
        if (stage.MoveX != 0f) s.VX = stage.MoveX;
        if (stage.MoveY != 0f && (!s.IsGrounded || stage.MoveY > 0f)) s.VY = stage.MoveY;
        if (stage.MoveZ != 0f) s.VZ = stage.MoveZ;
    }
}
