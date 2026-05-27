using System;

namespace SlopArena.Shared
{
	/// <summary>
	/// Action slot identifiers.
	/// </summary>
	public enum SlotType
	{
		Slot1,  // Slot 1 (Spam / Basic)
		Slot2,  // Slot 2 (Combo)
		Slot3,  // Slot 3 (Burst)
		Slot4,  // Slot 4 (Utility)
		Slot5,  // Slot 5 (Control / Zone)
		Slot6,  // Slot 6 (Utility 2 / Defense)
		Slot7,  // Slot 7 (Mobility / Dash)
		Slot8   // Slot 8 (Ultimate)
	}
	
	/// <summary>
	/// Static definition of a spell, wrapping SpellDefinition from Shared.
	/// The actual effect is delegated via a callback Action.
	/// 
	/// Generic: works with any CombatComponent (player, dummy, AI bot).
	/// </summary>
	public class SpellData
	{
		public int SpellID;
		public string Name;
		public float CooldownMax;   // Cooldown in seconds
		public float CastTime;      // 0 = Instant
		public float StunDuration;  // Hitstun in seconds (0 = no stun)
		public Action<CombatComponent>? ActionEffect;  // The spell's effect
		
		public SpellData(int id, string name, float cd, float castTime, float stunDuration, Action<CombatComponent>? effect)
		{
			SpellID = id;
			Name = name;
			CooldownMax = cd;
			CastTime = castTime;
			StunDuration = stunDuration;
			ActionEffect = effect;
		}
		
		/// <summary>
		/// Create SpellData from a Shared SpellDefinition.
		/// </summary>
		public static SpellData FromDefinition(SpellDefinition def, Action<CombatComponent>? effect)
		{
			return new SpellData(def.SpellID, def.Name, def.Cooldown, def.CastTime, def.StunDuration, effect);
		}
	}
}