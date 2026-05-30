// Copyright SlopArena Contributors. MIT License.

#include "SpellResolver.h"
#include "CombatMath.h"
#include <cmath>

TOptional<FHitResultStruct> FSpellResolver::ResolveProjectileHit(
	const FVector2D& OldPos,
	const FVector2D& NewPos,
	const FVector& Origin,
	float Damage,
	float KnockbackForce,
	float KnockbackUpward,
	uint64 CasterEntityId,
	TArrayView<const FEntityData> Entities)
{
	for (const auto& Entity : Entities)
	{
		if (!Entity.bActive || Entity.Id == CasterEntityId)
			continue;

		bool bIntersects = FCombatMath::LineIntersectsCircle(
			OldPos, NewPos,
			FVector2D(Entity.Position.X, Entity.Position.Y),
			Entity.Radius
		);

		if (bIntersects)
		{
			FVector Knockback = FCombatMath::CalculateKnockback(
				Entity.Position, Origin,
				KnockbackForce, KnockbackUpward
			);

			return FHitResultStruct{
				Entity.Id, Damage, Knockback
			};
		}
	}

	return TOptional<FHitResultStruct>();
}

TArray<FHitResultStruct> FSpellResolver::ResolveCircleHit(
	const FVector& Center,
	float Radius,
	float Damage,
	float KnockbackForce,
	float KnockbackUpward,
	uint64 CasterEntityId,
	TArrayView<const FEntityData> Entities)
{
	TArray<FHitResultStruct> Results;

	for (const auto& Entity : Entities)
	{
		if (!Entity.bActive || Entity.Id == CasterEntityId)
			continue;

		bool bInRange = FCombatMath::IsInCircle(
			Entity.Position, Center,
			Radius + Entity.Radius
		);

		if (bInRange)
		{
			FVector Knockback = FCombatMath::CalculateKnockback(
				Entity.Position, Center,
				KnockbackForce, KnockbackUpward
			);

			Results.Add(FHitResultStruct{ Entity.Id, Damage, Knockback });
		}
	}

	return Results;
}

TArray<FHitResultStruct> FSpellResolver::ResolveConeHit(
	const FVector& Origin,
	const FVector& Direction,
	float HalfAngleRad,
	float Range,
	float Damage,
	float KnockbackForce,
	float KnockbackUpward,
	uint64 CasterEntityId,
	TArrayView<const FEntityData> Entities)
{
	TArray<FHitResultStruct> Results;

	for (const auto& Entity : Entities)
	{
		if (!Entity.bActive || Entity.Id == CasterEntityId)
			continue;

		bool bInCone = FCombatMath::IsInCone(
			Entity.Position, Origin, Direction,
			HalfAngleRad, Range + Entity.Radius
		);

		if (bInCone)
		{
			FVector Knockback = FCombatMath::CalculateKnockback(
				Entity.Position, Origin,
				KnockbackForce, KnockbackUpward
			);

			Results.Add(FHitResultStruct{ Entity.Id, Damage, Knockback });
		}
	}

	return Results;
}
