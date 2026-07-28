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
/// Params: throw_trigger_tick, throw_duration, max_range,
/// launch_angle, gravity, launch_offset_y, hitbox_radius, seed_damage,
/// max_flight_ticks, rift_radius, rift_damage, rift_duration_ticks,
/// rift_rehit_ticks, rift_stun_ticks, rift_kb_angle, rift_kb_base, rift_kb_growth.
/// </summary>
public sealed class NilusVoidRift : ServerAbility
{
    private bool _seedSpawned;
    private float _cachedAimDistance;
    private float _cachedAimYaw;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _seedSpawned = false;
        _cachedAimDistance = 0f;
        _cachedAimYaw = 0f;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.IsAiming = true;
        s.AnimLockTicks = 8;
        s.ChargeTicks = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        // AbilitySpec.ChargeHoldTicks, not a Param: Simulation.cs:307-314 clamps s.ChargeTicks
        // with that same field for AimedProjectile slots, which is what makes the auto-release
        // below reachable at all. Two copies of one number would let a retune move the clamp
        // without moving the release, or the reverse.
        ushort maxHoldTicks = def.GetSlotAbility(Slot, airborne: false)?.ChargeHoldTicks ?? 180;
        bool dbg = Simulation.OnDebugLog != null;

        // ── Aim phase ──
        if (s.ComboStage == 0)
        {
            if (s.AttackElapsedTicks > 8 && AnimIndex != 1)
                AnimIndex = 1;

            bool released = !input.IsAiming || (maxHoldTicks > 0 && s.ChargeTicks >= maxHoldTicks);
            if (s.AttackElapsedTicks > 8 && released)
            {
                if (dbg) Simulation.OnDebugLog?.Invoke(
                    $"[NilusQ] Release -> throw! ticks={s.AttackElapsedTicks} " +
                    $"aiming={input.IsAiming} charge={s.ChargeTicks}/{maxHoldTicks} " +
                    $"aimDist={s.AimTargetDistance:F2} aimYaw={s.AimYaw:F2}");
                _cachedAimDistance = s.AimTargetDistance;
                _cachedAimYaw = s.AimYaw;
                s.ComboStage = 1;
                AnimIndex = 2;
                s.AttackElapsedTicks = 0;
            }
            return;
        }

        // ── Throw phase ──
        ushort throwTick = (ushort)GetParam(def, "throw_trigger_tick", 10f);
        if (!_seedSpawned && s.AttackElapsedTicks >= throwTick)
        {
            _seedSpawned = true;
            s.IsAiming = false;
            SpawnSeed(ref s, def);
        }

        ushort duration = (ushort)GetParam(def, "throw_duration", 40f);
        if (s.AttackElapsedTicks >= duration)
            EndAbility(ref s);
    }

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
