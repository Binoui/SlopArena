using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>
    /// Pure C# ability specification — the single source of truth for an ability.
    /// Instantiated directly in character data files (MankiData, BunnyData).
    ///
    /// The server reads Stages/HitboxEvents/CooldownTicks from this.
    /// The client wraps this in an Ability subclass for input/indicators.
    /// </summary>
    public class AbilitySpec
    {
        public string Name = "";
        /// <summary>
        /// DEPRECATED: No longer used since slot-based mapping.
        /// Factory now maps by (CharacterClass, slot) instead of global typeId.
        /// </summary>
        public byte AbilityTypeId;
        /// <summary>0 = no cooldown</summary>
        public ushort CooldownTicks;
        public AttackStage[] Stages = [];
        /// <summary>Hold-to-charge variant. Triggers after ChargeHoldTicks.</summary>
        public AttackStage[]? ChargedStages;
        public ushort ChargeHoldTicks;
        /// <summary>Special effects (hitbox spawning VFX, projectiles). Keys in AbilityRegistry.</summary>
        public string[]? SpecialEffectKeys;
        /// <summary>Animation name per combo stage.</summary>
        public string[]? AnimationNames;
        /// <summary>Named float parameters for server-side abilities (e.g., "backflip_damage").</summary>
        public Dictionary<string, float> Params = new();

        /// <summary>
        /// Get the animation name for a given combo stage.
        /// Falls back to "melee".
        /// </summary>
        public string GetAnimationName(int comboStage)
        {
            if (AnimationNames != null && AnimationNames.Length > 0)
                return AnimationNames[comboStage % AnimationNames.Length];
            return "melee";
        }

        /// <summary>
        /// Called when a hitbox event triggers during simulation.
        /// Override for custom hitbox spawning (projectiles, mines, etc.).
        /// Return true if handled (skip default melee hitbox), false to fall through.
        /// </summary>
        public virtual bool SpawnHitbox(HitboxEvent evt, CharacterState state, CharacterDefinition def, SpellResolver resolver, ulong ownerId)
        {
            return false;
        }
    }
}
