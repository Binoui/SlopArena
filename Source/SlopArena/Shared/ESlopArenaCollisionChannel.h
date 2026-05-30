// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "ESlopArenaCollisionChannel.generated.h"

UENUM(BlueprintType)
enum class ESlopArenaCollisionChannel : uint8
{
	WorldStatic  UMETA(DisplayName = "World Static"),
	Pawn         UMETA(DisplayName = "Pawn"),
	Hitbox       UMETA(DisplayName = "Hitbox"),
	Hurtbox      UMETA(DisplayName = "Hurtbox"),
};
