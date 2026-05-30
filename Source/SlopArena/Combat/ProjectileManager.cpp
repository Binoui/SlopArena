// Copyright SlopArena Contributors. MIT License.

#include "ProjectileManager.h"
#include "SlopArena/Shared/CombatMath.h"
#include "SlopArena/SlopArena.h"

uint64 FProjectileManager::SpawnProjectile(
	uint64 CasterId,
	const FVector& Origin,
	const FVector& Direction,
	float Speed,
	float Damage,
	float KnockbackForce,
	float KnockbackUpward,
	float Radius,
	float MaxRange)
{
	uint64 Id = NextId++;

	FProjectileState State;
	State.ProjectileId = Id;
	State.CasterEntityId = CasterId;
	State.Position = Origin;
	State.Direction = Direction.GetSafeNormal();
	State.Speed = Speed;
	State.Damage = Damage;
	State.KnockbackForce = KnockbackForce;
	State.KnockbackUpward = KnockbackUpward;
	State.Radius = Radius;
	State.MaxRange = MaxRange;
	State.DistanceTraveled = 0.0f;
	State.bActive = true;
	State.ResetOldPosition();

	Projectiles.Add(Id, State);

	UE_LOG(LogSlopArenaCombat, Verbose, TEXT("Projectile %llu spawned by %llu at %s dir %s speed=%.0f"),
		Id, CasterId, *Origin.ToString(), *Direction.ToString(), Speed);

	return Id;
}

TArray<FHitResultStruct> FProjectileManager::SimulateAll(float DeltaTime)
{
	TArray<FHitResultStruct> Hits;
	TArray<uint64> ToRemove;

	for (auto& [Id, Proj] : Projectiles)
	{
		if (!Proj.bActive) continue;

		FVector OldPos = Proj.Position;

		// Simulate movement
		bool bAlive = Proj.SimulateStep(DeltaTime);
		if (!bAlive)
		{
			ToRemove.Add(Id);
			continue;
		}

		// Check for hits along the path (line from old pos to new pos)
		bool bUseLineCheck = true;
		if (bUseLineCheck)
		{
			FVector2D Old2D(OldPos.X, OldPos.Y);
			FVector2D New2D(Proj.Position.X, Proj.Position.Y);

			for (const auto& Entity : Entities)
			{
				if (!Entity.bActive || Entity.Id == Proj.CasterEntityId)
					continue;

				bool bIntersects = FCombatMath::LineIntersectsCircle(
					Old2D, New2D,
					FVector2D(Entity.Position.X, Entity.Position.Y),
					Proj.Radius + Entity.Radius
				);

				if (bIntersects)
				{
					FVector Knockback = FCombatMath::CalculateKnockback(
						Entity.Position, Proj.Position,
						Proj.KnockbackForce, Proj.KnockbackUpward
					);

					Hits.Add(FHitResultStruct{
						Entity.Id, Proj.Damage, Knockback
					});

					ToRemove.Add(Id);
					break; // Projectile consumed
				}
			}
		}
	}

	// Cleanup
	for (uint64 Id : ToRemove)
	{
		RemoveProjectile(Id);
	}

	return Hits;
}

void FProjectileManager::RemoveProjectile(uint64 Id)
{
	if (Projectiles.Contains(Id))
	{
		Projectiles[Id].bActive = false;
		Projectiles.Remove(Id);
	}
}

void FProjectileManager::Clear()
{
	Projectiles.Empty();
	Entities.Empty();
}

TOptional<FHitResultStruct> FProjectileManager::CheckProjectileHit(const FProjectileState& Proj)
{
	FVector2D ProjPos(Proj.Position.X, Proj.Position.Y);
	FVector2D OldPos(Proj.GetOldPosition().X, Proj.GetOldPosition().Y);

	for (const auto& Entity : Entities)
	{
		if (!Entity.bActive || Entity.Id == Proj.CasterEntityId)
			continue;

		bool bIntersects = FCombatMath::LineIntersectsCircle(
			OldPos, ProjPos,
			FVector2D(Entity.Position.X, Entity.Position.Y),
			Proj.Radius + Entity.Radius
		);

		if (bIntersects)
		{
			FVector Knockback = FCombatMath::CalculateKnockback(
				Entity.Position, Proj.Position,
				Proj.KnockbackForce, Proj.KnockbackUpward
			);

			return FHitResultStruct{ Entity.Id, Proj.Damage, Knockback };
		}
	}

	return TOptional<FHitResultStruct>();
}
