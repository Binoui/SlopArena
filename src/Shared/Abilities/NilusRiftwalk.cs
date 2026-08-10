using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Nilus' E — Riftwalk. A short blink in the facing direction that also works
/// airborne, making it his primary recovery AND his primary approach. It adds no
/// vertical velocity of its own, and is not purely horizontal in practice: it
/// covers distance at his current height, so it recovers horizontally — vertical
/// recovery is double-jump first (MaxJumps 2, FloatWindowTicks 40), then a blink
/// in at stage level. Note (issue #115 / ADR-0015): aerial abilities no longer
/// zero falling VY or reset AirTimeTicks — momentum-preserve removed that
/// engine-wide policy; Riftwalk rides the trajectory it was cast from.
///
/// The blink does NOT phase through arena geometry: it traces the path in
/// TraceStep increments and stops at the last valid position. A candidate is
/// invalid when the surface under it would put Nilus INSIDE the geometry, i.e.
/// surfaceY + capsuleHalf > PY + Simulation.PlatformSnapTolerance. That tolerance
/// is deliberately the same constant ground resolution uses (Simulation.cs:361-367),
/// so anything the sim would snap him onto next tick — steps, ramps — stays
/// traversable, and only rises it would NOT snap (The Split's 3 m upper platform,
/// Sanctum's Y=5/6/8 tiers) stop the blink. Without the trace such a blink leaves
/// him below the surface, ungrounded, and gravity carries him through the stage to
/// the blast zone: there is no force-snap-up outside hitstun (the one at
/// Simulation.cs:348-353 is inside the Hitstun branch).
///
/// Sampling OFF the heightmap (float.MinValue) is VALID and must stay that way:
/// blinking over a gap or past the stage edge leaves him airborne and falling.
/// That is the designed recovery risk and the reason spending both charges to
/// engage is lethal.
///
/// Truncation costs the same as a full blink — the charge is still spent and the
/// arrival burst still fires, at the FINAL position rather than the intended one.
///
/// When Arena is null (harnesses that drive abilities without a simulation) the
/// blink falls back to unconditional displacement.
///
/// The charge pool is data-driven: ServerSimulation blocks activation when
/// ChargeStockSpent >= max_charges and spends the charge itself; Simulation
/// regenerates on charge_regen_ticks. This class contains no charge logic.
/// Note ChargeStockSpent is a single per-entity counter shared by every slot —
/// Riftwalk is the only Nilus slot that declares max_charges, so a second
/// Nilus charge ability would need that field split first.
///
/// The lifecycle runs off _ticks against a duration CACHED at OnStart, never against
/// s.AnimLockTicks: TickTimers DECREMENTS AnimLockTicks every tick (Simulation.cs:405)
/// before TickAbilities runs, so an up-counter compared against it crosses at ceil(N/2)
/// and the instance died on tick 4 of the authored 8. EndAbility leaves AnimLockTicks
/// untouched (ServerAbility.cs:236) while Simulation.cs:300-303 runs ProcessNormalMovement
/// for any Idle entity, so E handed air control back on tick 5 and undid the blink's own
/// VX = VZ = 0. It also put burst_tick = 4 exactly ON the end tick, surviving only because
/// the burst block sits textually above the end check. With the cached duration the full
/// 8-tick window the data declares is real. KistuUltFlurry.cs:53 is the in-repo model;
/// KistuRisingSlash and KistuCounter still carry the trap.
///
/// Params: blink_distance, burst_tick, burst_radius, burst_damage, burst_stun_ticks.
/// </summary>
public sealed class NilusRiftwalk : ServerAbility
{
    /// <summary>
    /// Path-trace granularity in metres. 0.25 m over the 6 m blink is 24 samples —
    /// fine enough that the stop position never overshoots a ledge edge by more than
    /// a quarter of Nilus' 0.66 m capsule width, and coarse enough to stay negligible
    /// against a per-tick heightmap lookup that already runs for every entity.
    /// </summary>
    private const float TraceStep = 0.25f;

    private ushort _ticks;
    private ushort _duration;
    private bool _blinked;
    private bool _burst;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;
        _blinked = false;
        _burst = false;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;

        var spec = def.GetSlotAbility(Slot, airborne: false);
        _duration = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)8;
        s.AnimLockTicks = _duration;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        // Blink on the first tick: displace along facing, kill residual horizontal
        // velocity so he does not keep sliding out of the arrival position. VY is
        // deliberately untouched — the blink grants no lift of its own (the aerial
        // fall-stall noted above is ActivateAbility's, not ours).
        if (!_blinked)
        {
            _blinked = true;
            float distance = GetParam(def, "blink_distance", 6f);
            float dirX = MathF.Sin(s.FacingYaw);
            float dirZ = MathF.Cos(s.FacingYaw);
            float reached = TraceDistance(in s, def, dirX, dirZ, distance);
            s.PX += dirX * reached;
            s.PZ += dirZ * reached;
            s.VX = 0f;
            s.VZ = 0f;
        }

        // Arrival burst — a normal one-hit hitbox centred on the arrival point.
        ushort burstTick = (ushort)GetParam(def, "burst_tick", 4f);
        if (!_burst && _ticks >= burstTick)
        {
            _burst = true;

            float damage = GetParam(def, "burst_damage", 4f);
            float radius = GetParam(def, "burst_radius", 1.6f);
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            var (kbAngle, kbBase, kbGrowth) = new KnockbackData { Profile = KnockbackProfile.Light }.Resolve();

            Resolver.Spawn(new Hitbox
            {
                X = s.PX, Y = s.PY + 0.5f, Z = s.PZ,
                EndX = s.PX, EndY = s.PY + 0.5f, EndZ = s.PZ,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = damage,
                BaseKnockback = kbBase,
                KnockbackGrowth = kbGrowth,
                KnockbackAngle = kbAngle,
                StunTicks = (ushort)GetParam(def, "burst_stun_ticks", 12f),
                DurationTicks = 4,
                OwnerId = s.EntityId,
            });
        }

        if (_ticks >= _duration)
            EndAbility(ref s);
    }

    /// <summary>
    /// How far along (dirX, dirZ) the blink can travel from the caster's current
    /// position without ending inside arena geometry. Walks the path in TraceStep
    /// increments and returns the last valid offset — 0 when even the first step is
    /// blocked, the full distance when nothing blocks it. Off-heightmap samples are
    /// valid: blinking into open air is the designed recovery risk, not a collision.
    /// With no arena injected there is nothing to trace against, so the full distance
    /// is returned unchanged.
    /// </summary>
    private float TraceDistance(in CharacterState s, CharacterDefinition def,
        float dirX, float dirZ, float distance)
    {
        if (Arena == null) return distance;

        // Copy the heightmap out of the nullable struct once: Arena.Value would
        // re-copy the whole ArenaDefinition on every sample.
        ArenaHeightmap heightmap = Arena.Value.Heightmap;
        float capsuleHalf = def.CapsuleHeight * 0.5f;
        float headroom = s.PY + Simulation.PlatformSnapTolerance;

        float reached = 0f;
        int steps = (int)MathF.Ceiling(distance / TraceStep);
        for (int i = 1; i <= steps; i++)
        {
            float t = MathF.Min(i * TraceStep, distance);
            float surfaceY = heightmap.Sample(s.PX + dirX * t, s.PZ + dirZ * t);
            if (surfaceY > float.MinValue && surfaceY + capsuleHalf > headroom) break;
            reached = t;
        }
        return reached;
    }
}
