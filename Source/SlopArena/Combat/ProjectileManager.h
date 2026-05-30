// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "ProjectileState.h"
#include "SlopArena/Shared/SpellResolver.h"

class ASlopArenaCharacter;

/**
 * Manages all active projectiles in the arena.
 * Handles:
 *   - Spawning new projectiles
 *   - Updating positions each tick
 *   - Hit detection against characters (via SpellResolver)
 *   - Despawning on hit / timeout
 *
 * Designed for NetworkPrediction: the manager can be rolled back
 * by restoring the projectile array from saved state.
 */
struct SLOPARENA_API FProjectileManager
{
	/** Register all alive entities for hit detection. */
	void SetEntities(const TArray<FSpellResolver::FEntityData>& InEntities)
	{
		Entities = InEntities;
	}

	/** Spawn a new projectile. Returns its ID (0 on failure). */
	uint64 SpawnProjectile(
		uint64 CasterId,
		const FVector& Origin,
		const FVector& Direction,
		float Speed,
		float Damage,
		float KnockbackForce,
		float KnockbackUpward,
		float Radius,
		float MaxRange);

	/** Simulate all projectiles for one tick.
	 *  Returns list of hits (entity ID + damage) that occurred this tick. */
	TArray<FHitResultStruct> SimulateAll(float DeltaTime);

	/** Remove a projectile by ID. */
	void RemoveProjectile(uint64 Id);

	/** Remove all projectiles. */
	void Clear();

	/** Get all active projectile states (for network sync or visual updates). */
	const TMap<uint64, FProjectileState>& GetActiveProjectiles() const { return Projectiles; }

private:
	TMap<uint64, FProjectileState> Projectiles;
	TArray<FSpellResolver::FEntityData> Entities;
	uint64 NextId = 1;

	/** Check if a projectile hits any entity. Returns hit result or empty. */
	TOptional<FHitResultStruct> CheckProjectileHit(const FProjectileState& Proj);
};
