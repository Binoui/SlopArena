// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "ArenaDefinition.h"

/**
 * Registry of built-in arenas.
 * DKO-style: floating platforms in the sky with ring-out kills.
 */
struct SLOPARENA_API FArenaRegistry
{
	static FArenaDefinition CreateSanctuary();    // Classic colosseum
	static FArenaDefinition CreateSkyTemple();    // Multi-tier platforms
	static FArenaDefinition CreatePillarPit();    // Central pillar cover
	static FArenaDefinition CreateLavaFoundry();  // Hazardous edges

	static TArray<FArenaDefinition> GetAll();
};
