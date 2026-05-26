using SlopArena.Shared

namespace SlopArena.Shared
{
	/// <summary>
	/// Action slot identifiers.
	/// </summary>
	public enum SlotType
	{
		Slot1,  // Key 1 (Spam / Basic)
		Slot2,  // Key 2 (Combo)
		Slot3,  // Key 3 (Burst)
		Slot4,  // Key 4 (Utility)
		SlotA,  // Key A (Control / Zone)
		SlotE,  // Key E (Utility 2 / Defense)
		Shift,  // Shift key (Mobility / Dash)
		Elite   // R key (Ultimate)
using SlopArena.Shared

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
using SlopArena.Shared

		public SpellData(int id, string name, float cd, float castTime, float stunDuration, Action<CombatComponent>? effect)
		{
			SpellID = id;
			Name = name;
			CooldownMax = cd;
			CastTime = castTime;
			StunDuration = stunDuration;
			ActionEffect = effect;
using SlopArena.Shared

		/// <summary>
		/// Create SpellData from a Shared SpellDefinition.
		/// </summary>
		public static SpellData FromDefinition(SpellDefinition def, Action<CombatComponent>? effect)
		{
			return new SpellData(def.SpellID, def.Name, def.Cooldown, def.CastTime, def.StunDuration, effect);
		}
	}
using SlopArena.Shared
