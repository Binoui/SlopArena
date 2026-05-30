// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "AIController.h"
#include "BotBrain.h"
#include "SlopArenaBotController.generated.h"

class ASlopArenaCharacter;

/**
 * AI controller that runs the FBotBrain decision logic.
 * Each tick:
 *   1. Gathers perception (self state, nearest enemy, cooldowns)
 *   2. Runs FBotBrain::Decide()
 *   3. Executes the decision (movement, abilities)
 */
UCLASS()
class SLOPARENA_API ASlopArenaBotController : public AAIController
{
	GENERATED_BODY()

public:
	ASlopArenaBotController();

	virtual void Tick(float DeltaTime) override;

	/** Set the character this bot controls. */
	void SetControlledCharacter(ASlopArenaCharacter* InChar) { ControlledChar = InChar; }

	/** Set the bot's difficulty name for display. */
	UPROPERTY(EditDefaultsOnly, Category = "Bot")
	FString BotName = TEXT("Bot");

protected:
	virtual void BeginPlay() override;

	/** Gather all perception data the brain needs. */
	FBotPerception GatherPerception() const;

	/** Execute the bot's decision. */
	void ExecuteDecision(const FBotDecision& Decision);

	/** Find the nearest alive enemy. */
	AActor* FindNearestEnemy() const;

private:
	UPROPERTY()
	TObjectPtr<ASlopArenaCharacter> ControlledChar;

	/** Target actor the bot is focused on. */
	UPROPERTY()
	TWeakObjectPtr<AActor> CurrentTarget;

	/** Tick rate limiter (don't need to rethink every frame). */
	float DecisionCooldown = 0.0f;
	static constexpr float DecisionInterval = 0.1f; // 10 decisions/sec
};
