// Copyright SlopArena Contributors. MIT License.

#include "SlopArenaAbility.h"
#include "SlopArena/Characters/SlopArenaCharacter.h"
#include "SlopArena/SlopArena.h"

void USlopArenaAbility::PerformHitDetection(
	ASlopArenaCharacter* Owner,
	const FVector& Origin,
	const FVector& Direction)
{
	if (!Owner) return;

	const float HalfAngleRad = FMath::DegreesToRadians(45.0f); // Default 90-degree cone

	// Build entity list from arena participants
	TArray<FSpellResolver::FEntityData> Entities;
	// TODO: Gather all alive characters from game state

	const float EffectiveRadius = AbilityData.Radius;
	const float EffectiveRange = AbilityData.Range;

	switch (AbilityShape)
	{
	case EAbilityShape::MeleeCone:
	{
		TArrayView<const FSpellResolver::FEntityData> View(
			Entities.GetData(), Entities.Num());

		auto Results = FSpellResolver::ResolveConeHit(
			Origin, Direction, HalfAngleRad, EffectiveRange,
			AbilityData.Damage, AbilityData.KnockbackForce, AbilityData.KnockbackUpward,
			0, View
		);

		for (const auto& Hit : Results)
		{
			// Apply via GAS gameplay effect
			UE_LOG(LogSlopArenaCombat, Verbose, TEXT("Cone hit entity %llu for %.1f dmg"),
				Hit.TargetEntityId, Hit.Damage);
		}
		break;
	}
	case EAbilityShape::Projectile:
	{
		// Projectile spawning handled by a separate projectile actor
		UE_LOG(LogSlopArenaCombat, Verbose, TEXT("Spawning projectile: %s"), *AbilityData.Name.ToString());
		break;
	}
	case EAbilityShape::CircleAoE:
	{
		TArrayView<const FSpellResolver::FEntityData> View(
			Entities.GetData(), Entities.Num());

		auto Results = FSpellResolver::ResolveCircleHit(
			Origin, EffectiveRange, AbilityData.Damage, AbilityData.KnockbackForce, AbilityData.KnockbackUpward,
			0, View
		);

		for (const auto& Hit : Results)
		{
			UE_LOG(LogSlopArenaCombat, Verbose, TEXT("AoE hit entity %llu for %.1f dmg"),
				Hit.TargetEntityId, Hit.Damage);
		}
		break;
	}
	case EAbilityShape::SelfBuff:
	{
		// Apply self-effect (shield, speed boost, etc.)
		UE_LOG(LogSlopArenaCombat, Verbose, TEXT("Self buff: %s"), *AbilityData.Name.ToString());
		break;
	}
	case EAbilityShape::Beam:
	{
		TArrayView<const FSpellResolver::FEntityData> View(
			Entities.GetData(), Entities.Num());

		auto Results = FSpellResolver::ResolveConeHit(
			Origin, Direction, 0.1f, EffectiveRange,
			AbilityData.Damage, AbilityData.KnockbackForce, AbilityData.KnockbackUpward,
			0, View
		);

		for (const auto& Hit : Results)
		{
			UE_LOG(LogSlopArenaCombat, Verbose, TEXT("Beam hit entity %llu for %.1f dmg"),
				Hit.TargetEntityId, Hit.Damage);
		}
		break;
	}
	}
}
