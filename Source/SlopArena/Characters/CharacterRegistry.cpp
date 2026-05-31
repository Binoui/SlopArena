// Copyright SlopArena Contributors. MIT License.

#include "CharacterRegistry.h"
#include "Engine/ObjectLibrary.h"
#include "Engine/DataAsset.h"

// =====================================================================
// Compact data table — one entry per character.
// Keeps all stats in a flat struct so the roster is readable at a glance.
// When the editor is available, replace this with Blueprint DataAssets
// and they'll load via LoadFromPath() instead.
// =====================================================================

namespace
{

/** Stat block for one ability. */
struct FAbilityEntry
{
	FName Name;
	int32 Damage = 0;
	float Cooldown = 0.0f;
	float KnockbackForce = 0.0f;
	float KnockbackUpward = 0.0f;
	float Range = 0.0f;
	float Radius = 0.0f;
	float CastTime = 0.0f;
	float ProjectileSpeed = 0.0f;
	EAbilityShape Shape = EAbilityShape::SelfBuff;
};

/** Light-attack chain data. */
struct FLightEntry
{
	int32 Damage = 8;
	float KnockbackForce = 10.0f;
	float KnockbackUpward = 3.0f;
	float Range = 250.0f;
	float Radius = 120.0f;
	float ChainWindow = 0.4f;
	float FinisherMultiplier = 1.5f;
};

/** Complete kit for one playable character. */
struct FCharacterKit
{
	FName Name;
	FString Description;

	float MaxHP = 100.0f;
	float WalkSpeed = 600.0f;
	float JumpForce = 600.0f;
	float Weight = 1.0f;

	FLightEntry Light;
	FAbilityEntry Heavy;
	FAbilityEntry Ability1;
	FAbilityEntry Ability2;
	FAbilityEntry Ability3;
	FAbilityEntry Ultimate;
};

// =====================================================================
// Roster data
// =====================================================================

static const FCharacterKit CharacterTable[] =
{
	// ---- Colossus (Brawler) --------------------------------------------
	{
		.Name = "Colossus",
		.Description = "An unstoppable melee force. Grapples enemies, blocks attacks, and slams the arena.",
		.MaxHP = 120.0f,
		.WalkSpeed = 550.0f,
		.JumpForce = 550.0f,
		.Weight = 1.3f,
		.Light = { 8, 12.0f, 3.0f, 280.0f, 130.0f, 0.4f, 1.5f },
		.Heavy = { "Haymaker", 20, 2.0f, 40.0f, 8.0f, 350.0f, 150.0f, 0.3f },
		.Ability1 = { "Shield Bash", 15, 8.0f, 25.0f, 10.0f, 300.0f, 140.0f, 0.15f },
		.Ability2 = { "Grapple", 10, 10.0f, -50.0f, 2.0f, 800.0f, 80.0f, 0.0f, 3000.0f, EAbilityShape::Projectile },
		.Ability3 = { "War Cry", 0, 14.0f },
		.Ultimate = { "Colossus Slam", 40, 60.0f, 60.0f, 25.0f, 500.0f, 400.0f, 0.5f, 0.0f, EAbilityShape::CircleAoE },
	},
	// ---- Marksman (Ranger) ---------------------------------------------
	{
		.Name = "Marksman",
		.Description = "A precise ranged fighter. Controls space with traps and devastating arrow volleys.",
		.MaxHP = 80.0f,
		.WalkSpeed = 650.0f,
		.JumpForce = 620.0f,
		.Weight = 0.8f,
		.Light = { 6, 10.0f, 2.0f, 300.0f, 100.0f, 0.35f, 1.4f },
		.Heavy = { "Power Shot", 18, 1.5f, 35.0f, 5.0f, 2000.0f, 80.0f, 0.4f, 4000.0f, EAbilityShape::Projectile },
		.Ability1 = { "Arrow Volley", 10, 6.0f, 15.0f, 3.0f, 1800.0f, 60.0f, 0.0f, 3500.0f, EAbilityShape::Projectile },
		.Ability2 = { "Snare Trap", 5, 12.0f, 0.0f, 0.0f, 300.0f, 150.0f, 0.0f, 0.0f, EAbilityShape::CircleAoE },
		.Ability3 = { "Evade", 12, 10.0f, 0.0f, 0.0f, 0.0f, 200.0f },
		.Ultimate = { "Rain of Arrows", 35, 55.0f, 40.0f, 15.0f, 1500.0f, 350.0f, 0.3f, 0.0f, EAbilityShape::CircleAoE },
	},
	// ---- Wraith (Assassin) ---------------------------------------------
	{
		.Name = "Wraith",
		.Description = "A deadly shadow. Teleports behind enemies, poisons, and executes marked targets.",
		.MaxHP = 70.0f,
		.WalkSpeed = 700.0f,
		.JumpForce = 650.0f,
		.Weight = 0.7f,
		.Light = { 7, 8.0f, 0.0f, 240.0f, 110.0f, 0.3f, 2.0f },
		.Heavy = { "Backstab", 15, 3.0f, 20.0f, 5.0f, 260.0f, 120.0f, 0.1f },
		.Ability1 = { "Shadow Step", 0, 6.0f },
		.Ability2 = { "Poison Blade", 20, 8.0f },
		.Ability3 = { "Smoke Bomb", 0, 14.0f },
		.Ultimate = { "Death Mark", 60, 50.0f, 0.0f, 0.0f, 1000.0f, 80.0f, 0.0f, 2000.0f, EAbilityShape::Projectile },
	},
	// ---- Titan (Tank) --------------------------------------------------
	{
		.Name = "Titan",
		.Description = "An immovable fortress. Charges through enemies, shields allies, and traps foes in the arena.",
		.MaxHP = 150.0f,
		.WalkSpeed = 480.0f,
		.JumpForce = 500.0f,
		.Weight = 1.6f,
		.Light = { 6, 15.0f, 2.0f, 300.0f, 150.0f, 0.5f, 1.3f },
		.Heavy = { "Hammer Slam", 14, 2.5f, 50.0f, 12.0f, 380.0f, 200.0f, 0.4f },
		.Ability1 = { "Charge", 20, 10.0f, 70.0f, 5.0f, 800.0f, 160.0f, 0.0f },
		.Ability2 = { "Fortify", 0, 12.0f },
		.Ability3 = { "Ground Pound", 12, 8.0f, 20.0f, 0.0f, 0.0f, 350.0f, 0.0f, 0.0f, EAbilityShape::CircleAoE },
		.Ultimate = { "Arena", 25, 65.0f, 30.0f, 0.0f, 0.0f, 500.0f, 0.3f, 0.0f, EAbilityShape::CircleAoE },
	},
	// ---- Sol (Sun Mage) ------------------------------------------------
	{
		.Name = "Sol",
		.Description = "The blazing sun incarnate. Controls space with orbs and beams, then burns everything in a supernova.",
		.MaxHP = 75.0f,
		.WalkSpeed = 625.0f,
		.JumpForce = 600.0f,
		.Weight = 0.75f,
		.Light = { 7, 10.0f, 3.0f, 260.0f, 120.0f, 0.35f, 1.5f },
		.Heavy = { "Solar Orb", 18, 3.0f, 30.0f, 5.0f, 1200.0f, 150.0f, 0.0f, 1500.0f, EAbilityShape::Projectile },
		.Ability1 = { "Solar Flare", 14, 7.0f, 20.0f, 8.0f, 400.0f, 250.0f, 0.15f },
		.Ability2 = { "Sunbeam", 12, 5.0f, 15.0f, 3.0f, 2000.0f, 80.0f, 0.2f, 0.0f, EAbilityShape::Beam },
		.Ability3 = { "Supernova", 10, 10.0f, 45.0f, 15.0f, 0.0f, 350.0f, 0.1f, 0.0f, EAbilityShape::CircleAoE },
		.Ultimate = { "Sunburst", 45, 55.0f, 55.0f, 25.0f, 800.0f, 400.0f, 0.6f, 0.0f, EAbilityShape::CircleAoE },
	},
};

static constexpr int32 CharacterCount = sizeof(CharacterTable) / sizeof(CharacterTable[0]);

// =====================================================================
// Builder — converts a FCharacterKit entry into a runtime UObject
// =====================================================================

static USlopArenaCharacterDefinition* BuildFromEntry(const FCharacterKit& Entry)
{
	FLightAttackData Light;
	Light.Damage = static_cast<float>(Entry.Light.Damage);
	Light.KnockbackForce = Entry.Light.KnockbackForce;
	Light.KnockbackUpward = Entry.Light.KnockbackUpward;
	Light.Range = Entry.Light.Range;
	Light.Radius = Entry.Light.Radius;
	Light.ChainWindow = Entry.Light.ChainWindow;
	Light.FinisherMultiplier = Entry.Light.FinisherMultiplier;

	auto ToAbility = [](const FAbilityEntry& Src) -> FAbilityData
	{
		FAbilityData Dst;
		Dst.Name = Src.Name;
		Dst.Shape = Src.Shape;
		Dst.Cooldown = Src.Cooldown;
		Dst.Damage = static_cast<float>(Src.Damage);
		Dst.KnockbackForce = Src.KnockbackForce;
		Dst.KnockbackUpward = Src.KnockbackUpward;
		Dst.Range = Src.Range;
		Dst.Radius = Src.Radius;
		Dst.CastTime = Src.CastTime;
		Dst.ProjectileSpeed = Src.ProjectileSpeed;
		return Dst;
	};

	USlopArenaCharacterDefinition* Def = NewObject<USlopArenaCharacterDefinition>();
	Def->CharacterName = FText::FromString(Entry.Name.ToString());
	Def->Description = FText::FromString(Entry.Description);
	Def->MaxHP = Entry.MaxHP;
	Def->WalkSpeed = Entry.WalkSpeed;
	Def->JumpForce = Entry.JumpForce;
	Def->Weight = Entry.Weight;
	Def->LightAttack = Light;
	Def->HeavyAttack = ToAbility(Entry.Heavy);
	Def->Ability1 = ToAbility(Entry.Ability1);
	Def->Ability2 = ToAbility(Entry.Ability2);
	Def->Ability3 = ToAbility(Entry.Ability3);
	Def->Ultimate = ToAbility(Entry.Ultimate);
	return Def;
}

} // anonymous namespace

// =====================================================================
// FCharacterRegistry implementation
// =====================================================================

TArray<USlopArenaCharacterDefinition*> FCharacterRegistry::GetAll()
{
	// Try loading from DataAssets first (editor workflow).
	// If none found, fall back to the compiled-in character table.
	TArray<USlopArenaCharacterDefinition*> Result = LoadFromPath(TEXT("/Game/Characters/Roster"));

	if (Result.Num() > 0)
	{
		return Result;
	}

	// No DataAssets yet — use the compiled fallback table.
	return CreateFallbackRoster();
}

TArray<USlopArenaCharacterDefinition*> FCharacterRegistry::LoadFromPath(const FString& ContentPath)
{
	TArray<USlopArenaCharacterDefinition*> Result;

	UObjectLibrary* Lib = UObjectLibrary::CreateLibrary(USlopArenaCharacterDefinition::StaticClass(), false, true);
	Lib->AddToRoot();
	Lib->LoadAssetDataFromPath(ContentPath);
	Lib->LoadAssetsFromAssetData();

	TArray<FAssetData> AssetDataList;
	Lib->GetAssetDataList(AssetDataList);

	for (const FAssetData& Asset : AssetDataList)
	{
		if (USlopArenaCharacterDefinition* Def = Cast<USlopArenaCharacterDefinition>(Asset.GetAsset()))
		{
			Result.Add(Def);
		}
	}

	Lib->RemoveFromRoot();
	return Result;
}

TArray<USlopArenaCharacterDefinition*> FCharacterRegistry::CreateFallbackRoster()
{
	TArray<USlopArenaCharacterDefinition*> Result;
	Result.Reserve(CharacterCount);

	for (int32 i = 0; i < CharacterCount; ++i)
	{
		Result.Add(BuildFromEntry(CharacterTable[i]));
	}

	return Result;
}
