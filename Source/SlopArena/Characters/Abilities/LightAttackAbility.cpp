// Copyright SlopArena Contributors. MIT License.

#include "LightAttackAbility.h"
#include "SlopArena/Characters/SlopArenaCharacter.h"
#include "SlopArena/SlopArena.h"
#include "Abilities/Tasks/AbilityTask_PlayMontageAndWait.h"
#include "Abilities/Tasks/AbilityTask_WaitGameplayEvent.h"

/**
 * Per-hit data for the light attack combo.
 * In a full implementation, this would come from the character's definition asset.
 */
static constexpr float LightAttackDamages[3] = { 6.0f, 8.0f, 12.0f };
static constexpr float LightAttackKnockbacks[3] = { 8.0f, 12.0f, 25.0f };
static constexpr float LightAttackUpwards[3] = { 2.0f, 3.0f, 8.0f };

ULightAttackAbility::ULightAttackAbility()
{
	AbilityShape = EAbilityShape::MeleeCone;
	CastTime = 0.0f; // Instant
	InstancingPolicy = EGameplayAbilityInstancingPolicy::InstancedPerActor;
}

void ULightAttackAbility::ActivateAbility(
	const FGameplayAbilitySpecHandle Handle,
	const FGameplayAbilityActorInfo* ActorInfo,
	const FGameplayAbilityActivationInfo ActivationInfo,
	const FGameplayEventData* TriggerEventData)
{
	if (!CommitAbility(Handle, ActorInfo, ActivationInfo))
	{
		EndAbility(Handle, ActorInfo, ActivationInfo, true, false);
		return;
	}

	ASlopArenaCharacter* Owner = Cast<ASlopArenaCharacter>(GetAvatarActorFromActorInfo());
	if (!Owner)
	{
		EndAbility(Handle, ActorInfo, ActivationInfo, true, false);
		return;
	}

	// Reset combo if too much time passed
	// (In a full implementation, ComboTimer would track the chain window)

	// Apply hit based on current combo index
	float Damage = LightAttackDamages[CurrentHitIndex] * 
		(CurrentHitIndex == 2 ? 1.5f : 1.0f); // Finisher multiplier
	float Knocback = LightAttackKnockbacks[CurrentHitIndex];
	float Upward = LightAttackUpwards[CurrentHitIndex];

	FVector Origin = Owner->GetActorLocation();
	FVector Direction = Owner->GetActorForwardVector(); // Unreal: +X = forward

	// For now, log the hit
	UE_LOG(LogSlopArenaCombat, Log, TEXT("LightAttack hit %d: dmg=%.1f kb=%.1f up=%.1f"),
		CurrentHitIndex + 1, Damage, Knocback, Upward);

	// Advance combo
	CurrentHitIndex = (CurrentHitIndex + 1) % MaxComboHits;

	EndAbility(Handle, ActorInfo, ActivationInfo, false, false);
}

void ULightAttackAbility::OnNextHit()
{
	// If we're within the chain window, this advances the combo
	// Called by input binding when the player presses light attack again
	if (CurrentHitIndex < MaxComboHits)
	{
		// Re-trigger
	}
}
