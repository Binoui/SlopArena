// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "Abilities/GameplayAbility.h"
#include "SlopArena/Shared/CombatMath.h"
#include "SlopArena/Shared/SpellResolver.h"
#include "SlopArena/Characters/SlopArenaCharacterDefinition.h"
#include "SlopArenaAbility.generated.h"

class ASlopArenaCharacter;

/**
 * Base ability for all SlopArena abilities.
 * Handles the common pattern of:
 *   1. Activation (cast time, animation)
 *   2. Hit detection
 *   3. Apply damage + knockback
 *   4. Cooldown
 */
UCLASS()
class SLOPARENA_API USlopArenaAbility : public UGameplayAbility
{
	GENERATED_BODY()

public:
	/** Time before the ability effect triggers. 0 = instant. */
	UPROPERTY(EditDefaultsOnly, BlueprintReadOnly, Category = "SlopArena")
	float CastTime = 0.0f;

	/** Shape of the ability for hit detection. */
	UPROPERTY(EditDefaultsOnly, BlueprintReadOnly, Category = "SlopArena")
	EAbilityShape AbilityShape = EAbilityShape::MeleeCone;

	/** Raw ability data (overrides). */
	UPROPERTY(EditDefaultsOnly, BlueprintReadOnly, Category = "SlopArena")
	FAbilityData AbilityData;

protected:
	/** Perform hit detection and apply results. */
	void PerformHitDetection(ASlopArenaCharacter* Owner, const FVector& Origin, const FVector& Direction);
};
