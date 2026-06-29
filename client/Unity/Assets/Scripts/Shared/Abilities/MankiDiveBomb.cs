using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Manki's R (slot 4): Dive Bomb — air-to-ground slam.
/// Extends AimedGroundAbility: rise up, aim downward, then plunge into target zone.
/// AoE hitbox spawns at target on transition to resolve phase.
/// </summary>
public sealed class MankiDiveBomb : AimedGroundAbility
{
    protected override void Resolve(ref CharacterState s, CharacterDefinition def, float targetX, float targetZ)
    {
        // Spawn AoE hitbox at target ground position
        float radius = GetParam(def, "hitbox_radius", 3f);
        float damage = GetParam(def, "damage", 18f);
        float kbForce = GetParam(def, "knockback_force", 22f);
        float kbUp = GetParam(def, "knockback_upward", 8f);
        ushort stun = (ushort)GetParam(def, "stun_ticks", 20f);
        ushort duration = (ushort)GetParam(def, "hitbox_duration_ticks", 20f);

        // Apply Overclock buff bonus
        ApplyBuffBonuses(ref s, ref damage, ref radius);

        // Hitbox at ground level at the target position
        float groundY = 0.3f; // slightly above ground
        Resolver.Spawn(new Hitbox
        {
            X = targetX,
            Y = groundY,
            Z = targetZ,
            Radius = radius,
            Shape = HitboxShape.Sphere,
            Damage = damage,
            KnockbackForce = kbForce,
            KnockbackUpward = kbUp,
            StunTicks = stun,
            DurationTicks = duration,
            OwnerId = s.EntityId,
        });
    }
}
