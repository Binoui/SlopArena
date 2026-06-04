using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// A single hitbox spawned by an ability.
    /// Position is absolute (set at spawn time).
    /// Velocity (0,0,0) = static melee hitbox, non-zero = projectile.
    /// Resolved via sphere-sphere collision each tick in SpellResolver.
    /// </summary>
    public struct Hitbox
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float Radius;
        public ushort DurationTicks;
        public ushort AgeTicks;

        // Damage data
        public float Damage;
        public float KnockbackForce;
        public float KnockbackUpward;
        public ushort StunTicks;
        public ulong OwnerId;

        public bool Active;
    }
}
