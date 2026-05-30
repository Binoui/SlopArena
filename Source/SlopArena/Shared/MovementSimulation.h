// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"
#include "EActionState.h"
#include "MovementProfile.h"

/**
 * Pure movement simulation - no Unreal actor dependencies.
 * Designed to be used by NetworkPrediction's TNetworkedSimulationModel.
 * This is the C++ port of the Godot PhysicsConfig.cs simulation loop.
 */
struct SLOPARENA_API FMovementSimulation
{
	/** Simulate one tick (1/60s) of movement. */
	static void SimulateStep(
		FCharacterSimState& OutState,
		const FCharacterInputState& Input,
		const FMovementProfile& Profile,
		float DeltaTime,
		float GroundHeight,
		float Radius = 90.0f);

	/** Resolve movement input from flags and yaw. Returns normalized direction and max speed. */
	static void ResolveMovementInput(
		const FCharacterInputState& Input,
		float Yaw,
		FVector2D& OutDirection,
		float& OutMaxSpeed);

private:
	static void BeginSlide(EActionState& ActionState, uint8& StateTicksRemaining, bool& bSlideMomentum, bool CanMomentum);
	static bool CanMomentumSlide(const FMovementProfile& Profile, uint8 CombatLockout, float Speed2D);
	static void ClampSlideSpeed(FVector& Velocity, const FMovementProfile& Profile, bool bMomentum);
};
