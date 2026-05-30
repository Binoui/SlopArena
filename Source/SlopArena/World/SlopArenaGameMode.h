// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "SlopArenaGameMode.generated.h"

/**
 * DKO-style arena brawler game mode.
 * Handles match flow, respawns, and win conditions.
 */
UCLASS()
class SLOPARENA_API ASlopArenaGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	ASlopArenaGameMode();

	virtual void BeginPlay() override;

	/** Respawn a character at a random arena position. */
	UFUNCTION(BlueprintCallable, Category = "Game")
	void RespawnCharacter(AController* Controller);

protected:
	UPROPERTY(EditDefaultsOnly, BlueprintReadOnly, Category = "Game")
	float RespawnDelay = 3.0f;

	UPROPERTY(EditDefaultsOnly, BlueprintReadOnly, Category = "Game")
	int32 KillLimit = 10;

	UPROPERTY(EditDefaultsOnly, BlueprintReadOnly, Category = "Game")
	float RoundTime = 300.0f; // 5 minutes
};
