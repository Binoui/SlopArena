using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Nilus' R — Nether Grasp. A long reaching void claw (the spec's capsule runs from 0.6 m
/// out to 8.6 m in front of him); on connect the target is stunned and dragged back toward
/// Nilus, setting up the claw string or dropping them back into a Q rift they were escaping.
///
/// THE non-obvious part, and the thing not to "simplify" away: the drag is KNOCKBACK
/// aimed AT Nilus (Simulation.ApplyKnockback), never a velocity write. This ability
/// applies stun, so the target is in hitstun, and Simulation.ProcessHitstun rewrites
/// VX/VZ from KVX/KVZ on every single hitstun tick (Simulation.cs:470-471). A plain
/// `target.VZ = …` here is therefore erased before it ever integrates into position —
/// the target would simply stand still. KVX/KVY/KVZ is the only channel that survives.
/// It is exactly MankiGrapple's pull with the direction inverted: Grapple reels the
/// CASTER toward the target (target − attacker), Nether Grasp drags the TARGET toward
/// the caster (attacker − target).
///
/// This is also why the spec's HitboxEvent carries zero knockback: ResolveHits applies
/// the hitbox's own knockback first and calls OnHitEntity afterwards
/// (ServerSimulation.cs:722-731), so any outward knockback in the data would be
/// overwritten here anyway — and its stun tier/hit feedback would fight the yank.
///
/// pull_angle is a small POSITIVE launch angle, so ApplyKnockback clears IsGrounded and the
/// dragged target slides in unopposed by ground friction — which is why the drag distance is
/// the same fixed impulse whether the target started grounded or airborne. Note the target
/// is pulled UP-and-in, not down-and-in, and knockback resets AirTimeTicks, so it spends the
/// drag inside its float window: yanking someone toward a ledge takes them over it airborne.
/// The claw's vertical envelope is the real anti-air limit, not AttackRange — measured, it
/// connects on a target up to 2.75 m above the floor and misses from 3.00 m.
///
/// Aiming is target-lock, not a free aim cone: the stage declares UseTargetLock /
/// RotateTowardTarget over AttackRange 9 m, so ProcessTargetLock turns Nilus onto the
/// locked opponent before the claw spawns. There is no AimYaw/AimDistance to cache
/// (AimMode.None) — unlike Q, which lobs at a chosen ground point.
///
/// The lifecycle runs off _ticks against a duration CACHED at OnStart, never against
/// s.AnimLockTicks: TickTimers DECREMENTS AnimLockTicks every tick (Simulation.cs:405)
/// before TickAbilities runs, so an up-counter compared against it crosses at ceil(N/2)
/// and the instance dies at half the stage's DurationTicks — tick 17 of 34 here. That is
/// not harmless. EndAbility sets State = Idle WITHOUT clearing AnimLockTicks
/// (ServerAbility.cs:236), and Simulation.cs:300-303 runs ProcessNormalMovement for any
/// Idle entity with no AnimLockTicks guard (only dash and new-attack activation are gated,
/// Simulation.cs:252) — so Nilus walked freely from tick 18 of a 34-tick commitment, and a
/// HitboxEvent past tick 17 would have been dropped silently, since the trigger match at
/// :77 is == rather than >=. KistuUltFlurry.cs:53 is the in-repo model for the cached form;
/// KistuRisingSlash and KistuCounter still carry the trap.
///
/// Params: pull_force, pull_angle, pull_stun_ticks.
/// </summary>
public sealed class NilusNetherGrasp : ServerAbility
{
    private ushort _ticks;
    private ushort _duration;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.VX = 0f;
        s.VZ = 0f;

        var spec = def.GetSlotAbility(Slot, airborne: false);
        _duration = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)34;
        s.AnimLockTicks = _duration;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        // Spawn the reaching claw hitbox from the spec.
        var spec = def.GetSlotAbility(Slot, airborne: false);
        if (spec?.Stages is { Length: > 0 })
        {
            foreach (var evt in spec.Stages[0].HitboxEvents)
            {
                if (evt.TriggerTick == _ticks)
                    SpawnHitbox(ref s, evt);
            }
        }

        if (_ticks >= _duration)
            EndAbility(ref s);
    }

    /// <summary>
    /// Drag the target inward. Knockback aimed at Nilus, so hitstun preserves it — see the
    /// class remarks for why a VX/VZ write here cannot work.
    /// </summary>
    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, CharacterDefinition targetDef,
        ref float damage, ref float knockbackForce)
    {
        float dx = attacker.PX - target.PX;
        float dz = attacker.PZ - target.PZ;
        float dist = MathF.Sqrt((dx * dx) + (dz * dz));
        if (dist < 0.01f) return;

        float force = GetParam(attackerDef, "pull_force", 9.5f);
        float angle = GetParam(attackerDef, "pull_angle", 8f);
        float stun = GetParam(attackerDef, "pull_stun_ticks", 20f);

        // The yank is a fixed displacement, not a percent-scaling launch: a grab that
        // reaches further the more damage the victim has taken would drag them clean past
        // Nilus at high percent. applyScale:false keeps it out of the hit-KB balance pass.
        Simulation.ApplyKnockback(ref target, dx / dist, dz / dist,
            (sbyte)angle, force, 0f, damage, (ushort)stun, targetDef.Weight, applyScale: false);
    }
}
