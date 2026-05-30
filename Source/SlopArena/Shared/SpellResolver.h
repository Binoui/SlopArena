// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "HitResultStruct.h"

/**
 * Pure C++ spell/ability hit resolution.
 * No Unreal engine dependencies beyond basic types.
 * Designed to be usable from NetworkPrediction simulation.
 */
struct SLOPARENA_API FSpellResolver
{
	/** Entity data needed for hit detection. */
	struct FEntityData
	{
		uint64 Id = 0;
		FVector Position = FVector::ZeroVector;
		float Radius = 90.0f;
		bool bActive = true;
	};

	/** Resolve a projectile hit (line segment intersection). */
	static TOptional<FHitResultStruct> ResolveProjectileHit(
		const FVector2D& OldPos,
		const FVector2D& NewPos,
		const FVector& Origin,
		float Damage,
		float KnockbackForce,
		float KnockbackUpward,
		uint64 CasterEntityId,
		TArrayView<const FEntityData> Entities);

	/** Resolve a circle AoE hit (explosion). */
	static TArray<FHitResultStruct> ResolveCircleHit(
		const FVector& Center,
		float Radius,
		float Damage,
		float KnockbackForce,
		float KnockbackUpward,
		uint64 CasterEntityId,
		TArrayView<const FEntityData> Entities);

	/** Resolve a cone AoE hit (melee slash). */
	static TArray<FHitResultStruct> ResolveConeHit(
		const FVector& Origin,
		const FVector& Direction,
		float HalfAngleRad,
		float Range,
		float Damage,
		float KnockbackForce,
		float KnockbackUpward,
		uint64 CasterEntityId,
		TArrayView<const FEntityData> Entities);
};
