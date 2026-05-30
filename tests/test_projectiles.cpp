#include <cstdio>
#include <cassert>
#include <cmath>

#include "UnrealCompat.h"
#include "../Source/SlopArena/Shared/CombatMath.h"
#include "../Source/SlopArena/Shared/SpellResolver.h"
#include "../Source/SlopArena/Shared/CombatMath.cpp"
#include "../Source/SlopArena/Shared/SpellResolver.cpp"

// Minimal FProjectileState + hit detection (avoid including full ProjectileManager which needs Unreal)
#include "../Source/SlopArena/Combat/ProjectileState.h"

// Minimal TMap-like tracker for testing
struct FTestProjectile {
	FProjectileState State;
	bool bActive = true;
};

int main() {
	int passed = 0, failed = 0;
	auto check = [&](bool ok, const char* name) {
		if (ok) passed++; else { failed++; printf("FAIL: %s\n", name); }
	};
	auto checkInt = [&](int actual, int expected, const char* name) {
		if (actual == expected) passed++; else { failed++; printf("FAIL: %s (got %d, expected %d)\n", name, actual, expected); }
	};

	// ==========================================
	// Test 1: FProjectileState tick
	// ==========================================
	{
		FProjectileState Proj;
		Proj.Position = FVector(0, 0, 0);
		Proj.Direction = FVector(1, 0, 0).GetSafeNormal();
		Proj.Speed = 1000.0f;
		Proj.MaxRange = 5000.0f;
		Proj.bActive = true;
		Proj.ResetOldPosition();

		for (int i = 0; i < 10; i++) {
			bool alive = Proj.SimulateStep(1.0f / 60.0f);
			check(alive, "Alive after 10 ticks");
		}

		float expected = 1000.0f * 10.0f / 60.0f;
		check(std::abs(Proj.Position.X - expected) < 1.0f, "Projectile moved correct distance");
		check(std::abs(Proj.DistanceTraveled - expected) < 1.0f, "Distance traveled matches");
	}
	printf("  State tick: %d\n", passed);

	// ==========================================
	// Test 2: Range expiration
	// ==========================================
	{
		FProjectileState Proj;
		Proj.Position = FVector(0, 0, 0);
		Proj.Direction = FVector(1, 0, 0).GetSafeNormal();
		Proj.Speed = 1000.0f;
		Proj.MaxRange = 500.0f;
		Proj.bActive = true;
		Proj.ResetOldPosition();

		bool wasAlive = false;
		for (int i = 0; i < 100; i++) {
			bool alive = Proj.SimulateStep(1.0f / 60.0f);
			if (alive) wasAlive = true;
			if (!alive) break;
		}

		check(wasAlive, "Was alive");
		check(!Proj.bActive, "Expired");
		check(std::abs(Proj.DistanceTraveled - 500.0f) < 25.0f, "Expired at ~500 range");
	}
	printf("  Range expiration: %d\n", passed);

	// ==========================================
	// Test 3: Hit detection using CombatMath directly
	// ==========================================
	{
		FProjectileState Proj;
		Proj.Position = FVector(0, 0, 0);
		Proj.Direction = FVector(1, 0, 0).GetSafeNormal();
		Proj.Speed = 2000.0f;
		Proj.MaxRange = 3000.0f;
		Proj.Damage = 25.0f;
		Proj.KnockbackForce = 30.0f;
		Proj.KnockbackUpward = 5.0f;
		Proj.Radius = 50.0f;
		Proj.bActive = true;
		Proj.ResetOldPosition();

		// Entity at (500, 0) with radius 90
		FSpellResolver::FEntityData Target;
		Target.Id = 42;
		Target.Position = FVector(500, 0, 0);
		Target.Radius = 90.0f;
		Target.bActive = true;
		TArray<FSpellResolver::FEntityData> Entities;
		Entities.Add(Target);

		// Simulate until hit or timeout
		bool bHit = false;
		FHitResultStruct HitResult;
		for (int i = 0; i < 60; i++) {
			FVector OldPos = Proj.Position;
			bool alive = Proj.SimulateStep(1.0f / 60.0f);
			if (!alive) break;

			// Line intersection check
			for (const auto& Entity : Entities) {
				if (Entity.Id == 0) continue; // No caster filter needed here
				bool hit = FCombatMath::LineIntersectsCircle(
					FVector2D(OldPos.X, OldPos.Y),
					FVector2D(Proj.Position.X, Proj.Position.Y),
					FVector2D(Entity.Position.X, Entity.Position.Y),
					Proj.Radius + Entity.Radius
				);
				if (hit) {
					bHit = true;
					FVector KB = FCombatMath::CalculateKnockback(
						Entity.Position, Proj.Position,
						Proj.KnockbackForce, Proj.KnockbackUpward);
					HitResult = {Entity.Id, Proj.Damage, KB};
					break;
				}
			}
			if (bHit) break;
		}

		check(bHit, "Projectile hits entity at (500,0)");
		check(HitResult.TargetEntityId == 42, "Hit entity ID 42");
		check(std::abs(HitResult.Damage - 25.0f) < 0.1f, "Damage = 25");
	}
	printf("  Hit detection: %d\n", passed);

	// ==========================================
	// Test 4: Miss (entity not in path)
	// ==========================================
	{
		FProjectileState Proj;
		Proj.Position = FVector(0, 0, 0);
		Proj.Direction = FVector(1, 0, 0).GetSafeNormal(); // +X
		Proj.Speed = 2000.0f;
		Proj.MaxRange = 3000.0f;
		Proj.Radius = 50.0f;
		Proj.bActive = true;
		Proj.ResetOldPosition();

		// Entity at (0, 2000) — far off the +X path
		FSpellResolver::FEntityData Target;
		Target.Id = 99;
		Target.Position = FVector(0, 2000, 0);
		Target.Radius = 90.0f;
		Target.bActive = true;
		TArray<FSpellResolver::FEntityData> Entities;
		Entities.Add(Target);

		bool bHit = false;
		for (int i = 0; i < 120; i++) {
			FVector OldPos = Proj.Position;
			bool alive = Proj.SimulateStep(1.0f / 60.0f);
			if (!alive) break;

			for (const auto& Entity : Entities) {
				bool hit = FCombatMath::LineIntersectsCircle(
					FVector2D(OldPos.X, OldPos.Y),
					FVector2D(Proj.Position.X, Proj.Position.Y),
					FVector2D(Entity.Position.X, Entity.Position.Y),
					Proj.Radius + Entity.Radius
				);
				if (hit) { bHit = true; break; }
			}
			if (bHit) break;
		}

		check(!bHit, "Projectile misses entity at (0, 2000)");
	}
	printf("  Miss: %d\n", passed);

	// ==========================================
	// Test 5: OldPosition tracks correctly
	// ==========================================
	{
		FProjectileState Proj;
		Proj.Position = FVector(100, 200, 0);
		Proj.Direction = FVector(1, 0, 0).GetSafeNormal();
		Proj.Speed = 500.0f;
		Proj.MaxRange = 10000.0f;
		Proj.bActive = true;
		Proj.ResetOldPosition();

		FVector oldBefore = Proj.GetOldPosition();
		check(std::abs(oldBefore.X - 100.0f) < 0.1f, "OldPosition = position before tick");

		Proj.SimulateStep(1.0f / 60.0f);

		FVector oldAfter = Proj.GetOldPosition();
		check(std::abs(oldAfter.X - 100.0f) < 0.1f, "OldPosition = pre-tick position");
		check(std::abs(Proj.Position.X - 108.33f) < 1.0f, "New position advanced");
	}
	printf("  OldPosition tracking: %d\n", passed);

	printf("\n=== PROJECTILE SYSTEM RESULTS ===\n");
	printf("Passed: %d\nFailed: %d\nTotal:  %d\n", passed, failed, passed + failed);
	return failed > 0 ? 1 : 0;
}
