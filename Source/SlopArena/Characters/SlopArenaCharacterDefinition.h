// Copyright SlopArena Contributors. MIT License.

#pragma once
#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "GameplayTagContainer.h"
#include "SlopArenaCharacterDefinition.generated.h"

class UGameplayAbility;
class UAnimMontage;

/** Shape of an ability's hitbox for hit detection. */
UENUM(BlueprintType)
enum class EAbilityShape : uint8
{
	MeleeCone       UMETA(DisplayName = "Melee Cone"),
	Projectile      UMETA(DisplayName = "Projectile"),
	CircleAoE       UMETA(DisplayName = "Circle AoE"),
	SelfBuff        UMETA(DisplayName = "Self Buff"),
	Beam            UMETA(DisplayName = "Hitscan Beam"),
};

/** Data for a single ability in a character's kit. */
USTRUCT(BlueprintType)
struct FAbilityData
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	FName Name;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	FText Description;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	EAbilityShape Shape = EAbilityShape::MeleeCone;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	float Cooldown = 8.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	float Damage = 15.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	float KnockbackForce = 20.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	float KnockbackUpward = 5.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	float Range = 500.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	float Radius = 150.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	float CastTime = 0.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	float ProjectileSpeed = 0.0f; // 0 = instant

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Ability")
	FGameplayTagContainer Tags;
};

/** Light attack data (3-hit combo). */
USTRUCT(BlueprintType)
struct FLightAttackData
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LightAttack")
	float Damage = 8.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LightAttack")
	float KnockbackForce = 10.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LightAttack")
	float KnockbackUpward = 3.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LightAttack")
	float Range = 250.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LightAttack")
	float Radius = 120.0f;

	/** Window in seconds to chain to next hit. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LightAttack")
	float ChainWindow = 0.4f;

	/** Knockback multiplier on the 3rd (finisher) hit. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LightAttack")
	float FinisherMultiplier = 1.5f;
};

/** Full character definition — one data asset per playable character. */
UCLASS(BlueprintType, Blueprintable)
class SLOPARENA_API USlopArenaCharacterDefinition : public UDataAsset
{
	GENERATED_BODY()

public:
	// ~ Identity
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Character")
	FText CharacterName;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Character")
	FText Description;

	// ~ Visual
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Visual")
	TSoftObjectPtr<USkeletalMesh> Mesh;

	/** Shared humanoid skeleton (Mixamo-based) used by all characters. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Visual")
	TSoftObjectPtr<USkeleton> Skeleton;

	// ~ Stats
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Stats")
	float MaxHP = 100.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Stats")
	float WalkSpeed = 600.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Stats")
	float JumpForce = 600.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Stats")
	float Weight = 1.0f; // Knockback resistance multiplier

	// ~ Combat data
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Combo")
	FLightAttackData LightAttack;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Combo")
	FAbilityData HeavyAttack;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Abilities")
	FAbilityData Ability1;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Abilities")
	FAbilityData Ability2;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Abilities")
	FAbilityData Ability3;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Abilities")
	FAbilityData Ultimate;

	// ~ Animation slots (one UAnimMontage per ability)
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Animation")
	TSoftObjectPtr<UAnimMontage> LightAttackMontage;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Animation")
	TSoftObjectPtr<UAnimMontage> HeavyMontage;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Animation")
	TSoftObjectPtr<UAnimMontage> Ability1Montage;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Animation")
	TSoftObjectPtr<UAnimMontage> Ability2Montage;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Animation")
	TSoftObjectPtr<UAnimMontage> Ability3Montage;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Animation")
	TSoftObjectPtr<UAnimMontage> UltimateMontage;

	// ~ GAS references (set in Blueprint child, only needed at runtime)
	UPROPERTY(EditDefaultsOnly, BlueprintReadOnly, Category = "GAS")
	TArray<TSubclassOf<UGameplayAbility>> GrantedAbilities;
};
