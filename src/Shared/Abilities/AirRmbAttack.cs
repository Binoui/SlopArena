using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// AirRMB (slot 1, airborne): single-hit aerial spike attack shared by all characters.
/// Reads stage data from the character's AirRMB spec (CharacterDefinition.AirRMB).
///
/// Single stage: spawns hitbox at TriggerTick, applies lunge if configured, drives the
/// stage's per-tick MoveX/MoveY/MoveZ velocity, ends naturally after DurationTicks.
/// </summary>
public sealed class AirRmbAttack : ServerAbility
{
    private ushort _ticks;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;

        var spec = def.GetSlotAbility(Slot, airborne: true);
        s.State = ActionState.Attacking;
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;

        // Apply lunge from spec's first stage
        if (spec?.Stages is { Length: > 0 } && spec.Stages[0].LungeForce > 0f)
            SetVelocityInFacing(ref s, spec.Stages[0].LungeForce);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        var spec = def.GetSlotAbility(Slot, airborne: true);
        if (spec?.Stages is not { Length: > 0 }) { EndAbility(ref s); return; }

        var stage = spec.Stages[0];

        // ── Per-tick stage velocity (AttackStage.MoveX/MoveY/MoveZ) ──
        // Re-applied EVERY tick, not once at OnStart: ServerSimulation.ActivateAbility
        // zeroes downward VY on activation, and SimulateTick's gravity + friction run
        // before TickAbilities each tick, so a single write would be eaten immediately.
        // Components are applied individually so a stage that declares only MoveY (Nilus'
        // Collapse) keeps whatever horizontal velocity LungeForce gave it.
        //
        // MoveY is refused while GROUNDED unless it points up. Grounded is reachable here: the
        // ability is activated airborne but nothing ends it on landing, so the remaining ticks
        // keep writing. Descent per tick is |MoveY| / 60 against PlatformSnapTolerance = 0.5
        // (Simulation.cs:85), so at |MoveY| > 30 the post-integration PY lands BELOW the snap
        // window (Simulation.cs:363), control falls through to `IsGrounded = false`, and the
        // character leaves the floor downward with this write re-dirtying VY every tick — a
        // fall-through, not a cosmetic reading. Collapse's -14 gives 0.233 m/tick so nothing
        // ships broken today, and no test would catch a future stage that crossed the line
        // (AirRmb_GroundedDownwardMoveY_CannotDrillThroughTheFloor now does).
        // Upward writes stay allowed: those are jump arcs, and ground resolution handles them.
        if (stage.MoveX != 0f) s.VX = stage.MoveX;
        if (stage.MoveY != 0f && (!s.IsGrounded || stage.MoveY > 0f)) s.VY = stage.MoveY;
        if (stage.MoveZ != 0f) s.VZ = stage.MoveZ;

        // Spawn hitboxes at trigger ticks
        foreach (var evt in stage.HitboxEvents)
        {
            if (evt.TriggerTick == _ticks)
                SpawnHitbox(ref s, evt);
        }

        if (_ticks >= stage.DurationTicks)
            EndAbility(ref s);
    }
}
