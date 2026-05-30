// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "SlopArenaAbility.h"
#include "LightAttackAbility.generated.h"

/**
 * Light attack ability with a 3-hit combo chain.
 * 
 * Hit 1: Quick jab — low damage, low knockback
 * Hit 2: Cross — medium damage, medium knockback  
 * Hit 3: Finisher (chain window) — high damage, high knockback, launches
 *
 * If the player doesn't attack within the chain window, the combo resets.
 */
UCLASS()
class SLOPARENA_API ULightAttackAbility : public USlopArenaAbility
{
	GENERATED_BODY()

public:
	ULightAttackAbility();

	virtual void ActivateAbility(
		const FGameplayAbilitySpecHandle Handle,
		const FGameplayAbilityActorInfo* ActorInfo,
		const FGameplayAbilityActivationInfo ActivationInfo,
		const FGameplayEventData* TriggerEventData) override;

	/** Called when the next hit in the chain is triggered. */
	UFUNCTION()
	void OnNextHit();

private:
	int32 CurrentHitIndex = 0;
	float ComboTimer = 0.0f;

	static constexpr int32 MaxComboHits = 3;
};
