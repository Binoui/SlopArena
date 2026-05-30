#include <cstdio>
#include <cassert>
#include <cmath>

#include "UnrealCompat.h"
#include "../Source/SlopArena/Shared/CombatMath.h"
#include "../Source/SlopArena/Shared/CombatMath.cpp"

int main() {
	int passed = 0, failed = 0;

	// Test 1: IsInCircle (Unreal convention: X/Y horizontal, Z = up)
	{
		bool hit = FCombatMath::IsInCircle(
			FVector(10, 10, 0), FVector(10, 10, 0), 5.0f);
		assert(hit);
		if (hit) passed++; else { failed++; printf("FAIL: IsInCircle center\n"); }
	}
	{
		// Edge: 4 units away horizontally, within radius 5
		bool hit = FCombatMath::IsInCircle(
			FVector(14, 10, 0), FVector(10, 10, 0), 5.0f);
		assert(hit);
		if (hit) passed++; else { failed++; printf("FAIL: IsInCircle edge\n"); }
	}
	{
		// Outside: 6 units away, radius 5
		bool miss = !FCombatMath::IsInCircle(
			FVector(16, 10, 0), FVector(10, 10, 0), 5.0f);
		assert(miss);
		if (miss) passed++; else { failed++; printf("FAIL: IsInCircle outside\n"); }
	}
	printf("IsInCircle: %d/3 passed\n", 3-failed);

	// Test 2: IsInCone (Unreal: X/Y horizontal)
	{
		// Target at X=15, Y=0 (straight ahead), cone facing +X
		bool hit = FCombatMath::IsInCone(
			FVector(15, 0, 0), FVector(0, 0, 0), FVector(1, 0, 0),
			0.785f, 20.0f);
		assert(hit);
		passed++;
	}
	{
		// Target at X=0, Y=15 (90 degrees off) should miss cone
		bool miss = !FCombatMath::IsInCone(
			FVector(0, 15, 0), FVector(0, 0, 0), FVector(1, 0, 0),
			0.785f, 20.0f);
		assert(miss);
		passed++;
	}
	{
		// Target at X=10, Y=5 (~26.6 deg) within 45 deg cone
		bool hit = FCombatMath::IsInCone(
			FVector(10, 5, 0), FVector(0, 0, 0), FVector(1, 0, 0),
			0.785f, 20.0f);
		assert(hit);
		passed++;
	}
	printf("IsInCone: %d/3 passed\n", passed);

	// Test 3: LineIntersectsCircle
	{
		bool hit = FCombatMath::LineIntersectsCircle(
			FVector2D(0, 0), FVector2D(10, 0), FVector2D(5, 0), 2.0f);
		assert(hit);
		passed++;
	}
	{
		bool miss = !FCombatMath::LineIntersectsCircle(
			FVector2D(0, 10), FVector2D(10, 10), FVector2D(5, 0), 2.0f);
		assert(miss);
		passed++;
	}
	printf("LineIntersectsCircle: %d/2 passed\n", passed);

	// Test 4: CalculateKnockback
	{
		// From (10,10) to (20,10) → +X direction, force=50, upward=10
		FVector kb = FCombatMath::CalculateKnockback(
			FVector(20, 10, 0), FVector(10, 10, 0), 50.0f, 10.0f);
		float expectedLen = std::sqrt(50.0f*50.0f + 10.0f*10.0f);
		float actualLen = std::sqrt(kb.X*kb.X + kb.Y*kb.Y + kb.Z*kb.Z);
		bool ok = std::abs(actualLen - expectedLen) < 0.1f && std::abs(kb.X - 50.0f) < 0.1f;
		assert(ok);
		if (ok) passed++; else { failed++; printf("FAIL: CalculateKnockback length\n"); }
	}
	{
		// Zero distance → default +Y
		FVector kb = FCombatMath::CalculateKnockback(
			FVector(10, 10, 0), FVector(10, 10, 0), 50.0f, 10.0f);
		bool ok = (std::abs(kb.Y - 50.0f) < 0.1f) && (std::abs(kb.Z - 10.0f) < 0.1f);
		assert(ok);
		if (ok) passed++; else { failed++; printf("FAIL: CalculateKnockback zero distance\n"); }
	}
	printf("CalculateKnockback: %d/2 passed\n", passed);

	// Test 5: HorizontalDistance (X/Y plane)
	{
		float d = FCombatMath::HorizontalDistance(FVector(0, 0, 0), FVector(3, 4, 0));
		bool ok = std::abs(d - 5.0f) < 0.001f;
		assert(ok);
		if (ok) passed++; else { failed++; printf("FAIL: HorizontalDistance\n"); }
	}
	printf("HorizontalDistance: %d/1 passed\n", passed);

	// Test 6: ClampHorizontalSpeed
	{
		FVector clamped = FCombatMath::ClampHorizontalSpeed(FVector(800, 600, 100), 500.0f);
		float hSpeed = std::sqrt(clamped.X*clamped.X + clamped.Y*clamped.Y);
		bool ok = std::abs(hSpeed - 500.0f) < 0.1f;
		assert(ok);
		if (ok) passed++; else { failed++; printf("FAIL: ClampHorizontalSpeed magnitude\n"); }
	}
	{
		FVector noClamp = FCombatMath::ClampHorizontalSpeed(FVector(300, 0, 100), 500.0f);
		bool ok = std::abs(noClamp.X - 300.0f) < 0.1f;
		assert(ok);
		if (ok) passed++; else { failed++; printf("FAIL: ClampHorizontalSpeed under limit\n"); }
	}
	printf("ClampHorizontalSpeed: %d/2 passed\n", passed);

	printf("\n=== RESULTS ===\n");
	printf("Passed: %d\nFailed: %d\nTotal:  %d\n", passed, failed, passed+failed);
	return failed > 0 ? 1 : 0;
}
