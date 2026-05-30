// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "EStatusType.generated.h"

UENUM(BlueprintType)
enum class EStatusType : uint8
{
	None        UMETA(DisplayName = "None"),
	Stunned     UMETA(DisplayName = "Stunned"),
	Slowed      UMETA(DisplayName = "Slowed"),
	KnockedBack UMETA(DisplayName = "Knocked Back"),
	Invincible  UMETA(DisplayName = "Invincible"),
};
