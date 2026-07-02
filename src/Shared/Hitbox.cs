namespace SlopArena.Shared
{
    public enum HitboxShape : byte
    {
        Sphere = 0,
        /// <summary>
        /// segment from (X,Y,Z) to (EndX,EndY,EndZ), radius = Radius
        /// </summary>
        Capsule = 1
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

        /// <summary>
        /// Capsule support
        /// </summary>
        public HitboxShape Shape;
        /// <summary>
        /// Capsule end point (ignored for Sphere)
        /// </summary>
        public float EndX, EndY, EndZ;

        /// <summary>
        /// Damage data
        /// </summary>
        public float Damage;
        public float BaseKnockback;
        public float KnockbackGrowth;
        public float KnockbackUpward;
        public ushort StunTicks;
        public ulong OwnerId;

        public bool Active;

        /// <summary>
        /// Gravity applied each tick (m/s²). 0 = no gravity (default for melee hitboxes).
        /// </summary>
        public float Gravity;

        /// <summary>
        /// Optional explosion spawned when this hitbox deactivates (hits entity or expires).
        /// Used by projectiles to create an AoE burst on impact.
        /// </summary>
        public ProjectileExplosion? Explosion;

        /// <summary>If true, this hitbox can hit the entity that spawned it.</summary>
        public bool CanHitOwner;
    }
}
