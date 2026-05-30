// Copyright SlopArena Contributors. MIT License.

#include "SlopArenaGameMode.h"
#include "SlopArena/SlopArena.h"

ASlopArenaGameMode::ASlopArenaGameMode()
{
	// Default pawn class set in Blueprint child
	DefaultPawnClass = nullptr;
}

void ASlopArenaGameMode::BeginPlay()
{
	Super::BeginPlay();
	UE_LOG(LogSlopArena, Log, TEXT("SlopArena GameMode started"));
}

void ASlopArenaGameMode::RespawnCharacter(AController* Controller)
{
	if (!Controller) return;

	// Teleport to a random arena position
	FVector SpawnLocation(FMath::RandRange(500.0f, 4500.0f), FMath::RandRange(500.0f, 4500.0f), 200.0f);

	if (APawn* Pawn = Controller->GetPawn())
	{
		Pawn->TeleportTo(SpawnLocation, FRotator::ZeroRotator);
	}
}
