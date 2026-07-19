namespace SlopArena.Shared
{
    /// <summary>
    /// A hitbox spawned during an attack at a specific tick.
    /// </summary>
    /// <summary>Hitbox event triggered by an attack stage at a specific tick.</summary>
    public struct HitboxEvent
    {
        /// <summary>Frame offset (in ticks) from the start of the stage.</summary>
        public ushort TriggerTick;
        /// <summary>Lifetime of the spawned hitbox in ticks.</summary>
        public ushort DurationTicks;
        /// <summary>Hitbox shape (Sphere or Capsule).</summary>
        public HitboxShape Shape;
        public float Radius;
        /// <summary>Offset from attacker center (rotated by facing yaw).</summary>
        public float OffX, OffY, OffZ;
        /// <summary>Capsule end offset (relative to OffX/Y/Z, rotated by facing yaw).</summary>
        public float EndOffX, EndOffY, EndOffZ;
        /// <summary>If set, position hitbox at this bone's world position instead of OffX/Y/Z. Null = use existing entity-relative offset.</summary>
        public string? BoneName;
        /// <summary>Local offset from bone origin (applied after bone position is resolved).</summary>
        public float BoneOffX, BoneOffY, BoneOffZ;
        public float Damage;
        public float BaseKnockback;
        public float KnockbackGrowth;
        public float KnockbackUpward;
        public ushort StunTicks;
        /// <summary>If false: persists even if attacker is hit during startup.</summary>
        public bool Interruptible;
    }

    /// <summary>
    /// One stage of an ability. A simple ability has 1 stage.
    /// A combo ability (like LMB) has N stages chained by ChainWindowTicks.
    /// </summary>
    public struct AttackStage
    {
        /// <summary>Total animation lock duration in ticks.</summary>
        public ushort DurationTicks;
        /// <summary>Hitbox events triggered during this stage.</summary>
        public HitboxEvent[] HitboxEvents;
        /// <summary>Forward burst at attack start (applied once).</summary>
        public float LungeForce;
        /// <summary>Per-tick velocity during this stage (world space). Set VY for jump arcs.</summary>
        public float MoveX, MoveY, MoveZ;
        /// <summary>
        /// 0 = final stage / no chain. Non-zero = frames to buffer next input.
        /// </summary>
        public ushort ChainWindowTicks;

        /// <summary>Distance where auto-dash triggers (e.g., 12m)</summary>
        public float AttackRange;
        public float WarpRange;
        /// <summary>
        /// Warp drives the entity toward target at SprintSpeed (constant velocity).
        /// See Simulation.ProcessWarp for the implementation.
        /// </summary>
        public bool UseTargetLock;     // true = use soft lock system for this attack
        /// <summary>
        /// true = auto-rotate toward target during attack
        /// </summary>
        public bool RotateTowardTarget;
        /// <summary>
        /// 0-1: rotation lerp toward target per frame (0.8 = strong tracking)
        /// </summary>
        public float TrackingStrength;
        /// <summary>Optional bone trail VFX for this stage. If null, falls back to AbilitySpec.BoneTrails.</summary>
        public BoneTrailDef[]? BoneTrails;
    }

    /// <summary>
    /// Config for a targeted projectile ability (hold-to-aim, release-to-throw).
    /// The projectile trajectory is a parabolic arc computed from the client's
    /// aim direction + distance.
    /// </summary>
    public struct ProjectileConfig
    {
        /// <summary>Launch angle above horizontal in degrees (e.g., 30 = 30° arc).</summary>
        public float LaunchAngleDeg;
        /// <summary>Gravity applied to the projectile per tick (m/s²). Use sim gravity (35) for consistency.</summary>
        public float Gravity;
        /// <summary>Max targeting range in meters (e.g., 20).</summary>
        public float MaxRange;
        /// <summary>Hitbox radius for the projectile sphere.</summary>
        public float HitboxRadius;
        /// <summary>Launch height offset from character feet (e.g., 1.2 = hand height).</summary>
        public float LaunchOffsetY;
        /// <summary>Damage on direct hit.</summary>
        public float Damage;
        /// <summary>Minimum horizontal knockback (at 0% damage).</summary>
        public float BaseKnockback;
        /// <summary>Additional knockback per (damage% * 0.01).</summary>
        public float KnockbackGrowth;
        /// <summary>Vertical knockback (upward).</summary>
        public float KnockbackUpward;
        /// <summary>Stun ticks on hit.</summary>
        public ushort StunTicks;
        /// <summary>Max lifetime of the projectile in ticks (600 = 10 seconds, more than enough).</summary>
        public ushort MaxFlightTicks;
        /// <summary>
        /// Optional explosion on impact (entity hit or ground). If set, a spherical AoE
        /// hitbox spawns at the projectile's last position when it deactivates.
        /// </summary>
        public ProjectileExplosion? Explosion;
    }

    /// <summary>
    /// Explosion spawned when a projectile hits an entity or the ground.
    /// Larger radius, separate damage/knockback from the direct projectile hit.
    /// </summary>
    public struct ProjectileExplosion
    {
        public float Radius;
        public float Damage;
        public float BaseKnockback;
        public float KnockbackGrowth;
        public float KnockbackUpward;
        public ushort StunTicks;
        public ushort DurationTicks;
        /// <summary>If true, this explosion can hit its spawner (mine jump, etc.).</summary>
        public bool CanHitOwner;
    }
}

