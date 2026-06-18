using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// Targeted projectile ability spec. Adds a ProjectileConfig
    /// for parabolic-arc throw abilities (e.g., Manki Q).
    /// Self-spawns the projectile hitbox via SpawnHitbox override.
    /// </summary>
    public class RoundBombSpec : AbilitySpec
    {
        public ProjectileConfig ProjectileConfig;
        public string LoopAnimName = "";

        public override bool SpawnHitbox(HitboxEvent evt, CharacterState state, CharacterDefinition def, SpellResolver resolver, ulong ownerId)
        {
            var pc = ProjectileConfig;
            float D = Math.Clamp(state.AimTargetDistance, 0.5f, pc.MaxRange);
            float launchRad = pc.LaunchAngleDeg * (MathF.PI / 180f);
            float g = pc.Gravity;
            float dY = -def.CapsuleHeight * 0.5f - pc.LaunchOffsetY;

            CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
                out float _, out float hSpeed, out float vSpeed);

            float aimCos = MathF.Cos(state.AimYaw);
            float aimSin = MathF.Sin(state.AimYaw);

            resolver.Spawn(new Hitbox
            {
                X = state.PX, Y = state.PY + pc.LaunchOffsetY, Z = state.PZ,
                VX = hSpeed * aimSin, VY = vSpeed, VZ = hSpeed * aimCos,
                Radius = pc.HitboxRadius, Shape = HitboxShape.Sphere,
                EndX = state.PX, EndY = state.PY, EndZ = state.PZ,
                Damage = pc.Damage, KnockbackForce = pc.KnockbackForce,
                KnockbackUpward = pc.KnockbackUpward, StunTicks = pc.StunTicks,
                DurationTicks = pc.MaxFlightTicks, OwnerId = ownerId, Gravity = g,
                Explosion = pc.Explosion,
            });
            return true;
        }
    }
}
