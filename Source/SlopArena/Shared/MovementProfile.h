// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "EActionState.h"
#include "MovementProfile.generated.h"

USTRUCT(BlueprintType)
struct FMovementProfile
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement")
	float Acceleration = 1400.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement")
	float AirAcceleration = 500.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement")
	float WalkSpeed = 600.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement")
	float DashSpeed = 1800.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement")
	float DragWhenStopped = 12.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement")
	float DragWhenMoving = 6.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Dash")
	uint8 DashDurationTicks = 10;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Dash")
	uint8 DashCooldownTicks = 30;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Slide")
	float SlideMomentumDrag = 1.5f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Slide")
	float SlideNormalDrag = 6.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Slide")
	float SlideMomentumMinSpeed = 300.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Combat")
	uint8 AttackDurationTicks = 12;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Combat")
	uint8 PostAttackSlideLockoutTicks = 15;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Jump")
	float JumpForce = 600.0f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movement|Jump")
	float Gravity = 1400.0f;
};

USTRUCT(BlueprintType)
struct FCharacterInputState
{
	GENERATED_BODY()

	uint8 MovementFlags = 0; // Bitfield: Up, Left, Down, Right, Jump, Dash, Crouch, Respawn
	uint8 ActionFlags = 0;   // Bitfield: Attack
	float Yaw = 0.0f;        // Camera yaw for direction-relative movement
};

USTRUCT(BlueprintType)
struct FCharacterSimState
{
	GENERATED_BODY()

	FVector Position = FVector::ZeroVector;
	FVector Velocity = FVector::ZeroVector;
	float GroundHeight = 0.0f;

	EActionState ActionState = EActionState::Idle;
	uint8 StateTicksRemaining = 0;
	uint8 DashCooldownTicks = 0;
	uint8 CombatLockoutTicks = 0;

	bool bIsGrounded = false;
	bool bSlideMomentumActive = false;
};
