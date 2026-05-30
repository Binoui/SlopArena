// Copyright SlopArena Contributors. MIT License.
// NOTE: Unreal convention — X/Y = horizontal plane, Z = up axis

#include "CombatMath.h"

bool FCombatMath::IsInCircle(const FVector& Target, const FVector& Center, float Radius)
{
	float Dx = Target.X - Center.X;
	float Dy = Target.Y - Center.Y;
	return (Dx * Dx + Dy * Dy) <= Radius * Radius;
}

bool FCombatMath::IsInCone(const FVector& Target, const FVector& Origin, const FVector& Direction, float HalfAngleRad, float Range)
{
	float Dx = Target.X - Origin.X;
	float Dy = Target.Y - Origin.Y;
	float DistSq = Dx * Dx + Dy * Dy;

	if (DistSq > Range * Range)
		return false;

	if (DistSq < 0.001f)
		return true;

	float Dist = FMath::Sqrt(DistSq);
	float Dot = (Dx * Direction.X + Dy * Direction.Y) / Dist;
	Dot = FMath::Clamp(Dot, -1.0f, 1.0f);
	float Angle = FMath::Acos(Dot);
	return Angle <= HalfAngleRad;
}

bool FCombatMath::LineIntersectsCircle(
	const FVector2D& LineStart, const FVector2D& LineEnd,
	const FVector2D& CircleCenter, float Radius)
{
	FVector2D D = LineEnd - LineStart;
	FVector2D F = LineStart - CircleCenter;

	float A = D.X * D.X + D.Y * D.Y;
	float B = 2.0f * (F.X * D.X + F.Y * D.Y);
	float C = F.X * F.X + F.Y * F.Y - Radius * Radius;

	float Discriminant = B * B - 4.0f * A * C;
	if (Discriminant < 0.0f)
		return false;

	Discriminant = FMath::Sqrt(Discriminant);
	float T1 = (-B - Discriminant) / (2.0f * A);
	float T2 = (-B + Discriminant) / (2.0f * A);

	return (T1 >= 0.0f && T1 <= 1.0f) || (T2 >= 0.0f && T2 <= 1.0f) || (T1 < 0.0f && T2 > 1.0f);
}

FVector FCombatMath::CalculateKnockback(const FVector& TargetPos, const FVector& AttackerPos, float Force, float Upward)
{
	float Dx = TargetPos.X - AttackerPos.X;
	float Dy = TargetPos.Y - AttackerPos.Y;
	float DistSq = Dx * Dx + Dy * Dy;

	if (DistSq > 0.001f)
	{
		float Dist = FMath::Sqrt(DistSq);
		return FVector((Dx / Dist) * Force, (Dy / Dist) * Force, Upward);
	}

	return FVector(0.0f, Force, Upward);
}

float FCombatMath::HorizontalDistance(const FVector& A, const FVector& B)
{
	float Dx = B.X - A.X;
	float Dy = B.Y - A.Y;
	return FMath::Sqrt(Dx * Dx + Dy * Dy);
}

FVector FCombatMath::ClampHorizontalSpeed(const FVector& Velocity, float MaxSpeed)
{
	FVector Horizontal(Velocity.X, Velocity.Y, 0.0f);
	float Speed = Horizontal.Length();
	if (Speed > MaxSpeed)
	{
		float Scale = MaxSpeed / Speed;
		return FVector(Velocity.X * Scale, Velocity.Y * Scale, Velocity.Z);
	}
	return Velocity;
}
