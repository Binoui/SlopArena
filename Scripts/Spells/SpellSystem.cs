#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using MoveBox.Shared;

/// <summary>
/// Spell system managing action slots, cooldowns, and spell registration.
/// 
/// 40 spells organised in categories:
///   Apply (8) — apply a status effect to targets
///   Consume (8) — consume a status on targets for bonus effects  
///   Control (8) — knockback, stun, pull, zone control
///   Utility (8) — mobility, shield, counter, cleanse
///   Elite (8) — ultimate spells, high CD, massive impact
/// 
/// Key design: spells interact via status effects (StatusType).
/// Players discover combos by trying spell combinations.
/// </summary>
public partial class SpellSystem : Node
{
	// ==========================================
	// ACTION BAR : Slot → SpellData
	// ==========================================
	
	private Dictionary<SlotType, SpellData> _actionBar = new Dictionary<SlotType, SpellData>();
	
	// ==========================================
	// COOLDOWNS : Slot → remaining time
	// ==========================================
	
	private Dictionary<SlotType, float> _cooldowns = new Dictionary<SlotType, float>();
	
	// ==========================================
	// GLOBAL SPELL REGISTRY : SpellID → SpellData
	// ==========================================
	
	private static Dictionary<int, SpellData> _spellRegistry = new Dictionary<int, SpellData>();
	
	// ==========================================
	// PRESETS (optional, for UI)
	// ==========================================
	
	private static Dictionary<string, Dictionary<SlotType, int>> _presets = new Dictionary<string, Dictionary<SlotType, int>>();
	
	// ==========================================
	// INITIALIZATION
	// ==========================================
	
	public override void _Ready()
	{
		// Initialize cooldowns to 0 for all slots
		foreach (SlotType slot in Enum.GetValues<SlotType>())
		{
			_cooldowns[slot] = 0f;
		}
		
		// Register all spells globally
		RegisterAllSpells();
		
		// Register presets (synergy-based builds)
		RegisterPresets();
		
		// Load a default preset
		LoadPreset("Frostbite");
	}
	
	// ==========================================
	// GLOBAL SPELL REGISTRATION
	// ==========================================
	
	private static void RegisterAllSpells()
	{
		// ==================================================================
		// APPLY SPELLS (IDs 1-8) — Apply a status, moderate CD, low-mid damage
		// ==================================================================
		
		// ID 1 — Frost Bolt: projectile, applies Ralenti 3s
		Register(1,  "Frost Bolt",    3f, 0.20f, 0.5f,  StatusSpells.FrostBolt);
		// ID 2 — Shadow Mark: fast projectile, applies Marqué 5s
		Register(2,  "Shadow Mark",   2f, 0.15f, 0.3f,  StatusSpells.ShadowMark);
		// ID 3 — Ignite: slow projectile, applies Brûlure 4s
		Register(3,  "Ignite",        4f, 0.25f, 0.5f,  StatusSpells.Ignite);
		// ID 4 — Static Shock: beam hitscan, applies Electrifié 3s
		Register(4,  "Static Shock",  5f, 0.15f, 0.3f,  StatusSpells.StaticShock);
		// ID 5 — Sunder Armor: melee cone, applies Vulnérable 4s
		Register(5,  "Sunder Armor",  6f, 0.20f, 0.8f,  StatusSpells.SunderArmor);
		// ID 6 — Radiant Shield: self-buff, applies Bouclier 4s
		Register(6,  "Radiant Shield", 8f, 0.10f, 0.0f, StatusSpells.RadiantShield);
		// ID 7 — Freezing Trap: ground trap, applies Ralenti + bonus if already
		Register(7,  "Freezing Trap",  7f, 0.50f, 1.0f, StatusSpells.FreezingTrap);
		// ID 8 — Corrupted Ground: AoE zone, applies Vulnérable + Brûlure
		Register(8,  "Corrupted Ground", 9f, 0.40f, 1.5f, StatusSpells.CorruptedGround);
		
		// ==================================================================
		// CONSUME SPELLS (IDs 9-16) — Consume status for bonus damage/effects
		// ==================================================================
		
		// ID 9 — Piercing Shot: projectile, CONSUME Marqué → +100% dmg
		Register(9,  "Piercing Shot", 6f, 0.30f, 0.5f,  StatusSpells.PiercingShot);
		// ID 10 — Frost Lance: beam, CONSUME Ralenti → stun + extra dmg
		Register(10, "Frost Lance",   5f, 0.25f, 0.75f, StatusSpells.FrostLance);
		// ID 11 — Combustion: melee strike, CONSUME Brûlure → AoE explosion
		Register(11, "Combustion",    7f, 0.20f, 0.5f,  StatusSpells.Combustion);
		// ID 12 — Overload: AoE self, CONSUME Electrifié → stun
		Register(12, "Overload",      8f, 0.30f, 0.8f,  StatusSpells.Overload);
		// ID 13 — Execute: melee cone, CONSUME Vulnérable → +150% dmg
		Register(13, "Execute",       10f, 0.40f, 1.0f, StatusSpells.Execute);
		// ID 14 — Shield Bash: melee, CONSUME Bouclier → stun + bonus
		Register(14, "Shield Bash",   9f, 0.20f, 0.5f,  StatusSpells.ShieldBash);
		// ID 15 — Feedback Pulse: AoE self, CONSUME ALL → +dmg per status
		Register(15, "Feedback Pulse",7f, 0.30f, 0.5f,  StatusSpells.FeedbackPulse);
		// ID 16 — Dark Harvest: ranged, CONSUME any → heal
		Register(16, "Dark Harvest",  8f, 0.35f, 0.3f,  StatusSpells.DarkHarvest);
		
		// ==================================================================
		// CONTROL SPELLS (IDs 17-24) — Knockback, zone, stun, pull
		// ==================================================================
		
		// ID 17 — Wind Slash: melee cone, basic attack
		Register(17, "Wind Slash",    3f, 0.10f, 0.3f,  StatusSpells.WindSlash);
		// ID 18 — Arcane Shot: fast projectile
		Register(18, "Arcane Shot",   2f, 0.10f, 0.2f,  StatusSpells.ArcaneShot);
		// ID 19 — Power Strike: slow melee, heavy knockback
		Register(19, "Power Strike",  6f, 0.40f, 1.5f,  MeleeSpells.PowerStrike);
		// ID 20 — Force Push: AoE pushback around self
		Register(20, "Force Push",    8f, 0.20f, 0.5f,  StatusSpells.ForcePush);
		// ID 21 — Chain Pull: hook, pulls target toward you
		Register(21, "Chain Pull",    8f, 0.30f, 0.5f,  MeleeSpells.ChainPull);
		// ID 22 — Shockwave: wide melee cone, horizontal KB
		Register(22, "Shockwave",     7f, 0.35f, 1.2f,  MeleeSpells.Shockwave);
		// ID 23 — Ground Zone: AoE zone, ticks damage
		Register(23, "Void Zone",     10f, 0.40f, 1.0f, RangedSpells.PoisonFlask);
		// ID 24 — Counter: parry next 1.5s, stun attacker
		Register(24, "Counter",       10f, 0.10f, 0.5f, StatusSpells.Counter);
		
		// ==================================================================
		// UTILITY SPELLS (IDs 25-32) — Mobility, shield, utility
		// ==================================================================
		
		// ID 25 — Dash Roll: quick dodge with i-frames
		Register(25, "Dash Roll",     5f, 0.05f, 0.0f,  StatusSpells.DashRoll);
		// ID 26 — Blink: teleport forward 10m
		Register(26, "Blink",         8f, 0.05f, 0.0f,  StatusSpells.Blink);
		// ID 27 — Phase Shift: intangible (shield self + brief invuln)
		Register(27, "Phase Shift",   12f, 0.10f, 0.0f, StatusSpells.PhaseShift);
		// ID 28 — Iron Wall: defensive zone around self
		Register(28, "Iron Wall",     10f, 0.20f, 0.3f, MeleeSpells.IronWall);
		// ID 29 — Purify: remove all debuffs from self
		Register(29, "Purify",        12f, 0.15f, 0.0f, StatusSpells.Purify);
		// ID 30 — Blood Pact: sacrifice HP, gain damage buff
		Register(30, "Blood Pact",    14f, 0.20f, 0.5f, StatusSpells.BloodPact);
		// ID 31 — Magic Barrier: +50% magic resist
		Register(31, "Magic Barrier", 9f, 0.15f, 0.0f,  StatusSpells.MagicBarrier);
		// ID 32 — Charge: forward charge, knocks back enemies
		Register(32, "Charge",        8f, 0.10f, 0.5f,  MeleeSpells.Charge);
		
		// ==================================================================
		// ELITE SPELLS (IDs 33-40) — High CD, massive impact
		// ==================================================================
		
		// ID 33 — Meteor Rain: 5 meteors over 3s, CONSUME Brûlure → +50%
		Register(33, "Meteor Rain",   35f, 1.00f, 2.0f, StatusSpells.MeteorRain);
		// ID 34 — Annihilate: massive cone, CONSUME Vulnérable → +100%
		Register(34, "Annihilate",    40f, 1.50f, 3.0f, StatusSpells.Annihilate);
		// ID 35 — Storm Surge: buff, spells cast faster, CONSUME Electrifié → longer
		Register(35, "Storm Surge",   32f, 0.30f, 0.0f, StatusSpells.StormSurge);
		// ID 36 — Dark Pact: sacrifice HP, massive burst, CONSUME Bouclier → free
		Register(36, "Dark Pact",     30f, 0.50f, 1.0f, StatusSpells.DarkPact);
		// ID 37 — Bladestorm: spin attack, AoE 4m, multiple hits
		Register(37, "Bladestorm",    25f, 0.40f, 2.0f, MeleeSpells.Bladestorm);
		// ID 38 — Nova: huge burst AoE around self
		Register(38, "Nova",          28f, 0.60f, 1.5f, RangedSpells.NovaSpell);
		// ID 39 — Time Warp: zone slows enemies
		Register(39, "Time Warp",     35f, 0.50f, 1.0f, StatusSpells.TimeWarp);
		// ID 40 — Void Zone: large DoT zone
		Register(40, "Void Zone",     30f, 0.60f, 2.0f, RangedSpells.FireWall);
		
		GD.Print($"Registered {_spellRegistry.Count} spells globally.");
	}
	
	private static void Register(int id, string name, float cd, float castTime, float stunDuration, Action<CombatComponent>? effect)
	{
		_spellRegistry[id] = new SpellData(id, name, cd, castTime, stunDuration, effect);
	}
	
	// ==========================================
	// PRESETS (synergy-based builds)
	// ==========================================
	
	private static void RegisterPresets()
	{
		// "Frostbite" — Ralenti + Frost Lance synergy (ranged control)
		_presets["Frostbite"] = new Dictionary<SlotType, int>
		{
			[SlotType.Slot1] = 1,   // Frost Bolt (applies Ralenti)
			[SlotType.Slot2] = 10,  // Frost Lance (consumes Ralenti → stun)
			[SlotType.Slot3] = 18,  // Arcane Shot (basic poke)
			[SlotType.Slot4] = 7,   // Freezing Trap (zone control)
			[SlotType.SlotA] = 17,  // Wind Slash (melee backup)
			[SlotType.SlotE] = 22,  // Shockwave (pushback)
			[SlotType.Shift] = 25,  // Dash Roll (mobility)
			[SlotType.Elite] = 39,  // Time Warp (elite)
		};
		
		// "Shadowblade" — Marqué + Piercing Shot synergy (burst melee)
		_presets["Shadowblade"] = new Dictionary<SlotType, int>
		{
			[SlotType.Slot1] = 2,   // Shadow Mark (applies Marqué)
			[SlotType.Slot2] = 9,   // Piercing Shot (consumes Marqué → +100%)
			[SlotType.Slot3] = 17,  // Wind Slash (basic melee)
			[SlotType.Slot4] = 14,  // Shield Bash (Bouclier consume)
			[SlotType.SlotA] = 21,  // Chain Pull (gap closer)
			[SlotType.SlotE] = 24,  // Counter (defense)
			[SlotType.Shift] = 26,  // Blink (mobility)
			[SlotType.Elite] = 34,  // Annihilate (massive burst)
		};
		
		// "Inferno" — Brûlure + Combustion synergy (AoE fire mage)
		_presets["Inferno"] = new Dictionary<SlotType, int>
		{
			[SlotType.Slot1] = 3,   // Ignite (applies Brûlure)
			[SlotType.Slot2] = 11,  // Combustion (consumes Brûlure → AoE)
			[SlotType.Slot3] = 8,   // Corrupted Ground (zone)
			[SlotType.Slot4] = 23,  // Void Zone (DoT zone)
			[SlotType.SlotA] = 18,  // Arcane Shot (poke)
			[SlotType.SlotE] = 20,  // Force Push (pushback)
			[SlotType.Shift] = 25,  // Dash Roll (mobility)
			[SlotType.Elite] = 33,  // Meteor Rain (CONSUME Brûlure → empowered)
		};
		
		// "Tempest" — Electrifié + Overload synergy (control mage)
		_presets["Tempest"] = new Dictionary<SlotType, int>
		{
			[SlotType.Slot1] = 4,   // Static Shock (applies Electrifié)
			[SlotType.Slot2] = 12,  // Overload (consumes Electrifié → stun)
			[SlotType.Slot3] = 22,  // Shockwave (KB)
			[SlotType.Slot4] = 6,   // Radiant Shield (Bouclier)
			[SlotType.SlotA] = 20,  // Force Push (AoE push)
			[SlotType.SlotE] = 24,  // Counter (defense)
			[SlotType.Shift] = 26,  // Blink (mobility)
			[SlotType.Elite] = 35,  // Storm Surge (CONSUME Electrifié → longer)
		};
		
		// "Juggernaut" — Vulnérable + Execute synergy (tank/melee)
		_presets["Juggernaut"] = new Dictionary<SlotType, int>
		{
			[SlotType.Slot1] = 5,   // Sunder Armor (applies Vulnérable)
			[SlotType.Slot2] = 13,  // Execute (consumes Vulnérable → +150%)
			[SlotType.Slot3] = 19,  // Power Strike (heavy KB)
			[SlotType.Slot4] = 32,  // Charge (gap close)
			[SlotType.SlotA] = 14,  // Shield Bash (Bouclier consume)
			[SlotType.SlotE] = 28,  // Iron Wall (defense)
			[SlotType.Shift] = 25,  // Dash Roll (mobility)
			[SlotType.Elite] = 34,  // Annihilate (massive burst)
		};
		
		// "Starter" — legacy balanced build (kept for familiarity)
		_presets["Starter"] = new Dictionary<SlotType, int>
		{
			[SlotType.Slot1] = 18,  // Arcane Shot (basic ranged)
			[SlotType.Slot2] = 17,  // Wind Slash (basic melee)
			[SlotType.Slot3] = 25,  // Dash Roll (mobility)
			[SlotType.Slot4] = 6,   // Radiant Shield (shield)
			[SlotType.SlotA] = 26,  // Blink (teleport)
			[SlotType.SlotE] = 22,  // Shockwave (KB)
			[SlotType.Shift] = 24,  // Counter (parry)
			[SlotType.Elite] = 38,  // Nova (big AoE)
		};
	}
	
	/// <summary>
	/// Load a preset into the action bar.
	/// </summary>
	public void LoadPreset(string name)
	{
		if (!_presets.ContainsKey(name))
		{
			GD.PrintErr($"Preset '{name}' not found!");
			return;
		}
		
		_actionBar.Clear();
		foreach (var kvp in _presets[name])
		{
			if (_spellRegistry.ContainsKey(kvp.Value))
			{
				_actionBar[kvp.Key] = _spellRegistry[kvp.Value];
			}
		}
		
		GD.Print($"Preset loaded: {name}");
		foreach (var kvp in _actionBar)
		{
			GD.Print($"  {kvp.Key}: {kvp.Value.Name} (CD: {kvp.Value.CooldownMax}s, Stun: {kvp.Value.StunDuration}s)");
		}
	}
	
	/// <summary>
	/// Assign a specific spell to a slot.
	/// </summary>
	public void AssignSpellToSlot(SlotType slot, int spellID)
	{
		if (!_spellRegistry.ContainsKey(spellID))
		{
			GD.PrintErr($"Spell ID {spellID} not found in registry!");
			return;
		}
		_actionBar[slot] = _spellRegistry[spellID];
	}
	
	/// <summary>
	/// Clear a slot (remove spell).
	/// </summary>
	public void ClearSlot(SlotType slot)
	{
		_actionBar.Remove(slot);
	}
	
	// ==========================================
	// EXECUTE A SLOT
	// ==========================================
	
	/// <summary>
	/// Attempt to execute the spell in the given slot.
	/// Checks cooldown and triggers the effect.
	/// </summary>
	public bool TriggerSlot(SlotType slot, CombatComponent combat)
	{
		if (!_actionBar.ContainsKey(slot))
		{
			GD.Print($"Slot {slot} is empty!");
			return false;
		}
		
		var spell = _actionBar[slot];
		
		if (_cooldowns[slot] > 0f)
		{
			GD.Print($"{spell.Name} on cooldown ({_cooldowns[slot]:F1}s remaining)");
			return false;
		}
		
		spell.ActionEffect?.Invoke(combat);
		_cooldowns[slot] = spell.CooldownMax;
		
		return true;
	}
	
	// ==========================================
	// COOLDOWN UPDATE
	// ==========================================
	
	public override void _Process(double delta)
	{
		float dt = (float)delta;
		
		foreach (var slot in new List<SlotType>(_cooldowns.Keys))
		{
			if (_cooldowns[slot] > 0f)
			{
				_cooldowns[slot] -= dt;
				if (_cooldowns[slot] < 0f)
					_cooldowns[slot] = 0f;
			}
		}
	}
	
	// ==========================================
	// ACCESSORS
	// ==========================================
	
	public float GetCooldown(SlotType slot)
	{
		return _cooldowns.GetValueOrDefault(slot, 0f);
	}
	
	public SpellData? GetSpellInSlot(SlotType slot)
	{
		return _actionBar.GetValueOrDefault(slot);
	}
	
	public static SpellData? GetSpellByID(int id)
	{
		return _spellRegistry.GetValueOrDefault(id);
	}
	
	public static Dictionary<int, SpellData> GetAllSpells()
	{
		return new Dictionary<int, SpellData>(_spellRegistry);
	}
	
	public static Dictionary<string, Dictionary<SlotType, int>> GetAllPresets()
	{
		return new Dictionary<string, Dictionary<SlotType, int>>(_presets);
	}
}
