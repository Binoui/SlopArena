// Copyright SlopArena Contributors. MIT License.

#include "CharacterRegistry.h"

// =====================================================================
// BRAWLER — Melee all-rounder (Hercules / Arthur archetype)
// Playstyle: In your face, grabs, shields, relentless pressure
// =====================================================================
USlopArenaCharacterDefinition* FCharacterRegistry::CreateBrawler()
{
	FLightAttackData Light;
	Light.Damage = 8.0f;
	Light.KnockbackForce = 12.0f;
	Light.KnockbackUpward = 3.0f;
	Light.Range = 280.0f;
	Light.Radius = 130.0f;
	Light.ChainWindow = 0.4f;
	Light.FinisherMultiplier = 1.5f;

	FAbilityData Heavy;
	Heavy.Name = "Haymaker";
	Heavy.Description = NSLOCTEXT("SlopArena", "BrawlerHeavy", "A devastating lunging punch that knocks enemies back hard.");
	Heavy.Shape = EAbilityShape::MeleeCone;
	Heavy.Cooldown = 2.0f;
	Heavy.Damage = 20.0f;
	Heavy.KnockbackForce = 40.0f;
	Heavy.KnockbackUpward = 8.0f;
	Heavy.Range = 350.0f;
	Heavy.Radius = 150.0f;
	Heavy.CastTime = 0.3f;

	FAbilityData Q; // Shield Bash
	Q.Name = "Shield Bash";
	Q.Description = NSLOCTEXT("SlopArena", "BrawlerQ", "Raise your shield, then bash forward. Blocks incoming attacks during startup. Stuns on hit.");
	Q.Shape = EAbilityShape::MeleeCone;
	Q.Cooldown = 8.0f;
	Q.Damage = 15.0f;
	Q.KnockbackForce = 25.0f;
	Q.KnockbackUpward = 10.0f;
	Q.Range = 300.0f;
	Q.Radius = 140.0f;
	Q.CastTime = 0.15f;

	FAbilityData E; // Grapple
	E.Name = "Grapple";
	E.Description = NSLOCTEXT("SlopArena", "BrawlerE", "Throw a chain that pulls an enemy toward you. Stuns briefly on arrival.");
	E.Shape = EAbilityShape::Projectile;
	E.Cooldown = 10.0f;
	E.Damage = 10.0f;
	E.KnockbackForce = -50.0f; // Negative = pull toward caster
	E.KnockbackUpward = 2.0f;
	E.Range = 800.0f;
	E.Radius = 80.0f;
	E.ProjectileSpeed = 3000.0f;

	FAbilityData R; // War Cry (self buff)
	R.Name = "War Cry";
	R.Description = NSLOCTEXT("SlopArena", "BrawlerR", "Let out a mighty roar. Gain a damage buff and slow immunity for 4 seconds.");
	R.Shape = EAbilityShape::SelfBuff;
	R.Cooldown = 14.0f;
	R.Damage = 0.0f;
	R.Range = 0.0f;
	R.Radius = 0.0f;

	FAbilityData Ult; // Colossus Slam
	Ult.Name = "Colossus Slam";
	Ult.Description = NSLOCTEXT("SlopArena", "BrawlerUlt", "Leap into the air and slam down, creating a massive shockwave that launches all nearby enemies.");
	Ult.Shape = EAbilityShape::CircleAoE;
	Ult.Cooldown = 60.0f;
	Ult.Damage = 40.0f;
	Ult.KnockbackForce = 60.0f;
	Ult.KnockbackUpward = 25.0f;
	Ult.Range = 500.0f;
	Ult.Radius = 400.0f;
	Ult.CastTime = 0.5f;

	return MakeDefinition(
		NSLOCTEXT("SlopArena", "BrawlerName", "Colossus"),
		NSLOCTEXT("SlopArena", "BrawlerDesc", "An unstoppable melee force. Grapples enemies, blocks attacks, and slams the arena."),
		120.0f,  // HP
		550.0f,  // Speed
		550.0f,  // Jump
		1.3f,    // Weight (knockback resistant)
		Light, Heavy, Q, E, R, Ult
	);
}

// =====================================================================
// RANGER — Ranged poke / zone control (Sol / Artemis archetype)
// Playstyle: Keep distance, poke with projectiles, zone with traps
// =====================================================================
USlopArenaCharacterDefinition* FCharacterRegistry::CreateRanger()
{
	FLightAttackData Light;
	Light.Damage = 6.0f;
	Light.KnockbackForce = 10.0f;
	Light.KnockbackUpward = 2.0f;
	Light.Range = 300.0f;
	Light.Radius = 100.0f;
	Light.ChainWindow = 0.35f;
	Light.FinisherMultiplier = 1.4f;

	FAbilityData Heavy;
	Heavy.Name = "Power Shot";
	Heavy.Description = NSLOCTEXT("SlopArena", "RangerHeavy", "A charged shot that pierces through enemies. Longer range and higher knockback.");
	Heavy.Shape = EAbilityShape::Projectile;
	Heavy.Cooldown = 1.5f;
	Heavy.Damage = 18.0f;
	Heavy.KnockbackForce = 35.0f;
	Heavy.KnockbackUpward = 5.0f;
	Heavy.Range = 2000.0f;
	Heavy.Radius = 80.0f;
	Heavy.CastTime = 0.4f;
	Heavy.ProjectileSpeed = 4000.0f;

	FAbilityData Q; // Arrow Volley
	Q.Name = "Arrow Volley";
	Q.Description = NSLOCTEXT("SlopArena", "RangerQ", "Fire a volley of 3 arrows in a spread pattern.");
	Q.Shape = EAbilityShape::Projectile;
	Q.Cooldown = 6.0f;
	Q.Damage = 10.0f;
	Q.KnockbackForce = 15.0f;
	Q.KnockbackUpward = 3.0f;
	Q.Range = 1800.0f;
	Q.Radius = 60.0f;
	Q.ProjectileSpeed = 3500.0f;

	FAbilityData E; // Trap
	E.Name = "Snare Trap";
	E.Description = NSLOCTEXT("SlopArena", "RangerE", "Place a trap on the ground. The first enemy to step on it is rooted in place for 2 seconds.");
	E.Shape = EAbilityShape::CircleAoE;
	E.Cooldown = 12.0f;
	E.Damage = 5.0f;
	E.KnockbackForce = 0.0f;
	E.Range = 300.0f;
	E.Radius = 150.0f;

	FAbilityData R; // Evade (mobility)
	R.Name = "Evade";
	R.Description = NSLOCTEXT("SlopArena", "RangerR", "Quickly roll backward, leaving a decoy that explodes after a brief delay.");
	R.Shape = EAbilityShape::SelfBuff;
	R.Cooldown = 10.0f;
	R.Damage = 12.0f;
	R.Radius = 200.0f;

	FAbilityData Ult; // Rain of Arrows
	Ult.Name = "Rain of Arrows";
	Ult.Description = NSLOCTEXT("SlopArena", "RangerUlt", "Launch a signal arrow. After a delay, a massive volley rains down on the targeted area.");
	Ult.Shape = EAbilityShape::CircleAoE;
	Ult.Cooldown = 55.0f;
	Ult.Damage = 35.0f;
	Ult.KnockbackForce = 40.0f;
	Ult.KnockbackUpward = 15.0f;
	Ult.Range = 1500.0f;
	Ult.Radius = 350.0f;
	Ult.CastTime = 0.3f;

	return MakeDefinition(
		NSLOCTEXT("SlopArena", "RangerName", "Marksman"),
		NSLOCTEXT("SlopArena", "RangerDesc", "A precise ranged fighter. Controls space with traps and devastating arrow volleys."),
		80.0f,   // HP
		650.0f,  // Speed
		620.0f,  // Jump
		0.8f,    // Weight (light)
		Light, Heavy, Q, E, R, Ult
	);
}

// =====================================================================
// ASSASSIN — Fast melee burst (Izanami / Loki archetype)
// Playstyle: Hit-and-run, stealth, backstab, high mobility
// =====================================================================
USlopArenaCharacterDefinition* FCharacterRegistry::CreateAssassin()
{
	FLightAttackData Light;
	Light.Damage = 7.0f;
	Light.KnockbackForce = 8.0f;
	Light.Range = 240.0f;
	Light.Radius = 110.0f;
	Light.ChainWindow = 0.3f;  // Tight chain window — high skill
	Light.FinisherMultiplier = 2.0f; // Big finisher reward

	FAbilityData Heavy;
	Heavy.Name = "Backstab";
	Heavy.Description = NSLOCTEXT("SlopArena", "AssassinHeavy", "A precise strike. Deals 2x damage if hitting an enemy from behind.");
	Heavy.Shape = EAbilityShape::MeleeCone;
	Heavy.Cooldown = 3.0f;
	Heavy.Damage = 15.0f;
	Heavy.KnockbackForce = 20.0f;
	Heavy.KnockbackUpward = 5.0f;
	Heavy.Range = 260.0f;
	Heavy.Radius = 120.0f;
	Heavy.CastTime = 0.1f;

	FAbilityData Q; // Shadow Step (teleport)
	Q.Name = "Shadow Step";
	Q.Description = NSLOCTEXT("SlopArena", "AssassinQ", "Teleport a short distance in the direction you're moving. Leave a shadow behind.");
	Q.Shape = EAbilityShape::SelfBuff;
	Q.Cooldown = 6.0f;
	Q.Range = 500.0f;

	FAbilityData E; // Poison Blade
	E.Name = "Poison Blade";
	E.Description = NSLOCTEXT("SlopArena", "AssassinE", "Enchant your weapon with poison. Your next light attack deals bonus damage over time.");
	E.Shape = EAbilityShape::SelfBuff;
	E.Cooldown = 8.0f;
	E.Damage = 20.0f;

	FAbilityData R; // Smoke Bomb
	R.Name = "Smoke Bomb";
	R.Description = NSLOCTEXT("SlopArena", "AssassinR", "Throw a smoke bomb at your feet. Become invisible and gain movement speed for 3 seconds. Attacking breaks stealth.");
	R.Shape = EAbilityShape::SelfBuff;
	R.Cooldown = 14.0f;

	FAbilityData Ult; // Death Mark
	Ult.Name = "Death Mark";
	Ult.Description = NSLOCTEXT("SlopArena", "AssassinUlt", "Mark a target enemy. After 2 seconds, the mark detonates for massive damage. If the target dies, reset all ability cooldowns.");
	Ult.Shape = EAbilityShape::Projectile;
	Ult.Cooldown = 50.0f;
	Ult.Damage = 60.0f;
	Ult.Range = 1000.0f;
	Ult.Radius = 80.0f;
	Ult.ProjectileSpeed = 2000.0f;

	return MakeDefinition(
		NSLOCTEXT("SlopArena", "AssassinName", "Wraith"),
		NSLOCTEXT("SlopArena", "AssassinDesc", "A deadly shadow. Teleports behind enemies, poisons, and executes marked targets."),
		70.0f,   // HP
		700.0f,  // Speed (fastest)
		650.0f,  // Jump (highest)
		0.7f,    // Weight (lightest)
		Light, Heavy, Q, E, R, Ult
	);
}

// =====================================================================
// TANK — Heavy initiator (Thor / Herc tank archetype)
// Playstyle: Slow, disruptive, absorb damage, CC the backline
// =====================================================================
USlopArenaCharacterDefinition* FCharacterRegistry::CreateTank()
{
	FLightAttackData Light;
	Light.Damage = 6.0f;
	Light.KnockbackForce = 15.0f; // High pushback
	Light.KnockbackUpward = 2.0f;
	Light.Range = 300.0f;
	Light.Radius = 150.0f;
	Light.ChainWindow = 0.5f; // Generous chains
	Light.FinisherMultiplier = 1.3f;

	FAbilityData Heavy;
	Heavy.Name = "Hammer Slam";
	Heavy.Description = NSLOCTEXT("SlopArena", "TankHeavy", "Slam your hammer down in front of you, creating a short-range shockwave.");
	Heavy.Shape = EAbilityShape::MeleeCone;
	Heavy.Cooldown = 2.5f;
	Heavy.Damage = 14.0f;
	Heavy.KnockbackForce = 50.0f;
	Heavy.KnockbackUpward = 12.0f;
	Heavy.Range = 380.0f;
	Heavy.Radius = 200.0f;
	Heavy.CastTime = 0.4f;

	FAbilityData Q; // Charge
	Q.Name = "Charge";
	Q.Description = NSLOCTEXT("SlopArena", "TankQ", "Rush forward, grabbing the first enemy you hit. Carry them and slam them into a wall for bonus damage.");
	Q.Shape = EAbilityShape::MeleeCone;
	Q.Cooldown = 10.0f;
	Q.Damage = 20.0f;
	Q.KnockbackForce = 70.0f;
	Q.KnockbackUpward = 5.0f;
	Q.Range = 800.0f;
	Q.Radius = 160.0f;

	FAbilityData E; // Fortify (shield)
	E.Name = "Fortify";
	E.Description = NSLOCTEXT("SlopArena", "TankE", "Become immune to knockback and gain a shield that absorbs damage for 3 seconds.");
	E.Shape = EAbilityShape::SelfBuff;
	E.Cooldown = 12.0f;

	FAbilityData R; // Ground Pound (AoE slow)
	R.Name = "Ground Pound";
	R.Description = NSLOCTEXT("SlopArena", "TankR", "Stomp the ground, creating a shockwave that slows and damages all enemies around you.");
	R.Shape = EAbilityShape::CircleAoE;
	R.Cooldown = 8.0f;
	R.Damage = 12.0f;
	R.KnockbackForce = 20.0f;
	R.Radius = 350.0f;

	FAbilityData Ult; // Arena
	Ult.Name = "Arena";
	Ult.Description = NSLOCTEXT("SlopArena", "TankUlt", "Create a ring of stone pillars around you, trapping enemies inside. The walls last 6 seconds.");
	Ult.Shape = EAbilityShape::CircleAoE;
	Ult.Cooldown = 65.0f;
	Ult.Damage = 25.0f;
	Ult.KnockbackForce = 30.0f;
	Ult.Radius = 500.0f;
	Ult.CastTime = 0.3f;

	return MakeDefinition(
		NSLOCTEXT("SlopArena", "TankName", "Titan"),
		NSLOCTEXT("SlopArena", "TankDesc", "An immovable fortress. Charges through enemies, shields allies, and traps foes in the arena."),
		150.0f,  // HP (tankiest)
		480.0f,  // Speed (slowest)
		500.0f,  // Jump (lowest)
		1.6f,    // Weight
		Light, Heavy, Q, E, R, Ult
	);
}

// =====================================================================
// SOL — Sun mage, glass cannon zoning (DKO Sol faithful adaptation)
// Playstyle: Zone with orbs, poke with beams, all-in with ultimate
//
// Inspired by Sol from Divine Knockout:
//   Heavy: Solar Orb — slow projectile that explodes
//   Q: Solar Flare — wide fire cone
//   E: Sunbeam — hitscan beam, her signature move
//   R: Supernova — AoE knockback escape
//   Ult: Sunburst — fly up, come crashing down
// =====================================================================
USlopArenaCharacterDefinition* FCharacterRegistry::CreateSol()
{
	FLightAttackData Light;
	Light.Damage = 7.0f;
	Light.KnockbackForce = 10.0f;
	Light.KnockbackUpward = 3.0f;
	Light.Range = 260.0f;
	Light.Radius = 120.0f;
	Light.ChainWindow = 0.35f;
	Light.FinisherMultiplier = 1.5f;

	// HEAVY: Solar Orb — slow projectile that explodes on contact
	FAbilityData Heavy;
	Heavy.Name = "Solar Orb";
	Heavy.Description = NSLOCTEXT("SlopArena", "SolHeavy", "Launch a slow-moving orb of condensed sunlight. Explodes on contact, dealing AoE damage.");
	Heavy.Shape = EAbilityShape::Projectile;
	Heavy.Cooldown = 3.0f;
	Heavy.Damage = 18.0f;
	Heavy.KnockbackForce = 30.0f;
	Heavy.KnockbackUpward = 5.0f;
	Heavy.Range = 1200.0f;
	Heavy.Radius = 150.0f;
	Heavy.ProjectileSpeed = 1500.0f; // Slow — easy to dodge, high reward

	// Q: Solar Flare — wide cone, Sol's bread and butter
	FAbilityData Q;
	Q.Name = "Solar Flare";
	Q.Description = NSLOCTEXT("SlopArena", "SolQ", "Unleash a burst of solar energy in a wide cone. Sets enemies on fire, dealing damage over time.");
	Q.Shape = EAbilityShape::MeleeCone;
	Q.Cooldown = 7.0f;
	Q.Damage = 14.0f;
	Q.KnockbackForce = 20.0f;
	Q.KnockbackUpward = 8.0f;
	Q.Range = 400.0f;
	Q.Radius = 250.0f; // Wide cone
	Q.CastTime = 0.15f;

	// E: Sunbeam — hitscan beam, her signature poke
	FAbilityData E;
	E.Name = "Sunbeam";
	E.Description = NSLOCTEXT("SlopArena", "SolE", "Channel a beam of pure sunlight. Deals rapid damage to the first enemy hit. Longer range than any other ability.");
	E.Shape = EAbilityShape::Beam;
	E.Cooldown = 5.0f;
	E.Damage = 12.0f;
	E.KnockbackForce = 15.0f;
	E.KnockbackUpward = 3.0f;
	E.Range = 2000.0f;
	E.Radius = 80.0f;
	E.CastTime = 0.2f;

	// R: Supernova — AoE burst around self, escape tool
	FAbilityData R;
	R.Name = "Supernova";
	R.Description = NSLOCTEXT("SlopArena", "SolR", "Detonate your solar energy, pushing all nearby enemies away. Gives you a brief speed boost.");
	R.Shape = EAbilityShape::CircleAoE;
	R.Cooldown = 10.0f;
	R.Damage = 10.0f;
	R.KnockbackForce = 45.0f;
	R.KnockbackUpward = 15.0f;
	R.Radius = 350.0f;
	R.CastTime = 0.1f;

	// ULT: Sunburst — fly up, then crash down
	FAbilityData Ult;
	Ult.Name = "Sunburst";
	Ult.Description = NSLOCTEXT("SlopArena", "SolUlt", "Ascend into the sky and transform into a blazing sun. After a moment, crash down on the target area, dealing massive AoE damage and knockback.");
	Ult.Shape = EAbilityShape::CircleAoE;
	Ult.Cooldown = 55.0f;
	Ult.Damage = 45.0f;
	Ult.KnockbackForce = 55.0f;
	Ult.KnockbackUpward = 25.0f;
	Ult.Range = 800.0f; // Target selection range
	Ult.Radius = 400.0f;
	Ult.CastTime = 0.6f; // Wind-up before ascending

	return MakeDefinition(
		NSLOCTEXT("SlopArena", "SolName", "Sol"),
		NSLOCTEXT("SlopArena", "SolDesc", "The blazing sun incarnate. Controls space with orbs and beams, then burns everything in a supernova."),
		75.0f,   // HP — glass cannon
		625.0f,  // Speed — mobile
		600.0f,  // Jump — floaty
		0.75f,   // Weight — light, launched easily
		Light, Heavy, Q, E, R, Ult
	);
}

// =====================================================================
// Factory
// =====================================================================

TArray<USlopArenaCharacterDefinition*> FCharacterRegistry::GetAll()
{
	TArray<USlopArenaCharacterDefinition*> Characters;
	Characters.Add(CreateBrawler());
	Characters.Add(CreateRanger());
	Characters.Add(CreateAssassin());
	Characters.Add(CreateTank());
	Characters.Add(CreateSol());
	return Characters;
}

USlopArenaCharacterDefinition* FCharacterRegistry::MakeDefinition(
	const FText& Name,
	const FText& Desc,
	float HP,
	float Speed,
	float Jump,
	float Weight,
	FLightAttackData Light,
	FAbilityData Heavy,
	FAbilityData Ability1,
	FAbilityData Ability2,
	FAbilityData Ability3,
	FAbilityData Ultimate)
{
	// Note: In a real Unreal build, this would construct a UObject via NewObject<USlopArenaCharacterDefinition>()
	// For now, we just populate the struct data. The actual object creation happens at runtime.
	// This function is a placeholder — in the editor, these will be DataAssets created from the Content Browser.

	USlopArenaCharacterDefinition* Def = NewObject<USlopArenaCharacterDefinition>();
	Def->CharacterName = Name;
	Def->Description = Desc;
	Def->MaxHP = HP;
	Def->WalkSpeed = Speed;
	Def->JumpForce = Jump;
	Def->Weight = Weight;
	Def->LightAttack = Light;
	Def->HeavyAttack = Heavy;
	Def->Ability1 = Ability1;
	Def->Ability2 = Ability2;
	Def->Ability3 = Ability3;
	Def->Ultimate = Ultimate;
	return Def;
}
