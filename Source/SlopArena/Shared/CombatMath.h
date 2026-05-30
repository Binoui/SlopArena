// Copyright SlopArena Contributors. MIT License.

#pragma once

#include "CoreMinimal.h"

struct SLOPARENA_API FCombatMath
{
	/** Check if a point is within a horizontal circle (cylinder). Uses X,Y as horizontal (Unreal convention). */
	static bool IsInCircle(const FVector& Target, const FVector& Center, float Radius);

	/** Check if a point is within a cone originating from a position (2D horizontal, X/Y plane). */
	static bool IsInCone(const FVector& Target, const FVector& Origin, const FVector& Direction, float HalfAngleRad, float Range);

	/** Check if a line segment intersects a circle (2D horizontal, X/Y plane). */
	static bool LineIntersectsCircle(const FVector2D& LineStart, const FVector2D& LineEnd, const FVector2D& CircleCenter, float Radius);

	/** Calculate knockback direction and force from attacker to target (horizontal X/Y). */
	static FVector CalculateKnockback(const FVector& TargetPos, const FVector& AttackerPos, float Force, float Upward);

	/** Horizontal distance between two points (X/Y plane). */
	static float HorizontalDistance(const FVector& A, const FVector& B);

	/** Clamp velocity magnitude. */
	static FVector ClampHorizontalSpeed(const FVector& Velocity, float MaxSpeed);
};
