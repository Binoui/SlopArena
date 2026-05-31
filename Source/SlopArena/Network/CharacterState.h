// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "SlopArena/Shared/EActionState.h"
#include "SlopArena/Shared/MovementProfile.h"
#include "GameFramework/Actor.h"
#include "CharacterState.generated.h"

/**
 * Full character state synchronised between server and clients.
 * Used by NetworkPrediction plugin.
 */
USTRUCT(BlueprintType)
struct FSlopArenaCharacterState
{
	GENERATED_BODY()

	UPROPERTY()
	FVector Location = FVector::ZeroVector;

	UPROPERTY()
	FVector Velocity = FVector::ZeroVector;

	UPROPERTY()
	float Yaw = 0.0f;

	UPROPERTY()
	EActionState ActionState = EActionState::Idle;

	UPROPERTY()
	uint8 StateTicksRemaining = 0;

	UPROPERTY()
	uint8 DashCooldownTicks = 0;

	UPROPERTY()
	float HP = 100.0f;

	UPROPERTY()
	uint8 CombatLockoutTicks = 0;

	UPROPERTY()
	bool bIsGrounded = false;
};

/**
 * Client input per frame/tick.
 */
USTRUCT(BlueprintType)
struct FSlopArenaInputState
{
	GENERATED_BODY()

	UPROPERTY()
	uint8 MovementFlags = 0; // Bitfield

	UPROPERTY()
	uint8 ActionFlags = 0;   // Bitfield

	UPROPERTY()
	float Yaw = 0.0f;        // Camera yaw

	void Reset()
	{
		MovementFlags = 0;
		ActionFlags = 0;
		Yaw = 0.0f;
	}
};
