namespace SlopArena.Shared.Abilities;

/// <summary>
/// Nilus' F — Event Horizon (ultimate, and the kit's finisher). Three phases in one
/// instance:
///   1. Windup (windup_ticks): pure telegraph. Caster locked, nothing spawned.
///   2. Drag (drag_duration_ticks): every drag_interval_ticks, pull everything within
///      drag_radius toward the centre and land a small damage pulse on it.
///   3. Detonation: one outward-and-up kill-tier hitbox on the ability's last tick.
///
/// Nilus is locked in place (VX = VZ = 0 every tick) for the whole ability. That lock is
/// what makes a 6 m drag into a kill detonation fair — the telegraph is the commitment —
/// and it is also what makes the drag *implementable*: ServerSimulation discards an
/// ability instance the moment its caster leaves ActionState.Attacking
/// (ServerSimulation.cs:143), so staying locked in Attacking is what keeps this instance
/// alive long enough to run its own per-tick loop over SimulationStates. This is the
/// standard locked-ability lifecycle.
///
/// ── Why the lifecycle is NOT `_ticks >= s.AnimLockTicks` ──
/// The house idiom (KistuRisingSlash, NilusRiftwalk) compares an INCREMENTING local
/// counter against AnimLockTicks, which TickTimers DECREMENTS every tick
/// (Simulation.cs:405). The two cross at half the stage duration, so an ability written
/// that way ends at DurationTicks / 2. Those abilities get away with it because their
/// hitboxes fire early. This one would not: the stage is 132 ticks, so it would be
/// discarded on tick 66 and would silently drop its detonation entirely — no damage
/// spike, no knockback, no cooldown. The lifecycle therefore runs off _ticks against the
/// durations cached at OnStart, and nothing else.
///
/// For the same reason the input lock is set one tick LONGER than the ability: TickTimers
/// decrements AnimLockTicks before TickAbilities runs, so a lock of exactly N is already 0
/// on ability tick N — and a player holding dash on the detonation tick would StartDash,
/// leave Attacking, and lose the blast on the last frame of a 540-tick cooldown.
///
/// ── Why the drag may be a velocity write but the detonation may not ──
/// The drag writes VX/VZ on other entities directly, which is only legal because its pulse
/// carries no knockback and no stun: the pulse CLEARS whatever hitstun the target was in
/// (a zero-magnitude ApplyKnockback takes the else branch at Simulation.cs:937 with
/// StunTicks = 0), so ProcessHitstun does not run on a pulse tick and the velocity written
/// here integrates normally. Hitstun is therefore not a shield against the pull. What DOES
/// erase the drag is a THIRD-PARTY hit landing on a NON-pulse tick: ProcessHitstun then
/// rewrites VX/VZ from KVX/KVZ (Simulation.cs:470-471) until the next pulse re-applies it.
/// Accepted — the drag is glue, not a guarantee.
///
/// The detonation is the opposite case — it stuns by design, so its victim IS in hitstun
/// and a velocity write on it would be erased before it integrated into position. It must
/// travel the knockback channel (Simulation.ApplyKnockback, reached here via the hitbox and
/// ResolveHits), which also gives it the correct outward direction: ResolveHits recomputes
/// the direction from attacker to target (ServerSimulation.cs:706), so a blast centred on
/// Nilus throws every victim away from him — the exact opposite of the drag.
///
/// The detonation is also spawned INSTEAD of that tick's drag pulse, never alongside it.
/// SpellResolver walks hitboxes newest-to-oldest and ResolveHits applies each hit in turn
/// (ServerSimulation.cs:722), so a zero-knockback pulse resolving after the detonation
/// would overwrite the whole blast with KV = 0 and hand the victim back to Idle.
///
/// Escape is a real out and is deliberately not defended against: both the drag pulses and
/// the detonation are radius-gated by the same instance, so a target that leaves keeps the
/// tick damage already dealt and takes nothing else. No tether, no re-capture.
///
/// ── Jump is NOT gated on the lock (engine-wide, out of this class's hands) ──
/// The +1 on AnimLockTicks closes the dash-cancel hole, because StartDash is gated on
/// AnimLockTicks == 0 (Simulation.cs:252). Jump is NOT: the jump branch at
/// Simulation.cs:220 tests only HitstunTicks, JumpsLeft and JumpSquat. Pressing jump on any
/// tick of this ability — including the detonation tick — sets State = JumpSquat, and the
/// next TickAbilities drops the instance without OnEnd (ServerSimulation.cs:143-150) while
/// still charging the full 540-tick cooldown. So "Nilus is locked in place for the whole
/// ability" above is true for movement input and dash, and FALSE for jump.
/// An earlier pull ability had the identical hole, so gating jump on AnimLockTicks would
/// change the feel of every committed ability on the roster and is an owner decision, not a bug fix.
/// F_JumpCancelsTheUltAtFullCooldownCost pins the current behaviour so that a future gate
/// shows up as a failing test rather than a silent feel change.
///
/// Params: windup_ticks, drag_duration_ticks, drag_radius, drag_force,
/// drag_interval_ticks, drag_damage, detonation_damage, detonation_kb_angle,
/// detonation_kb_base, detonation_kb_growth, detonation_stun_ticks.
/// </summary>
public sealed class NilusEventHorizon : ServerAbility
{
    private ushort _ticks;
    private ushort _windupTicks;
    private ushort _dragDuration;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;
        _windupTicks = (ushort)GetParam(def, "windup_ticks", 72f);
        _dragDuration = (ushort)GetParam(def, "drag_duration_ticks", 60f);

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        // One tick longer than the ability itself — see the class remarks.
        s.AnimLockTicks = (ushort)(_windupTicks + _dragDuration + 1);

        s.VX = 0f;
        s.VZ = 0f;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        // Rooted for the entire ability. This is the commitment, and it is what keeps the
        // instance in Attacking (and therefore alive) until the detonation.
        s.VX = 0f;
        s.VZ = 0f;

        // ── Phase 1: windup / telegraph ──
        if (_ticks <= _windupTicks)
            return;

        AnimIndex = 1;
        ushort dragElapsed = (ushort)(_ticks - _windupTicks);
        float dragRadius = GetParam(def, "drag_radius", 6f);

        // ── Phase 3 first: the detonation owns its tick exclusively ──
        if (dragElapsed >= _dragDuration)
        {
            float damage = GetParam(def, "detonation_damage", 18f);
            // The blast is the rift's own mouth: there is no separate detonation radius in
            // the spec, and a blast smaller than the drag could not reach what it dragged in.
            float radius = dragRadius;

            var (kbAngle, kbBase, kbGrowth) = new KnockbackData
            {
                Profile = KnockbackProfile.Custom,
                Angle = (sbyte)GetParam(def, "detonation_kb_angle", 40f),
                BaseKnockback = GetParam(def, "detonation_kb_base", 16f),
                KnockbackGrowth = GetParam(def, "detonation_kb_growth", 9f),
            }.Resolve();

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
                StunTicks = (ushort)GetParam(def, "detonation_stun_ticks", 40f),
                DurationTicks = 5,
                // Without a rehit interval SpellResolver deactivates a hitbox after its FIRST
                // victim (SpellResolver.cs:250-251) — and dictionary iteration order, not
                // distance, picks which one. An 18-damage kill blast advertised as hitting
                // "everything it dragged in" must hit everything it dragged in. Equal to
                // DurationTicks so the age gate matches only at 0: one pulse, every body in
                // radius, no double-hit.
                RehitIntervalTicks = 5,
                OwnerId = s.EntityId,
            });

            EndAbility(ref s);
            return;
        }

        // ── Phase 2: drag pulses ──
        // Phased off the drag's FIRST tick, so the full six land inside a 60-tick window
        // and none of them shares a tick with the detonation.
        ushort dragInterval = (ushort)GetParam(def, "drag_interval_ticks", 10f);
        if (dragInterval == 0 || (dragElapsed - 1) % dragInterval != 0)
            return;

        float dragForce = GetParam(def, "drag_force", 3f);

        if (SimulationStates != null)
        {
            foreach (var kvp in SimulationStates)
            {
                ulong otherId = kvp.Key;
                if (otherId == s.EntityId) continue;

                var other = kvp.Value;
                float dist = CombatMath.HorizontalDistance(s.PX, s.PZ, other.PX, other.PZ);
                if (dist > dragRadius) continue;
                // Already at the centre: nothing to pull, and CalculateKnockback's degenerate
                // case would shove them along world +Z instead (CombatMath.cs:115). Its own
                // test is on the SQUARED distance (`distSq > 0.001f`, CombatMath.cs:106), i.e.
                // an effective threshold of ~0.0316 — so the guard has to square too, or
                // targets in [0.01, 0.0316) slip through into the case it exists to prevent.
                if (dist * dist <= 0.001f) continue;

                // direction from the OTHER entity toward Nilus, i.e. inward. Structs in the
                // dictionary are values — copy out, modify, copy back.
                // See the class remarks for why hitstun does not shield a target from the pull.
                CombatMath.CalculateKnockback(s.PX, s.PZ, other.PX, other.PZ,
                    dragForce, 0f, out float kx, out float _, out float kz);
                other.VX += kx;
                other.VZ += kz;
                SimulationStates[otherId] = other;
            }
        }

        // The damage pulse riding the drag: no knockback, no stun. Both zeroes are load
        // bearing, not defaults — any knockback here would put the target in hitstun and
        // ProcessHitstun would erase the drag velocity written above. DurationTicks = 1 so a
        // pulse that misses expires immediately instead of lingering into a later tick.
        float pulseDamage = GetParam(def, "drag_damage", 3f);
        float pulseRadius = dragRadius;

        Resolver.Spawn(new Hitbox
        {
            X = s.PX, Y = s.PY + 0.5f, Z = s.PZ,
            EndX = s.PX, EndY = s.PY + 0.5f, EndZ = s.PZ,
            Radius = pulseRadius,
            Shape = HitboxShape.Sphere,
            Damage = pulseDamage,
            BaseKnockback = 0f,
            KnockbackGrowth = 0f,
            KnockbackAngle = 0,
            StunTicks = 0,
            DurationTicks = 1,
            // Same reason as the detonation: a pulse with no rehit interval damages one
            // arbitrary victim out of everything the drag just vacuumed in. With
            // DurationTicks = 1 the zone pulses exactly once, at age 0, then expires.
            RehitIntervalTicks = 1,
            OwnerId = s.EntityId,
        });
    }
}
