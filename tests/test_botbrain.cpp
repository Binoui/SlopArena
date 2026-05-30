#include <cstdio>
#include <cassert>
#include <cmath>

#include "UnrealCompat.h"
#include "../Source/SlopArena/AI/BotBrain.h"
#include "../Source/SlopArena/AI/BotBrain.cpp"

int main() {
	int passed = 0, failed = 0;
	auto check = [&](bool ok, const char* name) {
		if (ok) passed++; else { failed++; printf("FAIL: %s\n", name); }
	};
	auto checkFlags = [&](uint8 actual, uint8 expected, const char* name) {
		if ((actual & expected) == expected) passed++;
		else { failed++; printf("FAIL: %s (flags 0x%02x missing 0x%02x)\n", name, actual, expected); }
	};

	// ==========================================
	// Test 1: Idle when no target
	// ==========================================
	{
		FBotPerception P;
		P.MyPosition = FVector(0, 0, 0);
		P.MyHP = 100.0f;
		P.ArenaRadius = 2000.0f;

		auto D = FBotBrain::Decide(P);
		check(D.CurrentGoal == EBotGoal::Search, "Search when no target");
		check(D.MovementFlags == 0, "No movement when no target");
		check(D.ActionFlags == 0, "No action when no target");
	}
	printf("  Idle: %d\n", passed);

	// ==========================================
	// Test 2: Chase target
	// ==========================================
	{
		FBotPerception P;
		P.MyPosition = FVector(0, 0, 0);
		P.bHasTarget = true;
		P.TargetPosition = FVector(1000, 0, 0);
		P.DistanceToTarget = 1000.0f;
		P.MyHP = 100.0f;
		P.ArenaRadius = 2000.0f;
		P.DashCooldown = 0.0f;

		auto D = FBotBrain::Decide(P);
		check(D.CurrentGoal == EBotGoal::Chase, "Chases target");
		check(D.MovementFlags != 0, "Moving toward target");
		// Should dash when far
		checkFlags(D.MovementFlags, 0x20, "Dashes when far from target");
	}
	printf("  Chase: %d\n", passed);

	// ==========================================
	// Test 3: Attack when in range
	// ==========================================
	{
		FBotPerception P;
		P.MyPosition = FVector(0, 0, 0);
		P.bHasTarget = true;
		P.TargetPosition = FVector(200, 0, 0);
		P.DistanceToTarget = 200.0f;
		P.MyHP = 100.0f;
		P.ArenaRadius = 2000.0f;

		auto D = FBotBrain::Decide(P);
		check(D.CurrentGoal == EBotGoal::Attack, "Attacks in melee range");
		checkFlags(D.ActionFlags, 0x01, "Uses light attack");
	}
	printf("  Attack: %d\n", passed);

	// ==========================================
	// Test 4: Retreat from edge
	// ==========================================
	{
		FBotPerception P;
		P.MyPosition = FVector(1800, 0, 0); // Near edge (75% of 2000 = 1500)
		P.bHasTarget = true;
		P.TargetPosition = FVector(2000, 0, 0);
		P.DistanceToTarget = 200.0f;
		P.MyHP = 100.0f;
		P.ArenaRadius = 2000.0f;

		auto D = FBotBrain::Decide(P);
		check(D.CurrentGoal == EBotGoal::Retreat, "Retreats from edge");
	}
	printf("  Retreat from edge: %d\n", passed);

	// ==========================================
	// Test 5: Use abilities on cooldown
	// ==========================================
	{
		FBotPerception P;
		P.MyPosition = FVector(0, 0, 0);
		P.bHasTarget = true;
		P.TargetPosition = FVector(500, 0, 0);
		P.DistanceToTarget = 500.0f;
		P.MyHP = 100.0f;
		P.ArenaRadius = 2000.0f;
		P.UltimateCooldown = 99.0f; // Not ready
		P.Ability1Cooldown = 0.0f;  // Ready

		auto D = FBotBrain::Decide(P);
		check(D.CurrentGoal == EBotGoal::Attack, "Uses ability in range");
		checkFlags(D.ActionFlags, 0x08, "Fires ability 1");
	}
	printf("  Abilities: %d\n", passed);

	// ==========================================
	// Test 6: Don't use abilities on cooldown
	// ==========================================
	{
		FBotPerception P;
		P.MyPosition = FVector(0, 0, 0);
		P.bHasTarget = true;
		P.TargetPosition = FVector(500, 0, 0);
		P.DistanceToTarget = 500.0f;
		P.MyHP = 100.0f;
		P.ArenaRadius = 2000.0f;
		P.Ability1Cooldown = 3.0f; // Not ready
		P.Ability2Cooldown = 5.0f;
		P.Ability3Cooldown = 2.0f;
		P.UltimateCooldown = 99.0f; // Not ready

		auto D = FBotBrain::Decide(P);
		printf("  Debug: goal=%d, flags=0x%02x, action=0x%02x\n",
			(int)D.CurrentGoal, D.MovementFlags, D.ActionFlags);
		// Should not attack because all abilities on cooldown and distance > 300
		check(D.CurrentGoal == EBotGoal::Chase, "Chases instead of using abilities on cooldown");
	}
	printf("  Cooldown respect: %d\n", passed);

	// ==========================================
	// Test 7: Ultimate when ready and in range
	// ==========================================
	{
		FBotPerception P;
		P.MyPosition = FVector(0, 0, 0);
		P.bHasTarget = true;
		P.TargetPosition = FVector(400, 0, 0);
		P.DistanceToTarget = 400.0f;
		P.MyHP = 100.0f;
		P.ArenaRadius = 2000.0f;
		P.UltimateCooldown = 0.0f;

		auto D = FBotBrain::Decide(P);
		check(D.CurrentGoal == EBotGoal::Attack, "Uses ultimate when ready");
		checkFlags(D.ActionFlags, 0x40, "Fires ultimate");
	}
	printf("  Ultimate: %d\n", passed);

	// ==========================================
	// Test 8: Stay in center when at edge (no target)
	// ==========================================
	{
		FBotPerception P;
		P.MyPosition = FVector(1800, 0, 0); // Near edge
		P.bHasTarget = false;
		P.MyHP = 100.0f;
		P.ArenaRadius = 2000.0f;

		auto D = FBotBrain::Decide(P);
		check(D.CurrentGoal == EBotGoal::Retreat, "Retreats from edge even without target");
		check(D.MovementFlags != 0, "Moves back toward center");
	}
	printf("  Edge safety: %d\n", passed);

	printf("\n=== BOT BRAIN RESULTS ===\n");
	printf("Passed: %d\nFailed: %d\nTotal:  %d\n", passed, failed, passed + failed);
	return failed > 0 ? 1 : 0;
}
