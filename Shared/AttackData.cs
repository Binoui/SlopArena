using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// One stage of an ability. A simple ability has 1 stage.
    /// A combo ability (like LMB) has N stages chained by ChainWindowTicks.
    /// Hitboxes are spawned by the ability code, not defined in AttackStage.
    /// </summary>
    public struct AttackStage
    {
        public float Damage;
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
    /// SpecialEffectKeys reference methods in AbilityRegistry for
    /// effects that stages can't express (hitbox spawning, teleport, etc.).
    /// AnimationNames per stage: "attack_2h_slice", "spell_cast", etc.
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

        /// <summary>Special effects (hitbox spawning, teleport, buff, delayed AoE). Keys in AbilityRegistry.</summary>
        public string[]? SpecialEffectKeys;

        /// <summary>
        /// Animation name for each stage.
        /// Each character defines their own animation keys.
        /// Example: LMB = ["attack_2h_slice", "attack_2h_chop", "attack_2h_spin"]
        /// </summary>
        public string[]? AnimationNames;
    }
}
