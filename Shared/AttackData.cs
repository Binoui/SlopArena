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
    /// AnimationNames per stage: "punch", "kick", "supernova" etc.
    /// Null/empty = use generic fallback ("attack_{slot}_{stage}" / "cast_{slot}").
    /// </summary>
    public struct AbilityData
    {
        public string Name;
        public ushort CooldownTicks;   // 0 = no cooldown
        public AttackStage[] Stages;

        /// <summary>Hold-to-charge variant. Triggers after ChargeHoldTicks.</summary>
        public AttackStage[]? ChargedStages;
        /// <summary>Ticks to hold before charged version fires.</summary>
        public ushort ChargeHoldTicks;

        /// <summary>Special effects (teleport, buff, delayed AoE). Keys in AbilityRegistry.</summary>
        public string[]? SpecialEffectKeys;

        /// <summary>
        /// Animation name for each stage (or one element for single-stage / no-stage).
        /// Each character has their own animation FBX files, loaded by AnimationController.
        /// Example: LMB = ["punch_jab", "punch_cross", "punch_uppercut"]
        /// Example: Q = ["supernova_start"] (for special effect with no stages)
        /// </summary>
        public string[]? AnimationNames;
    }
}
