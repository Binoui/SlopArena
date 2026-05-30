// Copyright SlopArena Contributors. MIT License.

#include "BotBrain.h"
#include <cmath>

// =====================================================================
// STRATEGY: Reactive priority-based AI
//
// Priority order:
//   1. DEFENSE — dodge incoming projectiles, avoid ring-out
//   2. COMBAT — attack if target in range, use abilities
//   3. MOVEMENT — chase target or retreat
// =====================================================================

FBotDecision FBotBrain::Decide(const FBotPerception& P)
{
	FBotDecision D;

	// 1. Defense first (survival)
	DecideDefense(P, D);
	if (D.CurrentGoal == EBotGoal::Retreat)
		return D; // Full focus on survival

	// 2. Combat if target exists
	if (P.bHasTarget)
	{
		DecideAbilities(P, D);
		DecideCombat(P, D);
	}

	// 3. Movement toward/away from target
	DecideMovement(P, D);

	return D;
}

// =====================================================================
// DEFENSE
// =====================================================================
void FBotBrain::DecideDefense(const FBotPerception& P, FBotDecision& D)
{
	// Check ring-out danger — are we near the edge?
	float DistFromCenter = std::sqrt(P.MyPosition.X * P.MyPosition.X + P.MyPosition.Y * P.MyPosition.Y);
	float EdgeThreshold = P.ArenaRadius * 0.75f; // 75% of arena = danger zone

	if (DistFromCenter > EdgeThreshold)
	{
		// Move back toward center
		float Angle = std::atan2(-P.MyPosition.Y, -P.MyPosition.X);
		D.Yaw = Angle;

		// Set movement toward center
		// Use the angle to determine which movement flags
		// For simplicity: move in the direction that points toward center
		if (std::abs(P.MyPosition.X) > std::abs(P.MyPosition.Y))
		{
			D.MovementFlags |= (P.MyPosition.X > 0) ? 0x04 : 0x01; // Down or Up
		}
		else
		{
			D.MovementFlags |= (P.MyPosition.Y > 0) ? 0x08 : 0x02; // Right or Left
		}

		D.CurrentGoal = EBotGoal::Retreat;
		return;
	}

	// Dodge incoming projectiles
	if (P.bIncomingProjectile && P.TimeUntilProjectileHit < 1.0f)
	{
		// Dash perpendicular to projectile direction
		D.MovementFlags |= 0x20; // Dash
		D.CurrentGoal = EBotGoal::Retreat;
		return;
	}
}

// =====================================================================
// COMBAT — choose attack type based on distance
// =====================================================================
void FBotBrain::DecideCombat(const FBotPerception& P, FBotDecision& D)
{
	if (!P.bHasTarget) return;

	// Light attack range: ~250 units
	// Heavy attack range: ~350 units
	// Ability range: varies

	if (P.DistanceToTarget < 300.0f)
	{
		// In melee range — light attack
		D.ActionFlags |= 0x01; // Attack
		D.CurrentGoal = EBotGoal::Attack;
	}
}

// =====================================================================
// ABILITIES — use on cooldown when in range
// =====================================================================
void FBotBrain::DecideAbilities(const FBotPerception& P, FBotDecision& D)
{
	if (!P.bHasTarget) return;

	// Use ultimate when ready and in range
	if (P.UltimateCooldown <= 0.0f && P.DistanceToTarget < 800.0f)
	{
		D.ActionFlags |= 0x40; // Ultimate (bit 6)
		D.CurrentGoal = EBotGoal::Attack;
		return;
	}

	// Use ability 1 at mid range
	if (P.Ability1Cooldown <= 0.0f && P.DistanceToTarget < 600.0f)
	{
		D.ActionFlags |= 0x08; // Ability 1 (bit 3)
		D.CurrentGoal = EBotGoal::Attack;
		return;
	}

	// Use ability 2 at any range
	if (P.Ability2Cooldown <= 0.0f && P.DistanceToTarget < 1000.0f)
	{
		D.ActionFlags |= 0x10; // Ability 2 (bit 4)
		D.CurrentGoal = EBotGoal::Attack;
		return;
	}

	// Use ability 3 close range (usually escape/defense)
	if (P.Ability3Cooldown <= 0.0f && P.DistanceToTarget < 400.0f)
	{
		D.ActionFlags |= 0x20; // Ability 3 (bit 5)
		D.CurrentGoal = EBotGoal::Attack;
	}
}

// =====================================================================
// MOVEMENT — chase or maintain spacing
// =====================================================================
void FBotBrain::DecideMovement(const FBotPerception& P, FBotDecision& D)
{
	if (!P.bHasTarget)
	{
		D.CurrentGoal = EBotGoal::Search;
		return;
	}

	// If we're attacking, don't override movement
	if (D.CurrentGoal == EBotGoal::Attack)
		return;

	// Chase target
	D.CurrentGoal = EBotGoal::Chase;

	// Calculate angle toward target
	float Dx = P.TargetPosition.X - P.MyPosition.X;
	float Dy = P.TargetPosition.Y - P.MyPosition.Y;
	float Angle = std::atan2(Dy, Dx);

	// Set yaw to face target
	D.Yaw = Angle; // In Unreal's convention, yaw = atan2(Y, X) roughly

	// Movement toward target
	// Simple: if far, move directly; if close, strafe slightly
	if (P.DistanceToTarget > 500.0f)
	{
		// Direct chase
		if (std::abs(Dx) > std::abs(Dy))
		{
			D.MovementFlags |= (Dx > 0) ? 0x01 : 0x04; // Up(+X) or Down(-X) relative to yaw
		}
		else
		{
			D.MovementFlags |= (Dy > 0) ? 0x08 : 0x02; // Right(+Y) or Left(-Y)
		}

		// Dash when far
		if (P.DashCooldown <= 0.0f && P.DistanceToTarget > 800.0f)
		{
			D.MovementFlags |= 0x20;
		}
	}
}

FString FBotBrain::GetStrategyName()
{
	return TEXT("ReactivePriority");
}
