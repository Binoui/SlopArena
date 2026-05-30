// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "SlopArenaCharacterDefinition.h"
#include "SlopArena/Shared/EStatusType.h"

/**
 * Registry of playable characters.
 * Each entry defines a complete DKO-style kit:
 *   - Light attack (3-hit chain)
 *   - Heavy attack
 *   - Ability 1, 2, 3
 *   - Ultimate
 *   - Stats (HP, speed, jump, weight)
 *
 * In the future these will live as DataAssets in the Content Browser.
 * For now they're defined in code so we can iterate without the editor.
 */
struct SLOPARENA_API FCharacterRegistry
{
	/** Build a character definition programmatically. */
	static USlopArenaCharacterDefinition* CreateBrawler();
	static USlopArenaCharacterDefinition* CreateRanger();
	static USlopArenaCharacterDefinition* CreateAssassin();
	static USlopArenaCharacterDefinition* CreateTank();
	static USlopArenaCharacterDefinition* CreateSol();

	/** Get all available characters. */
	static TArray<USlopArenaCharacterDefinition*> GetAll();

private:
	static USlopArenaCharacterDefinition* MakeDefinition(
		const FText& Name,
		const FText& Desc,
		float HP,
		float Speed,
		float Jump,
		float Weight,
		FLightAttackData Light,
		FAbilityData Heavy,
		FAbilityData Ability1,
		FAbilityData Ability2,
		FAbilityData Ability3,
		FAbilityData Ultimate);
};
