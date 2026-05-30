// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "ArenaDefinition.generated.h"

/** A single platform in the arena. */
USTRUCT(BlueprintType)
struct FPlatformData
{
	GENERATED_BODY()

	/** Center position of the platform. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Platform")
	FVector Center = FVector::ZeroVector;

	/** Size of the platform (X = width, Y = depth). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Platform")
	FVector2D Size = FVector2D(1000.0f, 1000.0f);

	/** Whether this is the main arena floor (spawn on it). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Platform")
	bool bIsMainFloor = false;

	/** Whether this platform has wall/pillar cover. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Platform")
	bool bHasCover = false;

	/** Cover position offset from platform center. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Platform")
	FVector2D CoverOffset = FVector2D::ZeroVector;
};

/** A pillar or wall for cover / line-of-sight breaks. */
USTRUCT(BlueprintType)
struct FCoverData
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Cover")
	FVector Center = FVector::ZeroVector;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Cover")
	FVector2D Size = FVector2D(100.0f, 100.0f); // Width, depth

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Cover")
	float Height = 500.0f;
};

/** A spawn point for a player. */
USTRUCT(BlueprintType)
struct FSpawnPointData
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Spawn")
	FVector Location = FVector::ZeroVector;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Spawn")
	float Yaw = 0.0f;
};

/**
 * Full definition of an arena layout.
 * DKO-style: floating platforms in the sky, ring-out kills.
 */
USTRUCT(BlueprintType)
struct FArenaDefinition
{
	GENERATED_BODY()

	/** Display name. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Arena")
	FText ArenaName;

	/** Theme description. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Arena")
	FText Description;

	/** World bounds (kill plane / void below this Z). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Arena")
	float VoidPlaneZ = -5000.0f;

	/** Skybox height (ceiling). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Arena")
	float CeilingHeight = 5000.0f;

	/** All platforms in the arena. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Arena")
	TArray<FPlatformData> Platforms;

	/** Cover objects (pillars, walls). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Arena")
	TArray<FCoverData> CoverObjects;

	/** Player spawn points. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Arena")
	TArray<FSpawnPointData> SpawnPoints;
};
