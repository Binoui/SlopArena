#include <cstdio>
#include <cassert>
#include <vector>

#include "UnrealCompat.h"
#include "../Source/SlopArena/Shared/SpellResolver.h"
#include "../Source/SlopArena/Shared/CombatMath.h"
#include "../Source/SlopArena/Shared/CombatMath.cpp"
#include "../Source/SlopArena/Shared/SpellResolver.cpp"

int main() {
	int passed = 0, failed = 0;
	auto check = [&](bool ok, const char* name) {
		if (ok) passed++; else { failed++; printf("FAIL: %s\n", name); }
	};

	// Test projectile hit detection
	// Entity at (10, 5) in X/Y plane — projectile from (0,0) to (20,0) passes through (10,0)
	{
		std::vector<FSpellResolver::FEntityData> entities;
		entities.push_back({1, FVector(10, 0, 0), 2.0f, true});  // On the line
		entities.push_back({2, FVector(50, 50, 0), 2.0f, true}); // Off the line

		TArrayView<const FSpellResolver::FEntityData> view(
			entities.data(), (int32)entities.size());

		auto result = FSpellResolver::ResolveProjectileHit(
			FVector2D(0, 0), FVector2D(20, 0),
			FVector(0, 0, 0),
			25.0f, 30.0f, 5.0f,
			0, // caster
			view
		);

		check(result.has_value(), "Projectile should hit entity at (10,0)");
		if (result.has_value()) {
			check(result->TargetEntityId == 1, "Hit entity ID 1");
			check(std::abs(result->Damage - 25.0f) < 0.001f, "Damage 25");
		}
	}
	printf("SpellResolver projectile: %d tests\n", passed);

	// Test cone hit
	{
		std::vector<FSpellResolver::FEntityData> entities;
		// Entity 1 at (0, 100) — 90 degrees left of cone direction (+X), should miss
		entities.push_back({1, FVector(0, 100, 0), 2.0f, true});
		// Entity 2 at (300, 0) — directly in cone direction, should hit
		entities.push_back({2, FVector(300, 0, 0), 2.0f, true});

		TArrayView<const FSpellResolver::FEntityData> view(
			entities.data(), (int32)entities.size());

		auto results = FSpellResolver::ResolveConeHit(
			FVector(0, 0, 0),      // Origin
			FVector(1, 0, 0),       // Direction = +X
			0.785f,                  // 45 deg half-angle
			500.0f,                  // Range
			20.0f, 15.0f, 5.0f,
			0,
			view
		);

		check(results.Num() == 1, "Cone hits exactly 1 entity");
		if (results.Num() == 1) {
			check(results[0].TargetEntityId == 2, "Cone hit entity 2");
		}
	}
	printf("SpellResolver cone: %d tests\n", passed);

	// Test circle AoE hit
	{
		std::vector<FSpellResolver::FEntityData> entities;
		// Entity 1 at (95, 0) — within radius 30 from center (100,0) + entity radius 5 = 35
		entities.push_back({1, FVector(95, 0, 0), 5.0f, true});
		// Entity 2 at (200, 200) — outside
		entities.push_back({2, FVector(200, 200, 0), 2.0f, true});

		TArrayView<const FSpellResolver::FEntityData> view(
			entities.data(), (int32)entities.size());

		auto results = FSpellResolver::ResolveCircleHit(
			FVector(100, 0, 0),     // Center at (100, 0)
			30.0f,                    // Radius 30
			15.0f, 20.0f, 10.0f,
			0,
			view
		);

		check(results.Num() == 1, "Circle hits exactly 1 entity");
		if (results.Num() == 1) {
			check(results[0].TargetEntityId == 1, "Circle hit entity 1");
		}
	}
	printf("SpellResolver circle: %d tests\n", passed);

	printf("\n=== RESULTS ===\n");
	printf("Passed: %d\nFailed: %d\nTotal:  %d\n", passed, failed, passed+failed);
	return failed > 0 ? 1 : 0;
}
