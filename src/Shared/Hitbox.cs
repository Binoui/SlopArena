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
        /// <summary>Launch angle in degrees (-90 to 90). Resolved from profile at spawn time.</summary>
        public sbyte KnockbackAngle;
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

        /// <summary>
        /// 0 = one-hit-then-die (default melee/projectile behaviour).
        /// &gt; 0 = lingering zone: tests collisions only when AgeTicks % RehitIntervalTicks == 0,
        /// hits every overlapping entity on that pulse, and survives until DurationTicks expires.
        /// The expiry path is unchanged, so a zone carrying an Explosion still queues it once
        /// when the zone times out — leave Explosion null if the zone should not burst on death.
        /// </summary>
        public ushort RehitIntervalTicks;

        /// <summary>
        /// If true this hitbox never scans bodies: no HitResult, no damage, no knockback, and
        /// crucially no <c>Active = false</c> on contact, so it keeps travelling and still
        /// reaches <see cref="SpellResolver.CheckGroundCollision"/>. For a projectile whose
        /// payload is its <see cref="Explosion"/> rather than its impact (Nilus' Q seed), the
        /// default one-hit behaviour would otherwise strand the explosion at the pre-move
        /// mid-air position and hand the clipped entity a free ability-cancel via
        /// <c>ApplyKnockback</c>'s zero-magnitude else branch.
        /// Aging, expiry, explosion queueing and ground collision are all unaffected.
        /// </summary>
        public bool IgnoresEntities;
    }
}
