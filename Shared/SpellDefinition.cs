using System;

namespace SlopArena.Shared
{
	/// <summary>
	/// Combo role of a spell.
	/// </summary>
	public enum SpellRole : byte
	{
		Starter = 0,    // Opener: long cast/hard to land, huge hitstun (1.5-2.5s)
		Extender = 1,   // Bridge: fast, short hitstun (0.5-0.75s), keeps combo alive
		Finisher = 2,   // Punish: massive damage, zero stun, requires setup
		Setup = 3,      // Zone control: delayed AoE, forces movement
		Mobility = 4,   // Movement: instant cast, short stun (0.2s), repositioning
	}
	
	/// <summary>
	/// Visual shape / behavior of a spell effect.
	/// </summary>
	public enum SpellShape : byte
	{
		MeleeCone = 0,       // Cone AoE from caster (melee range)
		SlowProjectile = 1,  // Slow moving projectile (easy to dodge)
		FastProjectile = 2,  // Fast moving projectile (hard to dodge)
		Beam = 3,            // Hitscan / instant line
		Trap = 4,            // Ground trap, triggers on proximity
		DelayedAoE = 5,      // Delayed circular AoE at target position
	}
	
	/// <summary>
	/// Pure data definition of a spell.
	/// No Godot dependencies - usable by Server, Shared, and Client.
	/// </summary>
	public struct SpellDefinition
	{
		public ushort SpellID;
		public string Name;
		public string Description;
		public SpellRole Role;
		public SpellShape Shape;
		public float CastTime;       // Seconds before effect triggers
		public float StunDuration;   // Hitstun in seconds (0 = no stun)
		public float Damage;
		public float Cooldown;       // Cooldown in seconds
		
		// Projectile / AoE physics
		public float Speed;          // Projectile speed (units/sec), 0 = instant
		public float Range;          // Max travel distance / AoE range
		public float Radius;         // Projectile hit radius / AoE radius
		public float KnockbackForce;
		public float KnockbackUpward;
		
		public SpellDefinition(
			ushort spellId,
			string name,
			string description,
			SpellRole role,
			SpellShape shape,
			float castTime,
			float stunDuration,
			float damage,
			float cooldown,
			float speed = 0f,
			float range = 0f,
			float radius = 0f,
			float knockbackForce = 0f,
			float knockbackUpward = 0f)
		{
			SpellID = spellId;
			Name = name;
			Description = description;
			Role = role;
			Shape = shape;
			CastTime = castTime;
			StunDuration = stunDuration;
			Damage = damage;
			Cooldown = cooldown;
			Speed = speed;
			Range = range;
			Radius = radius;
			KnockbackForce = knockbackForce;
			KnockbackUpward = knockbackUpward;
		}
	}
	
	/// <summary>
	/// Static catalog of all spell definitions.
	/// Single source of truth for spell balance.
	/// </summary>
	public static class SpellCatalog
	{
		public static readonly SpellDefinition[] AllSpells = new SpellDefinition[]
		{
			// ==========================================
			// STARTERS (IDs 1-8) — Apply spells
			// ==========================================
			new(1,  "Frost Bolt",     "Chills the target, slowing movement (Slow 3s)", SpellRole.Starter, SpellShape.FastProjectile, 0.20f, 0.5f, 10, 3, speed:60, range:3000, radius:1.5f, knockbackForce:10, knockbackUpward:5),
			new(2,  "Shadow Mark",    "Marks the target with shadow energy (Marked 5s)", SpellRole.Starter, SpellShape.FastProjectile, 0.15f, 0.3f, 8, 2, speed:65, range:3000, radius:1.2f, knockbackForce:8, knockbackUpward:3),
			new(3,  "Ignite",         "Launches a fireball that burns the target (Burn 4s)", SpellRole.Starter, SpellShape.SlowProjectile, 0.25f, 0.5f, 12, 4, speed:25, range:3000, radius:1.5f, knockbackForce:10, knockbackUpward:5),
			new(4,  "Static Shock",   "Shocks the target with electricity (Electrified 3s)", SpellRole.Starter, SpellShape.Beam, 0.15f, 0.3f, 10, 5, speed:0, range:15, radius:1.5f, knockbackForce:5, knockbackUpward:2),
			new(5,  "Sunder Armor",   "Shatters armor in melee range (Vulnerable 4s)", SpellRole.Starter, SpellShape.MeleeCone, 0.20f, 0.8f, 8, 6, speed:0, range:5, radius:3, knockbackForce:10, knockbackUpward:5),
			new(6,  "Radiant Shield", "Radiant barrier that absorbs damage (Shield 4s)", SpellRole.Starter, SpellShape.Beam, 0.10f, 0.0f, 0, 8, speed:0, range:0, radius:0, knockbackForce:0, knockbackUpward:0),
			new(7,  "Freezing Trap",  "Ground trap that freezes on trigger (Slow 4s + bonus)", SpellRole.Setup, SpellShape.Trap, 0.50f, 1.0f, 10, 7, speed:0, range:5, radius:4, knockbackForce:5, knockbackUpward:2),
			new(8,  "Corrupted Ground","Corrupts the ground beneath targets (Vulnerable + Burn)", SpellRole.Setup, SpellShape.DelayedAoE, 0.40f, 1.5f, 5, 9, speed:0, range:6, radius:5, knockbackForce:3, knockbackUpward:1),
			
			// ==========================================
			// CONSUME SPELLS (IDs 9-16) — Finishers
			// ==========================================
			new(9,  "Piercing Shot",  "Armor-piercing shot, +100% dmg vs Marked", SpellRole.Finisher, SpellShape.FastProjectile, 0.30f, 0.5f, 25, 6, speed:60, range:3000, radius:1.5f, knockbackForce:15, knockbackUpward:5),
			new(10, "Frost Lance",    "Freezing lance, stuns if target is Slowed", SpellRole.Finisher, SpellShape.Beam, 0.25f, 0.75f, 25, 5, speed:0, range:12, radius:2f, knockbackForce:15, knockbackUpward:5),
			new(11, "Combustion",     "Melee explosion, consumes Burn for AoE blast", SpellRole.Finisher, SpellShape.MeleeCone, 0.20f, 0.5f, 20, 7, speed:0, range:5, radius:3, knockbackForce:15, knockbackUpward:5),
			new(12, "Overload",       "Electric discharge, stuns if Electrified", SpellRole.Finisher, SpellShape.DelayedAoE, 0.30f, 0.8f, 15, 8, speed:0, range:0, radius:5, knockbackForce:15, knockbackUpward:5),
			new(13, "Execute",        "Brutal execution, +150% dmg vs Vulnerable", SpellRole.Finisher, SpellShape.MeleeCone, 0.40f, 1.0f, 25, 10, speed:0, range:6, radius:4, knockbackForce:30, knockbackUpward:10),
			new(14, "Shield Bash",    "Shield strike, bonus effect if Shielded", SpellRole.Finisher, SpellShape.MeleeCone, 0.20f, 0.5f, 10, 9, speed:0, range:4, radius:2, knockbackForce:40, knockbackUpward:5),
			new(15, "Feedback Pulse", "Shockwave, bonus dmg per status consumed", SpellRole.Finisher, SpellShape.DelayedAoE, 0.30f, 0.5f, 10, 7, speed:0, range:0, radius:5, knockbackForce:5, knockbackUpward:2),
			new(16, "Dark Harvest",   "Harvests life force, heals if status consumed", SpellRole.Finisher, SpellShape.FastProjectile, 0.35f, 0.3f, 20, 8, speed:40, range:3000, radius:1.5f, knockbackForce:10, knockbackUpward:3),
			
			// ==========================================
			// CONTROL SPELLS (IDs 17-24) — Setup
			// ==========================================
			new(17, "Wind Slash",    "Basic wind slash in melee range", SpellRole.Extender, SpellShape.MeleeCone, 0.10f, 0.3f, 18, 3, speed:0, range:5, radius:3, knockbackForce:10, knockbackUpward:3),
			new(18, "Arcane Shot",   "Quick arcane projectile", SpellRole.Extender, SpellShape.FastProjectile, 0.10f, 0.2f, 15, 2, speed:55, range:3000, radius:1.2f, knockbackForce:8, knockbackUpward:3),
			new(19, "Power Strike",  "Heavy strike with strong knockback", SpellRole.Finisher, SpellShape.MeleeCone, 0.40f, 1.5f, 20, 6, speed:0, range:7, radius:4, knockbackForce:30, knockbackUpward:10),
			new(20, "Force Push",    "Pushes all enemies away from self", SpellRole.Setup, SpellShape.DelayedAoE, 0.20f, 0.5f, 5, 8, speed:0, range:0, radius:5, knockbackForce:40, knockbackUpward:15),
			new(21, "Chain Pull",    "Chain that pulls an enemy toward you", SpellRole.Setup, SpellShape.SlowProjectile, 0.30f, 0.5f, 10, 8, speed:60, range:3000, radius:1.5f, knockbackForce:-40, knockbackUpward:5),
			new(22, "Shockwave",     "Horizontal shockwave in a wide cone", SpellRole.Setup, SpellShape.MeleeCone, 0.35f, 1.2f, 12, 7, speed:0, range:8, radius:5, knockbackForce:25, knockbackUpward:10),
			new(23, "Void Zone",     "Zone of void energy dealing damage over time", SpellRole.Setup, SpellShape.DelayedAoE, 0.40f, 1.0f, 8, 10, speed:0, range:5, radius:4, knockbackForce:5, knockbackUpward:2),
			new(24, "Counter",       "Parries incoming attacks, knocks back attacker", SpellRole.Setup, SpellShape.Beam, 0.10f, 0.5f, 0, 10, speed:0, range:0, radius:0, knockbackForce:0, knockbackUpward:0),
			
			// ==========================================
			// UTILITY SPELLS (IDs 25-32) — Mobility
			// ==========================================
			new(25, "Dash Roll",     "Quick dodge roll with invincibility frames", SpellRole.Mobility, SpellShape.MeleeCone, 0.05f, 0.0f, 0, 5, speed:0, range:0, radius:0, knockbackForce:0, knockbackUpward:0),
			new(26, "Blink",         "Teleports you forward instantly", SpellRole.Mobility, SpellShape.FastProjectile, 0.05f, 0.0f, 0, 8, speed:0, range:10, radius:0, knockbackForce:0, knockbackUpward:0),
			new(27, "Phase Shift",   "Intangibility shield that blocks damage", SpellRole.Mobility, SpellShape.Beam, 0.10f, 0.0f, 0, 12, speed:0, range:0, radius:0, knockbackForce:0, knockbackUpward:0),
			new(28, "Iron Wall",     "Creates a defensive wall around you", SpellRole.Setup, SpellShape.DelayedAoE, 0.20f, 0.3f, 0, 10, speed:0, range:0, radius:4, knockbackForce:0, knockbackUpward:0),
			new(29, "Purify",        "Removes all negative status effects from self", SpellRole.Mobility, SpellShape.Beam, 0.15f, 0.0f, 0, 12, speed:0, range:0, radius:0, knockbackForce:0, knockbackUpward:0),
			new(30, "Blood Pact",    "Sacrifices HP to gain a damage buff", SpellRole.Mobility, SpellShape.FastProjectile, 0.20f, 0.5f, 0, 14, speed:0, range:0, radius:0, knockbackForce:0, knockbackUpward:0),
			new(31, "Magic Barrier", "Protective magic barrier around self", SpellRole.Mobility, SpellShape.Beam, 0.15f, 0.0f, 0, 9, speed:0, range:0, radius:0, knockbackForce:0, knockbackUpward:0),
			new(32, "Charge",        "Charges forward knocking back enemies", SpellRole.Mobility, SpellShape.MeleeCone, 0.10f, 0.5f, 10, 8, speed:80, range:0, radius:1.5f, knockbackForce:50, knockbackUpward:10),
			
			// ==========================================
			// ELITE SPELLS (IDs 33-40) — Ultimates
			// ==========================================
			new(33, "Meteor Rain",   "Summons 5 meteors, +50% dmg if Burn consumed", SpellRole.Finisher, SpellShape.DelayedAoE, 1.00f, 2.0f, 40, 35, speed:0, range:12, radius:5, knockbackForce:30, knockbackUpward:15),
			new(34, "Annihilate",    "Massive cone that annihilates, +100% if Vulnerable", SpellRole.Finisher, SpellShape.MeleeCone, 1.50f, 3.0f, 100, 40, speed:0, range:8, radius:6, knockbackForce:60, knockbackUpward:20),
			new(35, "Storm Surge",   "Haste buff, longer duration if Electrified", SpellRole.Starter, SpellShape.Beam, 0.30f, 0.0f, 0, 32, speed:0, range:0, radius:0, knockbackForce:0, knockbackUpward:0),
			new(36, "Dark Pact",     "Massive burst, free cast if Shielded", SpellRole.Finisher, SpellShape.SlowProjectile, 0.50f, 1.0f, 80, 30, speed:30, range:3000, radius:2f, knockbackForce:20, knockbackUpward:8),
			new(37, "Bladestorm",    "Devastating spin attack, 5 hits over 1.5s", SpellRole.Finisher, SpellShape.MeleeCone, 0.40f, 2.0f, 18, 25, speed:0, range:4, radius:4, knockbackForce:8, knockbackUpward:3),
			new(38, "Nova",          "Massive energy explosion around self", SpellRole.Finisher, SpellShape.DelayedAoE, 0.60f, 1.5f, 50, 28, speed:0, range:0, radius:7, knockbackForce:40, knockbackUpward:15),
			new(39, "Time Warp",     "Temporal slow zone, applies Slow to enemies", SpellRole.Setup, SpellShape.DelayedAoE, 0.50f, 1.0f, 5, 35, speed:0, range:10, radius:6, knockbackForce:0, knockbackUpward:0),
			new(40, "Void Zone",     "Massive void zone that persists over time", SpellRole.Setup, SpellShape.DelayedAoE, 0.60f, 2.0f, 15, 30, speed:0, range:6, radius:6, knockbackForce:5, knockbackUpward:2),
		};
		
		public static SpellDefinition GetSpell(ushort id)
		{
			foreach (var spell in AllSpells)
			{
				if (spell.SpellID == id)
					return spell;
			}
			return AllSpells[0]; // Default to Frost Bolt
		}
	}
}