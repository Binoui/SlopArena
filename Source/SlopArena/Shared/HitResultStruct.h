// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "EStatusType.h"
#include "HitResultStruct.generated.h"

USTRUCT(BlueprintType)
struct FHitResultStruct
{
	GENERATED_BODY()

	UPROPERTY()
	uint64 TargetEntityId = 0;

	UPROPERTY()
	float Damage = 0.0f;

	UPROPERTY()
	FVector Knockback = FVector::ZeroVector;
};
