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
/// The blink does NOT phase through arena geometry: it uses the same deterministic
/// swept capsule resolver as ordinary movement and stops at the first solid triangle.
/// With no injected arena (standalone ability harnesses), it falls back to the authored
/// full-distance displacement.
///
/// Sampling OFF the heightmap is valid: blinking over a gap or past the stage edge leaves
/// Nilus airborne and falling. That is the designed recovery risk.
///
/// Truncation costs the same as a full blink — the charge is still spent and the arrival
/// burst still fires at the final position rather than the intended one.
///
/// The charge pool is data-driven: ServerSimulation blocks activation when
/// ChargeStockSpent >= max_charges and spends the charge itself; Simulation regenerates on
/// charge_regen_ticks. This class contains no charge logic.
/// Note ChargeStockSpent is a single per-entity counter shared by every slot —
/// Riftwalk is the only Nilus slot that declares max_charges, so a second Nilus charge
/// ability would need that field split first.
///
/// The lifecycle runs off _ticks against a duration cached at OnStart.
///
/// Params: blink_distance, burst_tick, burst_radius, burst_damage, burst_stun_ticks.
/// </summary>
public sealed class NilusRiftwalk : ServerAbility
{
    private const float LegacyTraceStep = 0.25f;


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
            if (Arena.HasValue && ArenaCollision.HasTriangles(Arena.Value))
                Simulation.MoveThroughStage(ref s, def, Arena.Value,
                    dirX * distance, 0f, dirZ * distance);
            else if (Arena.HasValue)
            {
                // Legacy/unbaked arenas have no authoritative triangles yet. Preserve
                // their heightmap trace until the arena is cooked.
                float reached = TraceLegacyHeightmap(in s, def, dirX, dirZ, distance);
                s.PX += dirX * reached;
                s.PZ += dirZ * reached;
            }
            else
            {
                s.PX += dirX * distance;
                s.PZ += dirZ * distance;
            }
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

    private float TraceLegacyHeightmap(in CharacterState s, CharacterDefinition def,
        float dirX, float dirZ, float distance)
    {
        ArenaHeightmap heightmap = Arena!.Value.Heightmap;
        float capsuleHalf = def.CapsuleHeight * 0.5f;
        float headroom = s.PY + Simulation.PlatformSnapTolerance;
        float reached = 0f;
        int steps = (int)MathF.Ceiling(distance / LegacyTraceStep);
        for (int i = 1; i <= steps; i++)
        {
            float t = MathF.Min(i * LegacyTraceStep, distance);
            float surfaceY = heightmap.Sample(s.PX + dirX * t, s.PZ + dirZ * t);
            if (surfaceY > float.MinValue && surfaceY + capsuleHalf > headroom) break;
            reached = t;
        }
        return reached;
    }

}
