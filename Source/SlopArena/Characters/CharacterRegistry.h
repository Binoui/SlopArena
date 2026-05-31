// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "SlopArenaCharacterDefinition.h"
#include "SlopArena/Shared/EStatusType.h"

/**
 * Registry of playable characters.
 *
 * Primary source: DataAssets in /Game/Characters/Roster/ (set via LoadFromPath).
 * Fallback: compiled-in data table (CreateFallbackRoster) so the project can
 * build/test without the editor.
 *
 * When the editor is available, create Blueprint child assets of
 * USlopArenaCharacterDefinition in the Content Browser and they'll load
 * automatically at runtime — no C++ changes needed for balance patches.
 */
struct SLOPARENA_API FCharacterRegistry
{
	/** Get all available characters — tries DataAssets first, falls back to compiled defaults. */
	static TArray<USlopArenaCharacterDefinition*> GetAll();

	/** Try to load roster from a Content directory path (e.g. "/Game/Characters/Roster"). */
	static TArray<USlopArenaCharacterDefinition*> LoadFromPath(const FString& ContentPath);

private:
	/** Fallback definitions compiled into the binary. Used when no DataAssets exist. */
	static TArray<USlopArenaCharacterDefinition*> CreateFallbackRoster();
};
