using System;

namespace SlopArena.Shared
{
    public enum HitboxShape : byte
    {
        Sphere = 0,
        Capsule = 1  // segment from (X,Y,Z) to (EndX,EndY,EndZ), radius = Radius
    }

    /// <summary>
    /// A single hitbox spawned by an ability.
    /// Position is absolute (set at spawn time).
    /// Velocity (0,0,0) = static melee hitbox, non-zero = projectile.
    /// Shape: Sphere (default) or Capsule (uses EndX/EndY/EndZ).
    /// Resolved via sphere/capsule collision each tick in SpellResolver.
    /// </summary>
    public struct Hitbox
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float Radius;
        public ushort DurationTicks;
        public ushort AgeTicks;

        // Capsule support
        public HitboxShape Shape;
        public float EndX, EndY, EndZ;   // Capsule end point (ignored for Sphere)

        // Damage data
        public float Damage;
        public float KnockbackForce;
        public float KnockbackUpward;
        public ushort StunTicks;
        public ulong OwnerId;

        public bool Active;
    }
}
