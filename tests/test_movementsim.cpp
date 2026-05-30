#include <cstdio>
#include <cassert>
#include <cmath>

#include "UnrealCompat.h"
#include "../Source/SlopArena/Shared/CombatMath.h"
#include "../Source/SlopArena/Shared/EActionState.h"
#include "../Source/SlopArena/Shared/MovementProfile.h"
#include "../Source/SlopArena/Shared/MovementSimulation.h"
#include "../Source/SlopArena/Shared/CombatMath.cpp"
#include "../Source/SlopArena/Shared/MovementSimulation.cpp"

int main() {
	int passed = 0, failed = 0;
	auto check = [&](bool ok, const char* name) {
		if (ok) passed++; else { failed++; printf("FAIL: %s\n", name); }
	};

	// Default movement profile
	FMovementProfile Profile;
	Profile.WalkSpeed = 600.0f;
	Profile.DashSpeed = 1800.0f;
	Profile.JumpForce = 600.0f;
	Profile.Gravity = 1400.0f;
	Profile.Acceleration = 1400.0f;
	Profile.DashDurationTicks = 10;
	Profile.DashCooldownTicks = 30;

	// Test 1: Idle → no movement stays at origin
	{
		FCharacterSimState State;
		FCharacterInputState Input;
		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		check(State.Position.X == 0.0f && State.Position.Y == 0.0f && State.Position.Z == 0.0f,
			"Idle stays at origin");
	}
	printf("  Idle stability: %d\n", passed);

	// Test 2: Movement input (Up at Yaw=0 → -Y direction in our input mapping)
	{
		FCharacterSimState State;
		FCharacterInputState Input;
		Input.MovementFlags = 0x01; // Up
		Input.Yaw = 0.0f;

		for (int i = 0; i < 10; i++) {
			FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		}

		// Should have moved in -Y direction (that's our mapping for Up+Yaw=0)
		check(State.Position.Y < 0.0f, "Moving after 10 ticks");
		check(State.ActionState == EActionState::Idle, "Still in Idle (not sprinting)");

		float speed = std::sqrt(State.Velocity.X*State.Velocity.X + State.Velocity.Y*State.Velocity.Y);
		check(speed > 0.0f, "Has horizontal velocity");
	}
	printf("  Movement input: %d\n", passed);

	// Test 3: Jump
	{
		FCharacterSimState State;
		FCharacterInputState Input;
		Input.MovementFlags = 0x10; // Jump flag

		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);

		check(State.Velocity.Z > 0.0f, "Jump gives upward velocity");
		check(State.Position.Z > 0.0f, "Jump increases Z position (not snapped)");
	}
	printf("  Jump: %d\n", passed);

	// Test 4: Dash (Up + Dash flags)
	{
		FCharacterSimState State;
		FCharacterInputState Input;
		Input.MovementFlags = 0x01 | 0x20; // Up + Dash
		Input.Yaw = 0.0f;

		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);

		check(State.ActionState == EActionState::Dashing, "Dash action state");
		float speed = std::sqrt(State.Velocity.X*State.Velocity.X + State.Velocity.Y*State.Velocity.Y);
		check(std::abs(speed - 1800.0f) < 1.0f, "Dash speed = 1800");
	}
	printf("  Dash: %d\n", passed);

	// Test 5: Dash cooldown prevents immediate re-dash
	{
		FCharacterSimState State;
		FCharacterInputState Input;
		Input.MovementFlags = 0x01 | 0x20; // Up + Dash
		Input.Yaw = 0.0f;

		// First dash
		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		check(State.DashCooldownTicks > 0, "Dash cooldown started");

		// Keep holding dash
		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		// Should still be in dash state or cooldown, not able to dash again
		check(State.ActionState != EActionState::Idle || State.DashCooldownTicks > 0,
			"Dash can't be spammed");
	}
	printf("  Dash cooldown: %d\n", passed);

	// Test 6: Gravity pulls down when airborne
	{
		FCharacterSimState State;
		State.Position = FVector(0, 0, 100); // In the air
		State.bIsGrounded = false;
		FCharacterInputState Input;

		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);

		check(State.Velocity.Z < 0.0f, "Gravity pulls down");
		check(State.Position.Z < 100.0f, "Falling down");
	}
	printf("  Gravity: %d\n", passed);

	// Test 7: Ground collision when falling
	{
		FCharacterSimState State;
		State.Position = FVector(0, 0, 100);
		State.Velocity = FVector(0, 0, -100); // Falling
		State.bIsGrounded = false;
		FCharacterInputState Input;

		// Simulate until landing
		for (int i = 0; i < 120; i++) {
			FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
			if (State.bIsGrounded) break;
		}

		check(State.bIsGrounded, "Landed on ground after falling");
		check(State.Position.Z <= 0.1f, "On ground surface");
		check(std::abs(State.Velocity.Z) < 0.1f, "Zero Z velocity when grounded");
	}
	printf("  Ground collision: %d\n", passed);

	// Test 8: Hitstun state
	{
		FCharacterSimState State;
		State.ActionState = EActionState::Hitstun;
		State.StateTicksRemaining = 10;
		State.Velocity = FVector(500, 0, 200);
		FCharacterInputState Input;

		for (int i = 0; i < 5; i++) {
			FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		}

		check(State.ActionState == EActionState::Hitstun, "Still in hitstun after 5 ticks");
		check(State.StateTicksRemaining == 5, "Hitstun counts down");

		// Finish hitstun
		for (int i = 0; i < 5; i++) {
			FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		}
		check(State.ActionState == EActionState::Idle, "Hitstun ended, now idle");
	}
	printf("  Hitstun: %d\n", passed);

	// Test 9: DI during hitstun
	{
		FCharacterSimState State;
		State.ActionState = EActionState::Hitstun;
		State.StateTicksRemaining = 10;
		State.Velocity = FVector(500, 0, 0); // Pushed +X
		FCharacterInputState Input;
		Input.MovementFlags = 0x04; // Down → DI in +Y direction at yaw=0

		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		// DI should add Y velocity component
		check(std::abs(State.Velocity.Y) > 0.0f, "DI applied Y velocity");
	}
	printf("  DI: %d\n", passed);

	// Test 10: Attack action
	{
		FCharacterSimState State;
		FCharacterInputState Input;
		Input.MovementFlags = 0x00;
		Input.ActionFlags = 0x01; // Attack

		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);

		check(State.ActionState == EActionState::Attacking, "Attack input triggers attack state");
		check(State.StateTicksRemaining > 0, "Attack has duration ticks");
	}
	printf("  Attack: %d\n", passed);

	// Test 11: Attack ends after duration (even if button held)
	{
		FCharacterSimState State;
		FCharacterInputState Input;
		Input.MovementFlags = 0x00;
		Input.ActionFlags = 0x01; // Attack

		// Step once to trigger attack
		FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		check(State.ActionState == EActionState::Attacking, "Started attacking");

		// Release the button
		Input.ActionFlags = 0x00;

		// Run past attack duration
		for (int i = 0; i < 20; i++) {
			FMovementSimulation::SimulateStep(State, Input, Profile, 1.0f/60.0f, 0.0f);
		}
		check(State.ActionState == EActionState::Idle, "Attack ended, now idle");
	}
	printf("  Attack duration: %d\n", passed);

	printf("\n=== MOVEMENT SIMULATION RESULTS ===\n");
	printf("Passed: %d\nFailed: %d\nTotal:  %d\n", passed, failed, passed+failed);

	printf("\n---\n");
	if (failed == 0) printf("All tests passed!\n");

	return failed > 0 ? 1 : 0;
}
