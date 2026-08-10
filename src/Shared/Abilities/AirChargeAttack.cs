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
        // Momentum-preserve (issue #115): the charge keeps the player's current trajectory —
        // no ascent stop, no hover. Air control resumes when the charge resolves.
    }

    protected override void OnAttackStart(ref CharacterState s, CharacterDefinition def, AttackStage stage)
    {
        // Momentum-preserve (issue #115): the air RMB fires from the player's current
        // trajectory — falling VY and the FloatWindow position carry into the attack.
        // No hover reset here or in the engine.

        if (stage.LungeForce != 0f)
            SetVelocityInFacing(ref s, stage.LungeForce);
    }

    protected override void OnAttackTick(ref CharacterState s, CharacterDefinition def, AttackStage stage)
    {
        // Per-tick stage velocity (AttackStage.MoveX/MoveY/MoveZ), re-applied EVERY tick:
        // gravity + friction run before TickAbilities each tick, so a single write would be
        // eaten immediately. Components are applied individually so a stage that declares only
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
