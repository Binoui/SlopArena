// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "EActionState.generated.h"

UENUM(BlueprintType)
enum class EActionState : uint8
{
	Idle       UMETA(DisplayName = "Idle"),
	Attacking  UMETA(DisplayName = "Attacking"),
	Dashing    UMETA(DisplayName = "Dashing"),
	Sliding    UMETA(DisplayName = "Sliding"),
	AirDodging UMETA(DisplayName = "Air Dodging"),
	Hitstun    UMETA(DisplayName = "Hitstun"),
};
