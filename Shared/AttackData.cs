using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// Shape of an attack hitbox. All resolved via pure math in CombatMath.cs — no Godot physics.
    /// </summary>
    public enum AttackShape : byte
    {
        MeleeCone,   // Cone from player position in facing direction
        CircleAOE,   // Circle centered on player or target point
        Projectile,  // Spawns a projectile (handled by CombatComponent)
        Beam,        // Continuous beam (client visual + server tick check)
        SelfBuff,    // Applies a status to self, no hitbox
    }

    /// <summary>
    /// One stage of an ability. A simple ability has 1 stage.
    /// A combo ability (like LMB) has N stages chained by ChainWindowTicks.
    /// </summary>
    public struct AttackStage
    {
        public AttackShape Shape;
        public float Damage;
        public float Range;            // MeleeCone range / Projectile max distance
        public float HitAngleDeg;      // MeleeCone: half-angle in degrees
        public float Radius;           // CircleAOE / Projectile hit radius
        public float KnockbackForce;
        public float KnockbackUpward;
        public float LungeForce;       // Forward burst during attack
        public ushort StunTicks;       // Hitstun duration in ticks
        public ushort SelfLockTicks;   // Self animation lock in ticks
        public ushort ChainWindowTicks; // 0 = final stage / no chain
    }

    /// <summary>
    /// Full definition of one ability slot (0-5).
    /// Stages.Length = 1 for single hit, N for combo chains.
    /// SpecialEffectKeys reference methods in ClassAbilities for
    /// effects that stages can't express (teleport, self-buff, delayed AoE, status apply).
    /// </summary>
    public struct AbilityData
    {
        public string Name;
        public ushort CooldownTicks;   // 0 = no cooldown
        public AttackStage[] Stages;

        /// <summary>
        /// LMB/RMB hold-to-charge variant. If non-null, holding the key
        /// for ChargeHoldTicks triggers this instead of the press variant.
        /// </summary>
        public AttackStage[]? ChargedStages;
        public ushort ChargeHoldTicks; // How many ticks to hold before charged version fires

        /// <summary>
        /// Optional special effects invoked AFTER stage resolution.
        /// Access hit targets via CombatComponent.GetTargetsFromLastHit().
        /// Keys reference methods in AbilityRegistry (same as old ClassAbilityKeys).
        /// Null/empty = no special effect.
        /// </summary>
        public string[]? SpecialEffectKeys;
    }
}
