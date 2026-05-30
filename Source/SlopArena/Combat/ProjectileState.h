// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "ProjectileState.generated.h"

/**
 * Pure data state of a single projectile.
 * Designed for NetworkPrediction rollback — no engine references.
 * Mirrors the Godot ProjectileState.cs.
 */
USTRUCT(BlueprintType)
struct FProjectileState
{
	GENERATED_BODY()

	UPROPERTY()
	uint64 ProjectileId = 0;

	UPROPERTY()
	uint64 CasterEntityId = 0;

	UPROPERTY()
	FVector Position = FVector::ZeroVector;

	UPROPERTY()
	FVector Direction = FVector::ZeroVector;

	UPROPERTY()
	float Speed = 0.0f;

	UPROPERTY()
	float Damage = 0.0f;

	UPROPERTY()
	float KnockbackForce = 0.0f;

	UPROPERTY()
	float KnockbackUpward = 0.0f;

	UPROPERTY()
	float Radius = 50.0f;

	UPROPERTY()
	float MaxRange = 0.0f; // 0 = unlimited

	UPROPERTY()
	float DistanceTraveled = 0.0f;

	UPROPERTY()
	bool bActive = true;

	/** Simulate one tick of projectile movement. Returns false if projectile should be destroyed. */
	bool SimulateStep(float DeltaTime)
	{
		if (!bActive) return false;

		// Save old position for line intersection
		OldPosition = Position;

		// Move
		float Step = Speed * DeltaTime;
		Position += Direction * Step;
		DistanceTraveled += Step;

		// Check range
		if (MaxRange > 0.0f && DistanceTraveled >= MaxRange)
		{
			bActive = false;
			return false;
		}

		return true;
	}

	/** Get old position for line intersection checks (set by SimulateStep). */
	FVector GetOldPosition() const { return OldPosition; }

	void ResetOldPosition() { OldPosition = Position; }

private:
	FVector OldPosition = FVector::ZeroVector;
};
