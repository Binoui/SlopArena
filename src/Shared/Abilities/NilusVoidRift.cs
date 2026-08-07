using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Nilus' Q — Void Rift (signature).
///
/// Hold to aim a ground target, release to lob a void seed on a parabolic arc. The seed
/// does not interact with bodies at all (Hitbox.IgnoresEntities), so it always completes
/// its flight: when it reaches the ground, SpellResolver.CheckGroundCollision spawns its
/// ProjectileExplosion at ground level — and because that explosion carries
/// RehitIntervalTicks, the result is a LINGERING RIFT that damages everything inside
/// on an interval for rift_duration_ticks.
///
/// The rift deliberately outlives the cast: it lives in the hitbox layer, which is
/// aged by SpellResolver and is not tied to this ability instance (ServerSimulation
/// discards ability instances as soon as the caster leaves ActionState.Attacking).
///
/// The hold/aim/throw lifecycle lives in <see cref="AimHoldAbility"/>; the hold cap is
/// the spec's ChargeHoldTicks (see the base class doc — no charge_hold_ticks Param, so
/// the auto-release can't drift from the Simulation.cs clamp).
///
/// Params: throw_trigger_tick, throw_duration, max_range,
/// launch_angle, gravity, launch_offset_y, hitbox_radius, seed_damage,
/// max_flight_ticks, rift_radius, rift_damage, rift_duration_ticks,
/// rift_rehit_ticks, rift_stun_ticks, rift_kb_angle, rift_kb_base, rift_kb_growth.
/// </summary>
public sealed class NilusVoidRift : AimHoldAbility
{
    private float _cachedAimDistance;
    private float _cachedAimYaw;

    protected override int GetMidHoldAnimIndex(CharacterDefinition def) => 1;
    protected override byte GetReleaseAnimIndex(CharacterDefinition def) => 2;

    protected override void OnAimStart(ref CharacterState s, CharacterDefinition def)
    {
        _cachedAimDistance = 0f;
        _cachedAimYaw = 0f;
    }

    protected override void OnRelease(ref CharacterState s, CharacterDefinition def)
    {
        if (Simulation.OnDebugLog != null)
            Simulation.OnDebugLog.Invoke(
                $"[NilusQ] Release -> throw! ticks={s.AttackElapsedTicks} " +
                $"aiming={s.IsAiming} charge={s.ChargeTicks}/{GetMaxHoldTicks(def)} " +
                $"aimDist={s.AimTargetDistance:F2} aimYaw={s.AimYaw:F2}");
        _cachedAimDistance = s.AimTargetDistance;
        _cachedAimYaw = s.AimYaw;
    }

    protected override void OnFire(ref CharacterState s, CharacterDefinition def)
        => SpawnSeed(ref s, def);

    private void SpawnSeed(ref CharacterState s, CharacterDefinition def)
    {
        float distance = Math.Clamp(_cachedAimDistance, 0.5f, GetParam(def, "max_range", 12f));
        float launchAngleDeg = GetParam(def, "launch_angle", 30f);
        float g = GetParam(def, "gravity", 30f);
        float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
        float dY = (-def.CapsuleHeight * 0.5f) - launchOffsetY;

        CombatMath.ComputeProjectileLaunch(distance, launchAngleDeg * (MathF.PI / 180f), g, dY,
            out float _, out float hSpeed, out float vSpeed);

        float aimCos = MathF.Cos(_cachedAimYaw);
        float aimSin = MathF.Sin(_cachedAimYaw);

        float riftDamage = GetParam(def, "rift_damage", 3f);
        float riftRadius = GetParam(def, "rift_radius", 3f);
        ApplyBuffBonuses(ref s, ref riftDamage, ref riftRadius);

        Resolver.Spawn(new Hitbox
        {
            X = s.PX,
            Y = s.PY + launchOffsetY,
            Z = s.PZ,
            VX = hSpeed * aimSin,
            VY = vSpeed,
            VZ = hSpeed * aimCos,
            Radius = GetParam(def, "hitbox_radius", 0.5f),
            Shape = HitboxShape.Sphere,
            EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
            // The seed is inert — the rift is the payload — and IgnoresEntities is what makes
            // "inert" true. Without it, clipping a body deactivates the seed (SpellResolver.cs:250)
            // and the expiry path strands the rift at the pre-move mid-air position, while the
            // zero-magnitude HitResult forces the victim to Idle and cancels its ability for free.
            IgnoresEntities = true,
            Damage = GetParam(def, "seed_damage", 0f),
            BaseKnockback = 0f,
            KnockbackGrowth = 0f,
            KnockbackAngle = 0,
            StunTicks = 0,
            DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 90f),
            OwnerId = s.EntityId,
            Gravity = g,
            Explosion = new ProjectileExplosion
            {
                Radius = riftRadius,
                Damage = riftDamage,
                Knockback = new()
                {
                    Profile = KnockbackProfile.Custom,
                    Angle = (sbyte)GetParam(def, "rift_kb_angle", 15f),
                    BaseKnockback = GetParam(def, "rift_kb_base", 2f),
                    KnockbackGrowth = GetParam(def, "rift_kb_growth", 1f),
                },
                StunTicks = (ushort)GetParam(def, "rift_stun_ticks", 6f),
                DurationTicks = (ushort)GetParam(def, "rift_duration_ticks", 240f),
                RehitIntervalTicks = (ushort)GetParam(def, "rift_rehit_ticks", 30f),
            },
        });
    }
}
