// Copyright SlopArena Contributors. MIT License.

#include "SlopArenaBotController.h"
#include "SlopArena/Characters/SlopArenaCharacter.h"
#include "SlopArena/Combat/ProjectileManager.h"
#include "SlopArena/SlopArena.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "AbilitySystemComponent.h"
#include "GameFramework/GameStateBase.h"
#include "GameFramework/PlayerState.h"

ASlopArenaBotController::ASlopArenaBotController()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.TickGroup = TG_PrePhysics;
	bWantsPlayerState = false;
}

void ASlopArenaBotController::BeginPlay()
{
	Super::BeginPlay();
	ControlledChar = Cast<ASlopArenaCharacter>(GetPawn());
}

void ASlopArenaBotController::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);

	// Rate-limit decisions
	DecisionCooldown -= DeltaTime;
	if (DecisionCooldown > 0.0f)
		return;
	DecisionCooldown = DecisionInterval;

	// Gather perception and decide
	FBotPerception Perception = GatherPerception();
	FBotDecision Decision = FBotBrain::Decide(Perception);
	ExecuteDecision(Decision);
}

FBotPerception ASlopArenaBotController::GatherPerception() const
{
	FBotPerception P;

	if (!ControlledChar)
		return P;

	// Self state
	P.MyPosition = ControlledChar->GetActorLocation();
	P.MyVelocity = ControlledChar->GetVelocity();
	P.bIsGrounded = ControlledChar->GetCharacterMovement()->IsMovingOnGround();

	// HP from GAS attribute set
	if (UAbilitySystemComponent* ASC = ControlledChar->GetAbilitySystemComponent())
	{
		// TODO: Read HP from AttributeSet
		P.MyHP = 100.0f;
		P.MyMaxHP = 100.0f;
	}

	// Find nearest enemy
	AActor* Enemy = FindNearestEnemy();
	if (Enemy)
	{
		P.bHasTarget = true;
		P.TargetPosition = Enemy->GetActorLocation();
		P.TargetVelocity = Enemy->GetVelocity();
		P.DistanceToTarget = FVector::Dist2D(P.MyPosition, P.TargetPosition);
	}

	// Check cooldowns via GAS
	if (UAbilitySystemComponent* ASC = ControlledChar->GetAbilitySystemComponent())
	{
		// TODO: Read actual cooldown values from GAS
		P.DashCooldown = 0.0f;
		P.Ability1Cooldown = 0.0f;
		P.Ability2Cooldown = 0.0f;
		P.Ability3Cooldown = 0.0f;
		P.UltimateCooldown = 0.0f;
	}

	// Arena bounds
	P.ArenaRadius = 2000.0f;
	P.VoidPlaneZ = -5000.0f;

	return P;
}

void ASlopArenaBotController::ExecuteDecision(const FBotDecision& Decision)
{
	if (!ControlledChar) return;

	// Movement flags
	// TODO: Translate movement flags into actual character movement input
	// For now, just move toward the target based on yaw

	// Action flags
	if (Decision.ActionFlags & 0x01) // Light attack
	{
		// Trigger via GAS
		if (UAbilitySystemComponent* ASC = ControlledChar->GetAbilitySystemComponent())
		{
			ASC->TryActivateAbilitiesByTag(FGameplayTag::RequestGameplayTag(FName("Ability.LightAttack")));
		}
	}
	if (Decision.ActionFlags & 0x08) // Ability 1
	{
		if (UAbilitySystemComponent* ASC = ControlledChar->GetAbilitySystemComponent())
		{
			ASC->TryActivateAbilitiesByTag(FGameplayTag::RequestGameplayTag(FName("Ability.Ability1")));
		}
	}
	if (Decision.ActionFlags & 0x10) // Ability 2
	{
		if (UAbilitySystemComponent* ASC = ControlledChar->GetAbilitySystemComponent())
		{
			ASC->TryActivateAbilitiesByTag(FGameplayTag::RequestGameplayTag(FName("Ability.Ability2")));
		}
	}
	if (Decision.ActionFlags & 0x20) // Ability 3
	{
		if (UAbilitySystemComponent* ASC = ControlledChar->GetAbilitySystemComponent())
		{
			ASC->TryActivateAbilitiesByTag(FGameplayTag::RequestGameplayTag(FName("Ability.Ability3")));
		}
	}
	if (Decision.ActionFlags & 0x40) // Ultimate
	{
		if (UAbilitySystemComponent* ASC = ControlledChar->GetAbilitySystemComponent())
		{
			ASC->TryActivateAbilitiesByTag(FGameplayTag::RequestGameplayTag(FName("Ability.Ultimate")));
		}
	}

	// Movement
	if (Decision.MovementFlags & 0x20) // Dash
	{
		// Dash input would go through Enhanced Input or direct ability trigger
	}

	UE_LOG(LogSlopArena, VeryVerbose, TEXT("Bot [%s] goal=%d move=0x%02x action=0x%02x"),
		*BotName, (int32)Decision.CurrentGoal, Decision.MovementFlags, Decision.ActionFlags);
}

AActor* ASlopArenaBotController::FindNearestEnemy() const
{
	if (!ControlledChar) return nullptr;

	AActor* Nearest = nullptr;
	float NearestDist = FLT_MAX;

	// Check all player states for alive characters
	if (AGameStateBase* GS = GetWorld()->GetGameState())
	{
		for (APlayerState* PS : GS->PlayerArray)
		{
			if (!PS) continue;
			APawn* Pawn = PS->GetPawn();
			if (!Pawn || Pawn == ControlledChar) continue;

			float Dist = FVector::Dist2D(ControlledChar->GetActorLocation(), Pawn->GetActorLocation());
			if (Dist < NearestDist)
			{
				NearestDist = Dist;
				Nearest = Pawn;
			}
		}
	}

	return Nearest;
}
