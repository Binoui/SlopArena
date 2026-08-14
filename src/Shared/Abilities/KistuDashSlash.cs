using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Kistu's E — Directional Dash Slash (aim-on-ground, release-to-dash).
///
/// Two phases:
///   Aim:   State = ActionState.Aiming — walk/run stays unlocked (SimulateTick runs
///          ProcessNormalMovement for Aiming), jump/dash/other-ability presses are
///          blocked (SimulateTick state gates). No aiming animation: the client plays
///          idle/run while in this state. The mouse rotates the aim direction
///          (input.AimYaw, camera locked client-side), and she turns to FACE it.
///   Dash:  State = ActionState.Attacking — constant velocity toward the cached aim
///          yaw for dash_duration_ticks, covering exactly dash_distance meters.
///          A single capsule hitbox sweeps along the aim axis from dash start (hits
///          her sides + the path, deactivates on its first victim). Plays the E attack
///          clip (AnimationNames[0] = "spell_e").
///
/// The aim yaw is cached on every aim tick — on the release tick the client sends
/// camera yaw instead of the mouse aim (InputController default), so reading s.AimYaw
/// at release would snap the dash to the camera. The cache is the last mouse aim.
///
/// All damage/knockback/timing data comes from the spec (Params + Stages[0].HitboxEvents).
/// </summary>
public sealed class KistuDashSlash : ServerAbility
{
    private enum Phase { Aim, Dash }

    private Phase _phase;
    private int _phaseTicks;
    private float _dashYaw;
    private bool _hitboxSpawned;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Aim;
        _phaseTicks = 0;

        s.State = ActionState.Aiming;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.IsAiming = true;
        s.StateTicks = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _phaseTicks++;

        if (_phase == Phase.Aim)
        {
            // Cache the mouse aim on real aim frames only. On the release frame the client
            // sends camera yaw instead of the mouse aim (InputController default), so caching
            // unconditionally would snap the dash to the camera.
            if (input.IsAiming)
            {
                _dashYaw = s.AimYaw;
                // Face the chosen direction while aiming so the dash starts oriented correctly
                // (deliberate exception to input-only facing, scoped to this ability).
                s.FacingYaw = s.AimYaw;
            }

            ushort maxAimTicks = (ushort)GetParam(def, "max_aim_ticks", 180f);
            if (!input.IsAiming || _phaseTicks >= maxAimTicks)
                StartDash(ref s, def);
            return;
        }

        // ── Dash phase ──
        float dashDuration = GetParam(def, "dash_duration_ticks", 16f);
        float dashDistance = GetParam(def, "dash_distance", 5f);
        // Momentum-preserve (issue #115): attacking applies no ground friction, so the
        // integrated displacement is exactly speed × duration — no friction compensation.
        float speed = dashDuration > 0f
            ? dashDistance / (dashDuration * Simulation.TickDt)
            : 0f;
        s.VX = MathF.Sin(_dashYaw) * speed;
        s.VZ = MathF.Cos(_dashYaw) * speed;

        // Single sweep hitbox, spawned once at dash start: it travels glued to the character
        // and SpellResolver deactivates it after its FIRST victim — one hit per dash, unlike
        // per-tick respawns which would re-hit a knockback-carried target every tick.
        if (!_hitboxSpawned)
        {
            SpawnDashHitbox(ref s, def, speed, (ushort)dashDuration);
            _hitboxSpawned = true;
        }

        // End AFTER the last full velocity tick (strict >) so the dash covers a full
        // dashDuration ticks of travel, not dashDuration - 1.
        if (_phaseTicks > dashDuration)
            EndAbility(ref s);
    }

    private void StartDash(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Dash;
        _phaseTicks = 0;

        s.State = ActionState.Attacking;
        s.AttackElapsedTicks = 0;
        s.IsAiming = false;
        AnimIndex = 0;

        ushort dashDuration = (ushort)GetParam(def, "dash_duration_ticks", 16f);
        // One tick longer than the dash: TickTimers decrements the lock before TickAbilities
        // runs, so a lock of exactly dashDuration would hit 0 on the last dash tick and let a
        // held dash key cancel the final frame (NilusEventHorizon documents the same pattern).
        s.AnimLockTicks = (ushort)(dashDuration + 1);
    }

    /// <summary>
    /// Capsule sweep along the aim axis, spawned once at dash start: covers her sides
    /// (radius) plus a forward reach (OffZ → EndOffZ from the spec) and travels at the
    /// character's effective velocity so it stays glued to her for the whole dash.
    /// </summary>
    private void SpawnDashHitbox(ref CharacterState s, CharacterDefinition def, float followSpeed, ushort durationTicks)
    {
        var spec = def.GetSlotAbility(Slot, false);
        if (spec?.Stages is not { Length: > 0 }) return;
        var stage = spec.Stages[0];
        if (stage.HitboxEvents == null || stage.HitboxEvents.Length == 0) return;
        var evt = stage.HitboxEvents[0];

        float cos = MathF.Cos(_dashYaw);
        float sin = MathF.Sin(_dashYaw);

        float sx = s.PX + (evt.OffZ * sin);
        float sz = s.PZ + (evt.OffZ * cos);
        float ex = s.PX + (evt.EndOffZ * sin);
        float ez = s.PZ + (evt.EndOffZ * cos);

        float damage = evt.Damage;
        float radius = evt.Radius;
        ApplyBuffBonuses(ref s, ref damage, ref radius);

        var (kbAngle, kbBase, kbGrowth) = evt.Knockback.Resolve();

        Resolver.Spawn(new Hitbox
        {
            X = sx,
            Y = s.PY + evt.OffY,
            Z = sz,
            // Follow the dash so the swept capsule stays glued to the character.
            // followSpeed is the dash speed itself — no friction acts during the dash
            // (momentum-preserve, issue #115).
            VX = MathF.Sin(_dashYaw) * followSpeed,
            VY = 0f,
            VZ = MathF.Cos(_dashYaw) * followSpeed,
            Radius = radius,
            Shape = evt.Shape,
            EndX = ex,
            EndY = s.PY + evt.OffY,
            EndZ = ez,
            Damage = damage,
            BaseKnockback = kbBase,
            KnockbackGrowth = kbGrowth,
            KnockbackAngle = kbAngle,
            StunTicks = evt.StunTicks,
            DurationTicks = durationTicks > 0 ? durationTicks : (ushort)1,
            OwnerId = s.EntityId,
            FreezesOwner = true,
            HitsMultipleOpponents = true,
        });
    }

    /// <summary>
    /// Precise landing (issue #115 carve-out): the dash-slash is a REPOSITION move whose
    /// authored endpoint is the aim distance — like a normal dash, which stops exactly at
    /// expiry (ProcessDash). Momentum-preserve coasts ATTACKS; movement tech lands exactly.
    /// </summary>
    public override void OnEnd(ref CharacterState s)
    {
        s.VX = 0f;
        s.VZ = 0f;
    }
}
