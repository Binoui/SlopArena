// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "ProjectileActor.generated.h"

class UProjectileMovementComponent;
class USphereComponent;

/**
 * Visual/networked projectile actor.
 * Spawned by ProjectileManager when a projectile is created.
 * Synced to the server-authoritative FProjectileState.
 *
 * In a full NetworkPrediction setup, this actor reads from
 * the projectile simulation state rather than predicting itself.
 */
UCLASS()
class SLOPARENA_API AProjectileActor : public AActor
{
	GENERATED_BODY()

public:
	AProjectileActor();

	/** Initialize from projectile state data. */
	void InitFromState(const struct FProjectileState& State);

	/** Update visual position to match simulation state. */
	void SyncToState(const struct FProjectileState& State);

	/** Called when this projectile hits something. */
	void OnHit(AActor* OtherActor);

protected:
	virtual void BeginPlay() override;
	virtual void Tick(float DeltaTime) override;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Projectile")
	TObjectPtr<USphereComponent> CollisionComponent;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Projectile")
	TObjectPtr<UProjectileMovementComponent> MovementComponent;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Projectile")
	TObjectPtr<UStaticMeshComponent> MeshComponent;

	/** Speed of the projectile for the movement component. */
	float Speed = 0.0f;

	/** Cached projectile ID for tracking. */
	uint64 ProjectileId = 0;

	/** Entity that fired this projectile. */
	uint64 CasterId = 0;

	/** Damage on hit. */
	float Damage = 0.0f;

	/** Knockback force on hit. */
	float KickForce = 0.0f;
	float KickUpward = 0.0f;
};
