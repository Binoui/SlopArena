// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "SlopArena/Shared/MovementProfile.h"
#include "BotBrain.generated.h"

class ASlopArenaCharacter;

/** What the bot wants to do right now. */
UENUM(BlueprintType)
enum class EBotGoal : uint8
{
	Idle        UMETA(DisplayName = "Idle"),
	Chase       UMETA(DisplayName = "Chase"),
	Attack      UMETA(DisplayName = "Attack"),
	Retreat     UMETA(DisplayName = "Retreat"),
	Search      UMETA(DisplayName = "Search"),
};

/** Output: what the bot wants to do this tick. */
USTRUCT()
struct FBotDecision
{
	GENERATED_BODY()

	uint8 MovementFlags = 0;
	uint8 ActionFlags = 0;
	float Yaw = 0.0f;
	EBotGoal CurrentGoal = EBotGoal::Idle;
};

/** Information the bot has about itself and the world. */
USTRUCT()
struct FBotPerception
{
	GENERATED_BODY()

	// Self
	FVector MyPosition = FVector::ZeroVector;
	FVector MyVelocity = FVector::ZeroVector;
	float MyHP = 100.0f;
	float MyMaxHP = 100.0f;
	bool bIsGrounded = true;

	// Target
	bool bHasTarget = false;
	FVector TargetPosition = FVector::ZeroVector;
	FVector TargetVelocity = FVector::ZeroVector;
	float TargetHP = 100.0f;
	float DistanceToTarget = 0.0f;

	// Cooldowns (0 = ready)
	float DashCooldown = 0.0f;
	float Ability1Cooldown = 0.0f;
	float Ability2Cooldown = 0.0f;
	float Ability3Cooldown = 0.0f;
	float UltimateCooldown = 0.0f;

	// Arena
	TArray<FVector> PlatformCenters;
	float VoidPlaneZ = -5000.0f;
	float ArenaRadius = 2000.0f;

	// Danger detection
	bool bIncomingProjectile = false;
	float TimeUntilProjectileHit = 999.0f;
};

/**
 * Bot brain — pure decision logic, no engine dependencies.
 * Takes perception input, outputs movement + action decisions.
 * Designed to be testable standalone (see tests/test_botbrain.cpp).
 */
struct SLOPARENA_API FBotBrain
{
	/** Decide what to do this tick based on perception. */
	static FBotDecision Decide(const FBotPerception& Perception);

	/** Get the name of the bot's current strategy for debug. */
	static FString GetStrategyName();

private:
	static void DecideCombat(const FBotPerception& P, FBotDecision& D);
	static void DecideMovement(const FBotPerception& P, FBotDecision& D);
	static void DecideAbilities(const FBotPerception& P, FBotDecision& D);
	static void DecideDefense(const FBotPerception& P, FBotDecision& D);
};
